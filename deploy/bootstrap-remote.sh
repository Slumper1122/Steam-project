#!/usr/bin/env bash
#
# Makes a machine you cannot physically reach reachable — and nothing else.
#
# This is the one command a helper sitting at the box has to run. It does not
# deploy the collector, does not touch the firewall and does not ask for any of
# the project's secrets, so the person typing it never handles them. Everything
# after this runs over SSH from the administrator's own laptop.
#
# Usage (as root, on the box):
#   sudo bash bootstrap-remote.sh \
#       --admin  <username> \
#       --ssh-key "ssh-ed25519 AAAA... you@laptop" \
#       --ts-key  tskey-auth-... \
#       [--hostname garage-collector]
#
# Generate the Tailscale key at https://login.tailscale.com/admin/settings/keys
# as single-use, pre-approved, with the shortest expiry you can work with. It is
# consumed the moment this script runs, so a leaked copy is worth nothing.
#
# Safe to re-run.

set -euo pipefail

ADMIN=""
SSH_KEY=""
TS_KEY=""
TS_HOSTNAME="garage-collector"

log()  { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m    %s\033[0m\n' "$*"; }
die()  { printf '\033[1;31m!!! %s\033[0m\n' "$*" >&2; exit 1; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --admin)    ADMIN="${2:-}";       shift 2 ;;
        --ssh-key)  SSH_KEY="${2:-}";     shift 2 ;;
        --ts-key)   TS_KEY="${2:-}";      shift 2 ;;
        --hostname) TS_HOSTNAME="${2:-}"; shift 2 ;;
        *) die "Unknown argument '$1'." ;;
    esac
done

# ── Validate before changing anything ─────────────────────────────────────────
# Getting any of these wrong produces a machine that looks set up but cannot be
# logged into, and the whole point is that nobody can go back to it easily.
[[ $EUID -eq 0 ]] || die "Run with sudo."

[[ -n "$ADMIN" ]] || die "Missing --admin <username>."
[[ "$ADMIN" =~ ^[a-z_][a-z0-9_-]{0,31}$ ]] || die "'$ADMIN' is not a valid Linux username."
[[ "$ADMIN" != "root" ]] || die "--admin must not be root."

[[ -n "$SSH_KEY" ]] || die "Missing --ssh-key. Pass the contents of the public
    key file (id_ed25519.pub), in quotes — never the private key."
[[ "$SSH_KEY" =~ ^(ssh-ed25519|ssh-rsa|ecdsa-sha2-nistp[0-9]+|sk-ssh-ed25519@openssh\.com)[[:space:]]+[A-Za-z0-9+/=]+ ]] \
    || die "--ssh-key does not look like an OpenSSH public key. Expected something
    starting with 'ssh-ed25519 AAAA...'. If it starts with '-----BEGIN', you have
    pasted the PRIVATE key — generate a new keypair, that one is compromised."

[[ -n "$TS_KEY" ]] || die "Missing --ts-key <tskey-auth-...>."
[[ "$TS_KEY" == tskey-* ]] || die "--ts-key should start with 'tskey-'."

[[ -r /etc/os-release ]] || die "Cannot identify the OS."
. /etc/os-release
log "Bootstrapping $PRETTY_NAME"

# ── SSH server ────────────────────────────────────────────────────────────────
# Desktop editions do not ship one, and without it there is no way in at all.
export DEBIAN_FRONTEND=noninteractive
log "Installing an SSH server"
apt-get update -qq
apt-get install -y -qq openssh-server ca-certificates curl
systemctl enable --now ssh 2>/dev/null || systemctl enable --now sshd

# ── Administrator account ─────────────────────────────────────────────────────
# A dedicated account, so the helper never shares their own login and the
# administrator's access does not depend on the helper's password.
if id "$ADMIN" >/dev/null 2>&1; then
    log "User '$ADMIN' already exists"
else
    log "Creating user '$ADMIN'"
    adduser --disabled-password --gecos "" "$ADMIN"
fi
usermod -aG sudo "$ADMIN"

ADMIN_HOME="$(getent passwd "$ADMIN" | cut -d: -f6)"
[[ -n "$ADMIN_HOME" ]] || die "No home directory for '$ADMIN'."

# sshd silently ignores an authorized_keys file that others can write, which
# looks exactly like a rejected key with no reason given.
install -d -o "$ADMIN" -g "$ADMIN" -m 0700 "$ADMIN_HOME/.ssh"
touch "$ADMIN_HOME/.ssh/authorized_keys"
if grep -qxF "$SSH_KEY" "$ADMIN_HOME/.ssh/authorized_keys"; then
    log "The key is already authorised for '$ADMIN'"
else
    log "Authorising the key for '$ADMIN'"
    printf '%s\n' "$SSH_KEY" >> "$ADMIN_HOME/.ssh/authorized_keys"
fi
chown "$ADMIN:$ADMIN" "$ADMIN_HOME/.ssh/authorized_keys"
chmod 0600 "$ADMIN_HOME/.ssh/authorized_keys"

# The account has no password on purpose: nothing to guess, nothing to share
# with the helper. sudo therefore cannot prompt for one, so allow it without.
# This is what cloud images do for the same reason — access is key-only, and a
# key that can log in could set a password anyway. To require one later:
#   sudo passwd <admin> && sudo rm /etc/sudoers.d/90-$ADMIN
SUDOERS="/etc/sudoers.d/90-$ADMIN"
printf '%s ALL=(ALL) NOPASSWD:ALL\n' "$ADMIN" > "$SUDOERS"
chmod 0440 "$SUDOERS"
visudo -cf "$SUDOERS" >/dev/null || { rm -f "$SUDOERS"; die "Rejected sudoers rule; nothing applied."; }

# ── Tailscale ─────────────────────────────────────────────────────────────────
if command -v tailscale >/dev/null 2>&1; then
    log "Tailscale already installed"
else
    log "Installing Tailscale"
    curl -fsSL https://tailscale.com/install.sh | sh
fi
systemctl enable --now tailscaled

log "Joining the tailnet as '$TS_HOSTNAME'"
# --ssh=false: we use the real sshd, which harden-ssh.sh locks down later.
tailscale up --auth-key="$TS_KEY" --hostname="$TS_HOSTNAME" --ssh=false

TS_IP=""
for _ in {1..15}; do
    TS_IP="$(tailscale ip -4 2>/dev/null | head -n1 || true)"
    [[ -n "$TS_IP" ]] && break
    sleep 2
done
[[ -n "$TS_IP" ]] || die "Tailscale did not get an address. Check 'tailscale status'."

# If a firewall is already running here, SSH over the overlay has to be allowed
# or the address printed below would be unreachable. setup-host.sh configures
# the firewall properly later; this only avoids locking ourselves out meanwhile.
if command -v ufw >/dev/null 2>&1 && ufw status | grep -q '^Status: active'; then
    warn "ufw is active — allowing SSH over tailscale0 so the address below works."
    ufw allow in on tailscale0 to any port 22 proto tcp comment 'SSH over Tailscale' >/dev/null
fi

printf '\n\033[1;32m'
cat <<EOF
=====================================================
 Done. Read this line back to the administrator:

     $TS_HOSTNAME   $TS_IP

 Nothing else is needed on this machine.
=====================================================
EOF
printf '\033[0m\n'
