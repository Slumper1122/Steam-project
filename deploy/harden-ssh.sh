#!/usr/bin/env bash
#
# Locks SSH down to key-only logins from the Tailscale network.
#
# Run this ONLY after `ssh <user>@<host>` works with a key, and keep your current
# session open while you verify a second one. A mistake here locks you out of a
# machine that has no monitor attached.
#
# Usage (as root, on the box):
#   sudo bash harden-ssh.sh

set -euo pipefail

APP_USER="${APP_USER:-steam}"
CONF="/etc/ssh/sshd_config.d/10-hardening.conf"

log()  { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
die()  { printf '\033[1;31m!!! %s\033[0m\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "Run with sudo."
id "$APP_USER" >/dev/null 2>&1 || die "User '$APP_USER' does not exist. Run setup-host.sh first."

# ── Refuse to lock the door with the key still inside ─────────────────────────
KEYS="/home/$APP_USER/.ssh/authorized_keys"
[[ -s "$KEYS" ]] || die "$KEYS is empty. Run 'ssh-copy-id $APP_USER@<host>' first, or you will be locked out."
log "Found $(grep -c . "$KEYS") authorised key(s) for '$APP_USER'"

TS_IP="$(tailscale ip -4 2>/dev/null | head -n1 || true)"
[[ -n "$TS_IP" ]] || die "Tailscale has no IPv4 address yet. Run 'tailscale up' first."
log "Tailscale address: $TS_IP"

# ── Write the hardened config ─────────────────────────────────────────────────
# A drop-in rather than an edit of sshd_config, so a distribution upgrade cannot
# quietly revert it and the original file stays pristine.
log "Writing $CONF"
cat > "$CONF" <<EOF
# Managed by harden-ssh.sh. Do not edit by hand.

# Only listen on the Tailscale interface. Even if the firewall were flushed,
# sshd would not answer on the LAN or on a forwarded router port.
ListenAddress $TS_IP

# Keys only. No password can be guessed if none is accepted.
PasswordAuthentication no
KbdInteractiveAuthentication no
ChallengeResponseAuthentication no
PermitEmptyPasswords no
PubkeyAuthentication yes

# Root has no business logging in directly; administration goes through sudo,
# which attributes the action to a person.
PermitRootLogin no

# Nobody else gets a shell on this box.
AllowUsers $APP_USER

# Drop unauthenticated connections quickly so a stuck client cannot pile up.
MaxAuthTries 3
MaxSessions 4
LoginGraceTime 20

# This box is a collector, not a jump host.
AllowAgentForwarding no
AllowTcpForwarding no
X11Forwarding no

# Notice a dead link within ~2 minutes instead of leaving the session wedged.
ClientAliveInterval 60
ClientAliveCountMax 2
EOF

chmod 0644 "$CONF"

# ── Validate before applying ──────────────────────────────────────────────────
log "Validating configuration"
sshd -t || die "sshd rejected the configuration; nothing was applied."

# Ubuntu 22.10+ ships socket activation, which ignores ListenAddress.
if systemctl is-enabled --quiet ssh.socket 2>/dev/null; then
    log "Disabling ssh.socket so ListenAddress takes effect"
    systemctl disable --now ssh.socket
    systemctl enable ssh.service
fi

log "Restarting sshd"
systemctl restart ssh

# ── Close the setup door ──────────────────────────────────────────────────────
# setup-host.sh left a rule allowing SSH from whichever address ran it, so the
# firewall could be enabled without dropping that session. Tailscale works now,
# so that opening is no longer needed.
if ufw status | grep -q 'TEMPORARY setup access'; then
    log "Removing the temporary LAN access rule left by setup-host.sh"
    while ufw status numbered | grep -q 'TEMPORARY setup access'; do
        NUM="$(ufw status numbered | grep 'TEMPORARY setup access' | head -n1 | sed 's/^\[ *\([0-9]\+\).*/\1/')"
        ufw --force delete "$NUM"
    done
fi

cat <<EOF

SSH is now locked down
----------------------
  listening on   $TS_IP (Tailscale only)
  login          key-only, user '$APP_USER'
  root login     disabled

DO NOT CLOSE THIS SESSION YET.

From your laptop, open a second terminal and confirm:

    ssh $APP_USER@$TS_IP

If that works, you are done. If it does not, fix it from this still-open
session — the box has no monitor.

To undo: sudo rm $CONF && sudo systemctl restart ssh

EOF
