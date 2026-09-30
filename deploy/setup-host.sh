#!/usr/bin/env bash
#
# Prepares a fresh Ubuntu Server box to run the collector.
#
# Designed for an old laptop on a home network: ~4 GB RAM, no monitor attached,
# reachable only over Tailscale. Safe to re-run — every step checks its own
# result first.
#
# Usage (as root, on the box):
#   sudo bash setup-host.sh
#
# Afterwards run harden-ssh.sh, which cannot run before Tailscale is up.

set -euo pipefail

APP_USER="${APP_USER:-steam}"
APP_DIR="${APP_DIR:-/opt/steamdata}"

log()  { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m    %s\033[0m\n' "$*"; }
die()  { printf '\033[1;31m!!! %s\033[0m\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "Run with sudo."
[[ -r /etc/os-release ]] || die "Cannot identify the OS."
. /etc/os-release
[[ "$ID" == "ubuntu" || "$ID" == "debian" ]] || die "Expected Ubuntu or Debian, found '$ID'."

# ── Base packages ─────────────────────────────────────────────────────────────
log "Updating package lists"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq ca-certificates curl gnupg ufw unattended-upgrades

# ── Unattended security updates ───────────────────────────────────────────────
# A box in a garage will not get logged into for months, so security patches
# have to land on their own.
log "Enabling automatic security updates"
cat > /etc/apt/apt.conf.d/20auto-upgrades <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF

# ── Docker ────────────────────────────────────────────────────────────────────
if command -v docker >/dev/null 2>&1; then
    log "Docker already installed ($(docker --version))"
else
    log "Installing Docker Engine from the official repository"
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL "https://download.docker.com/linux/$ID/gpg" \
        -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc

    cat > /etc/apt/sources.list.d/docker.list <<EOF
deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] \
https://download.docker.com/linux/$ID $VERSION_CODENAME stable
EOF

    apt-get update -qq
    apt-get install -y -qq \
        docker-ce docker-ce-cli containerd.io \
        docker-buildx-plugin docker-compose-plugin
fi

systemctl enable --now docker

# Container logs on a laptop disk need a ceiling even though compose sets its
# own limits — this catches anything started by hand.
log "Capping Docker's global log size"
cat > /etc/docker/daemon.json <<'EOF'
{
  "log-driver": "json-file",
  "log-opts": { "max-size": "10m", "max-file": "3" }
}
EOF
systemctl restart docker

# ── Application user ──────────────────────────────────────────────────────────
# The collector runs as UID 1654 inside the container; this account exists only
# to own the deployment files and to be the SSH login. It is deliberately not in
# the docker group: membership there is equivalent to root, so administration
# goes through sudo where it is logged.
if id "$APP_USER" >/dev/null 2>&1; then
    log "User '$APP_USER' already exists"
else
    log "Creating user '$APP_USER'"
    adduser --disabled-password --gecos "" "$APP_USER"
fi

install -d -o "$APP_USER" -g "$APP_USER" -m 0755 "$APP_DIR"
install -d -o "$APP_USER" -g "$APP_USER" -m 0700 "/home/$APP_USER/.ssh"

# ── Tailscale ─────────────────────────────────────────────────────────────────
if command -v tailscale >/dev/null 2>&1; then
    log "Tailscale already installed"
else
    log "Installing Tailscale"
    curl -fsSL https://tailscale.com/install.sh | sh
fi

systemctl enable --now tailscaled

# ── Firewall ──────────────────────────────────────────────────────────────────
# Deny everything from the LAN and the internet; allow SSH only over the
# Tailscale interface.
log "Configuring the firewall"
ufw --force reset >/dev/null
ufw default deny incoming
ufw default allow outgoing
ufw allow in on tailscale0 to any port 22 proto tcp comment 'SSH over Tailscale only'
ufw allow in on tailscale0 to any port 41641 proto udp comment 'Tailscale direct connections'

# Tailscale is installed but not authenticated yet, so the rules above let
# nobody in. If this script is running over SSH, enabling the firewall now would
# drop that session and strand a machine with no monitor. Keep the current
# client reachable and hand the cleanup to harden-ssh.sh.
TEMP_RULE=0
if [[ -n "${SSH_CONNECTION:-}" ]]; then
    CLIENT_IP="${SSH_CONNECTION%% *}"
    warn "Detected an SSH session from $CLIENT_IP."
    warn "Adding a TEMPORARY rule so this session survives; harden-ssh.sh removes it."
    ufw allow from "$CLIENT_IP" to any port 22 proto tcp comment 'TEMPORARY setup access'
    TEMP_RULE=1
fi

ufw --force enable

log "Base setup complete"
[[ $TEMP_RULE -eq 1 ]] && warn "Remember: SSH is still reachable from $CLIENT_IP until you harden."

cat <<EOF

Next steps
----------
1. Join the Tailscale network (prints a URL to open in a browser):

     sudo tailscale up --ssh=false --hostname=garage-collector

2. Note the address it reports, then from your laptop:

     ssh $APP_USER@garage-collector

   Once that works, copy your public key in:

     ssh-copy-id $APP_USER@garage-collector

3. Only after key login works, lock SSH down:

     sudo bash $APP_DIR/harden-ssh.sh

   Keep this session open until you have confirmed a second one still works.

EOF
