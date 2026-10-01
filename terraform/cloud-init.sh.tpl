#!/bin/bash
# Rendered by templatefile() in main.tf. Shell variables avoid the brace form
# because templatefile() would try to interpolate it.

set -euo pipefail
exec > >(tee -a /var/log/chatterror-bootstrap.log) 2>&1

echo "[$(date -Is)] bootstrap start"

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y ca-certificates curl gnupg git

install -m 0755 -d /etc/apt/keyrings
if [ ! -f /etc/apt/keyrings/docker.gpg ]; then
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
      | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
    chmod a+r /etc/apt/keyrings/docker.gpg
fi

. /etc/os-release
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
https://download.docker.com/linux/ubuntu $VERSION_CODENAME stable" \
    > /etc/apt/sources.list.d/docker.list

apt-get update
apt-get install -y docker-ce docker-ce-cli containerd.io \
    docker-buildx-plugin docker-compose-plugin
systemctl enable --now docker

# Containers must not reach the metadata service. iptables rules don't
# survive a reboot, so the rule is re-added whenever docker starts.
cat > /etc/systemd/system/block-container-metadata.service <<'EOF'
[Unit]
Description=Block containers from the cloud metadata service
After=docker.service
PartOf=docker.service

[Service]
Type=oneshot
RemainAfterExit=yes
ExecStart=/bin/sh -c 'iptables -C DOCKER-USER -d 169.254.169.254/32 -j DROP 2>/dev/null || iptables -I DOCKER-USER -d 169.254.169.254/32 -j DROP'

[Install]
WantedBy=docker.service
EOF
systemctl daemon-reload
systemctl enable --now block-container-metadata.service

# The volume is attached after the server is created, so it may not exist yet.
DATA_DEVICE='${data_device}'
for _ in $(seq 1 60); do
    [ -e "$DATA_DEVICE" ] && break
    sleep 5
done
[ -e "$DATA_DEVICE" ] || { echo "data volume never appeared"; exit 1; }

mkdir -p /mnt/data
if ! grep -q "$DATA_DEVICE" /etc/fstab; then
    echo "$DATA_DEVICE /mnt/data ext4 discard,nofail,defaults 0 0" >> /etc/fstab
fi
mountpoint -q /mnt/data || mount /mnt/data

# 1654 is the aspnet image's non-root APP_UID.
mkdir -p /mnt/data/relay /mnt/data/caddy/data /mnt/data/caddy/config
chown 1654:1654 /mnt/data/relay
chmod 0700 /mnt/data/relay

mkdir -p /opt/chatterror

# Terraform writes the GitHub token (empty for a public repo) to
# /etc/chatterror/github_token over SSH after boot.
cat > /usr/local/bin/git-credential-chatterror <<'EOF'
#!/bin/sh
[ "$1" = get ] || exit 0
token=$(cat /etc/chatterror/github_token 2>/dev/null) || exit 0
[ -n "$token" ] || exit 0
echo username=x-access-token
echo "password=$token"
EOF
chmod 0755 /usr/local/bin/git-credential-chatterror

cat > /opt/chatterror/Caddyfile <<'EOF'
${fqdn} {
    reverse_proxy relay:8080
}
EOF

# Fixed subnet so the relay can trust X-Forwarded-For from Caddy only.
cat > /opt/chatterror/compose.yaml <<'EOF'
services:
  relay:
    build: ./src
    restart: unless-stopped
    environment:
      Relay__VapidSubject: "mailto:${acme_email}"
      Relay__TrustedProxies: "172.30.0.0/24"
    volumes:
      - /mnt/data/relay:/data
    networks: [web]

  caddy:
    image: caddy:2
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
      - "443:443/udp"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - /mnt/data/caddy/data:/data
      - /mnt/data/caddy/config:/config
    depends_on: [relay]
    networks: [web]

networks:
  web:
    ipam:
      config:
        - subnet: 172.30.0.0/24
EOF

cat > /opt/chatterror/deploy.sh <<'EOF'
#!/bin/bash
# Clones or pulls the configured ref and rebuilds. Run as root on the VM.
set -euo pipefail
export GIT_TERMINAL_PROMPT=0
git_auth() { git -c credential.helper= -c credential.helper=chatterror "$@"; }
cd /opt/chatterror
if [ ! -d src/.git ]; then
    git_auth clone --depth 1 --branch '${repo_ref}' '${repo_url}' src
else
    git_auth -C src fetch --depth 1 origin '${repo_ref}'
    git -C src checkout -f FETCH_HEAD
fi
docker compose up -d --build
docker image prune -f
EOF
chmod +x /opt/chatterror/deploy.sh

# The first deploy runs from terraform once the token file is in place.
echo "[$(date -Is)] bootstrap done"
