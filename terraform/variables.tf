variable "hcloud_token" {
  description = "Hetzner Cloud API token (covers compute + DNS). Set via TF_VAR_hcloud_token from .envrc."
  type        = string
  sensitive   = true
}

variable "dns_token" {
  description = "API token for the project holding the DNS zone, when it differs from the server's. Set via TF_VAR_dns_token."
  type        = string
  sensitive   = true
  default     = ""
}

variable "domain" {
  description = "DNS zone hosted in Hetzner DNS."
  type        = string
}

variable "subdomain" {
  description = "Record name the relay is served on, joined with domain. Use \"@\" for the apex."
  type        = string
  default     = "chatterror"
  validation {
    condition     = length(var.subdomain) > 0 && !can(regex("[.]", var.subdomain))
    error_message = "subdomain must be a single non-empty DNS label (no dots) or \"@\"."
  }
}

variable "acme_email" {
  description = "ACME contact email for cert issuance, also used as the VAPID subject."
  type        = string
}

variable "ssh_public_key" {
  description = "Operator SSH public key contents."
  type        = string
}

variable "ssh_allowed_cidrs" {
  description = "Source CIDRs allowed to SSH (port 22) into the VM."
  type        = list(string)
  validation {
    condition     = alltrue([for cidr in var.ssh_allowed_cidrs : can(cidrhost(cidr, 0))])
    error_message = "Each entry in ssh_allowed_cidrs must be a valid CIDR (e.g. 1.2.3.4/32)."
  }
}

variable "location" {
  description = "Hetzner location."
  type        = string
  default     = "fsn1"
}

variable "server_type" {
  description = "Hetzner server type for the relay VM."
  type        = string
  default     = "cx23"
}

variable "image" {
  description = "Hetzner OS image."
  type        = string
  default     = "ubuntu-24.04"
}

variable "data_volume_size" {
  description = "Block volume size for relay data, in GB."
  type        = number
  default     = 10
  validation {
    condition     = var.data_volume_size >= 10
    error_message = "Hetzner block volume minimum is 10 GB."
  }
}

variable "repo_url" {
  description = "Public ChatTerror repo URL cloud-init clones from."
  type        = string
  default     = "https://github.com/MTVirux/ChatTerror.git"
}

variable "repo_ref" {
  description = "Git ref (branch/tag/SHA) cloud-init checks out."
  type        = string
  default     = "master"
}
