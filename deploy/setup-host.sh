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

APP_DIR="${APP_DIR:-/opt/steamdata}"

log()  { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m    %s\033[0m\n' "$*"; }
die()  { printf '\033[1;31m!!! %s\033[0m\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "Run with sudo."
[[ -r /etc/os-release ]] || die "Cannot identify the OS."
. /etc/os-release

# Docker publishes repositories for Ubuntu and Debian only. Derivatives such as
# Linux Mint report their own ID and their own codename, neither of which names
# anything on download.docker.com, so map them onto the base they are built from.
case "$ID" in
    ubuntu|debian) DOCKER_DISTRO="$ID";    DERIVED_SUITE="$VERSION_CODENAME"        ;;
    linuxmint)     DOCKER_DISTRO="ubuntu"; DERIVED_SUITE="${UBUNTU_CODENAME:-}"     ;;
    lmde)          DOCKER_DISTRO="debian"; DERIVED_SUITE="${DEBIAN_CODENAME:-}"     ;;
    *)             die "Expected Ubuntu, Debian or Linux Mint, found '$ID'."        ;;
esac

DOCKER_SUITE="${DOCKER_SUITE:-$DERIVED_SUITE}"
[[ -n "$DOCKER_SUITE" ]] || die "Cannot tell which $DOCKER_DISTRO release
    '$PRETTY_NAME' is based on. Name it explicitly, for example:
      sudo DOCKER_SUITE=noble bash setup-host.sh"

log "Detected $PRETTY_NAME — using the $DOCKER_DISTRO '$DOCKER_SUITE' Docker repository"

# ── Base packages ─────────────────────────────────────────────────────────────
log "Updating package lists"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq \
    ca-certificates curl gnupg ufw unattended-upgrades openssh-server

# ── Unattended security updates ───────────────────────────────────────────────
# A box in a garage will not get logged into for months, so security patches
# have to land on their own.
log "Enabling automatic security updates"
cat > /etc/apt/apt.conf.d/20auto-upgrades <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF

# ── Never fall asleep ─────────────────────────────────────────────────────────
# A laptop in a garage sits with its lid shut, and a desktop edition suspends on
# idle out of the box. Either one stops the collector without any error to find
# later. Masking the sleep targets is what makes this stick: it holds even if
# someone re-enables suspend in the desktop's power settings.
log "Disabling suspend, hibernate and lid-close sleep"
install -d -m 0755 /etc/systemd/logind.conf.d
cat > /etc/systemd/logind.conf.d/10-collector.conf <<'EOF'
# Managed by setup-host.sh — this machine must stay awake to collect.
[Login]
HandleLidSwitch=ignore
HandleLidSwitchDocked=ignore
HandleLidSwitchExternalPower=ignore
HandleSuspendKey=ignore
HandleHibernateKey=ignore
IdleAction=ignore
EOF

systemctl mask sleep.target suspend.target hibernate.target hybrid-sleep.target \
    >/dev/null 2>&1 || warn "Could not mask the sleep targets; check 'systemctl status sleep.target'."

# Applying this without a reboot needs logind reloaded. On a desktop edition
# that can end the graphical session, which is harmless here — the collector
# does not need one — but it is why this runs before anything is deployed.
systemctl restart systemd-logind || warn "systemd-logind did not restart; a reboot will apply the setting."

# ── Docker ────────────────────────────────────────────────────────────────────
if command -v docker >/dev/null 2>&1; then
    log "Docker already installed ($(docker --version))"
else
    log "Installing Docker Engine from the official repository"
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL "https://download.docker.com/linux/$DOCKER_DISTRO/gpg" \
        -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc

    cat > /etc/apt/sources.list.d/docker.list <<EOF
deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] \
https://download.docker.com/linux/$DOCKER_DISTRO $DOCKER_SUITE stable
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

# ── Deployment directory ──────────────────────────────────────────────────────
# No service account is created. The container runs as UID 1654 in its own
# namespace and its data lives in a named Docker volume, so that UID needs no
# host account. Everything here is root-owned: only root runs compose, and only
# root may read the Steam and Supabase keys.
#
# Nobody is added to the docker group either — that membership is equivalent to
# root and would bypass the sudo logging that makes actions attributable.
log "Creating $APP_DIR"
install -d -o root -g root -m 0755 "$APP_DIR"

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

# SSH access after hardening belongs to a human account that can sudo — the one
# created during the Ubuntu install. Naming it here keeps the instructions
# correct on a box where that account is not called 'ubuntu'.
ADMIN_USER="${SUDO_USER:-$(id -un)}"

cat <<EOF

Next steps
----------
1. Join the Tailscale network (prints a URL to open in a browser):

     sudo tailscale up --ssh=false --hostname=garage-collector

2. Note the address it reports, then from your own laptop copy your public key
   to the administrator account on this box:

     ssh-copy-id $ADMIN_USER@garage-collector
     ssh $ADMIN_USER@garage-collector          # must work without a password

3. Deploy the collector:

     sudo bash deploy/install.sh

4. Only after key login works, lock SSH down:

     sudo bash $APP_DIR/harden-ssh.sh

   Keep this session open until you have confirmed a second one still works.

EOF
