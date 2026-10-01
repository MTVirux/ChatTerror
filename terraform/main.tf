terraform {
  required_version = ">= 1.6.0"
  required_providers {
    hcloud = {
      source  = "hetznercloud/hcloud"
      version = "~> 1.54"
    }
  }
}

provider "hcloud" {
  token = var.hcloud_token
}

# The DNS zone can live in a different Hetzner project than the server.
provider "hcloud" {
  alias = "dns"
  token = coalesce(var.dns_token, var.hcloud_token)
}

locals {
  fqdn = var.subdomain == "@" ? var.domain : "${var.subdomain}.${var.domain}"
}

resource "hcloud_ssh_key" "operator" {
  name       = "chatterror-operator"
  public_key = var.ssh_public_key
}

resource "hcloud_firewall" "relay" {
  name = "chatterror-relay"

  rule {
    direction  = "in"
    protocol   = "tcp"
    port       = "22"
    source_ips = var.ssh_allowed_cidrs
  }

  dynamic "rule" {
    for_each = toset(["80", "443"])
    content {
      direction  = "in"
      protocol   = "tcp"
      port       = rule.value
      source_ips = ["0.0.0.0/0", "::/0"]
    }
  }
}

# Holds relay.db, vapid.json and Caddy's certificates. Losing vapid.json
# breaks every existing push subscription, so it must outlive the server.
resource "hcloud_volume" "data" {
  name     = "chatterror-data"
  size     = var.data_volume_size
  location = var.location
  format   = "ext4"
}

resource "hcloud_server" "relay" {
  name         = "chatterror-relay"
  server_type  = var.server_type
  image        = var.image
  location     = var.location
  ssh_keys     = [hcloud_ssh_key.operator.id]
  firewall_ids = [hcloud_firewall.relay.id]

  # Data lives on the attached volume, so the local disk never needs to grow.
  # Growing it is irreversible and blocks scaling back down.
  keep_disk = true

  user_data = templatefile("${path.module}/cloud-init.sh.tpl", {
    fqdn        = local.fqdn
    acme_email  = var.acme_email
    data_device = "/dev/disk/by-id/scsi-0HC_Volume_${hcloud_volume.data.id}"
    repo_url    = var.repo_url
    repo_ref    = var.repo_ref
  })

  lifecycle {
    ignore_changes = [user_data, ssh_keys]
  }
}

resource "hcloud_volume_attachment" "data" {
  volume_id = hcloud_volume.data.id
  server_id = hcloud_server.relay.id
  automount = false
}

# The token goes over SSH rather than user_data, which any process on the VM
# can read from the metadata service. A new token re-runs this to rewrite it.
resource "terraform_data" "deploy" {
  triggers_replace = [hcloud_server.relay.id, sha256(var.github_token)]

  depends_on = [hcloud_volume_attachment.data]

  connection {
    type        = "ssh"
    host        = hcloud_server.relay.ipv4_address
    user        = "root"
    private_key = file(pathexpand(var.ssh_private_key_path))
    timeout     = "10m"
  }

  provisioner "remote-exec" {
    inline = [
      "cloud-init status --wait > /dev/null || true",
      "test -x /opt/chatterror/deploy.sh",
      "install -d -m 0700 /etc/chatterror",
      "install -m 0600 /dev/null /etc/chatterror/github_token",
    ]
  }

  # The trailing newline keeps the content non-empty for public repos.
  provisioner "file" {
    content     = "${var.github_token}\n"
    destination = "/etc/chatterror/github_token"
  }

  provisioner "remote-exec" {
    inline = [
      "chmod 0600 /etc/chatterror/github_token",
      "[ -d /opt/chatterror/src/.git ] || /opt/chatterror/deploy.sh",
    ]
  }
}

resource "hcloud_zone_rrset" "relay_a" {
  provider = hcloud.dns
  zone     = var.domain
  name     = var.subdomain
  type     = "A"
  records = [
    { value = hcloud_server.relay.ipv4_address }
  ]
  ttl = 300
}

resource "hcloud_zone_rrset" "relay_aaaa" {
  provider = hcloud.dns
  zone     = var.domain
  name     = var.subdomain
  type     = "AAAA"
  records = [
    { value = hcloud_server.relay.ipv6_address }
  ]
  ttl = 300
}
