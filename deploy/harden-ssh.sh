#!/usr/bin/env bash
#
# Locks SSH down to key-only logins from the Tailscale network.
#
# Run this ONLY after `ssh <you>@<host>` works with a key, and keep your current
# session open while you verify a second one. A mistake here locks you out of a
# machine that has no monitor attached.
#
# Usage (from your own sudo-capable account, on the box):
#   sudo bash harden-ssh.sh
#
# The account that invoked sudo is the one that keeps access. Override with
# ADMIN_USER=<name> if you mean a different one.

set -euo pipefail

# The single account that keeps SSH access. It must be a human administrator:
# after this script runs, root login is refused and everything else is done
# through sudo, so an account that cannot sudo would leave a reachable box that
# nobody can actually administer.
ADMIN_USER="${ADMIN_USER:-${SUDO_USER:-}}"
CONF="/etc/ssh/sshd_config.d/10-hardening.conf"

log()  { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
die()  { printf '\033[1;31m!!! %s\033[0m\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "Run with sudo."

# ── Refuse to lock the door with the key still inside ─────────────────────────
# Every check below exists because getting it wrong means a machine with no
# monitor that nothing can reach.
[[ -n "$ADMIN_USER" ]] || die "Cannot tell which account should keep SSH access.
    Run this as 'sudo bash harden-ssh.sh' from your own account, or pass it
    explicitly: 'sudo ADMIN_USER=<name> bash harden-ssh.sh'."

[[ "$ADMIN_USER" != "root" ]] || die "root cannot be the SSH login — this script
    disables direct root login. Run it from your own sudo-capable account."

id "$ADMIN_USER" >/dev/null 2>&1 || die "User '$ADMIN_USER' does not exist."

id -nG "$ADMIN_USER" | grep -qw sudo \
    || die "'$ADMIN_USER' is not in the sudo group. Hardening would leave a box
    that accepts logins but cannot be administered. Fix with:
      sudo usermod -aG sudo $ADMIN_USER"

ADMIN_HOME="$(getent passwd "$ADMIN_USER" | cut -d: -f6)"
[[ -n "$ADMIN_HOME" && -d "$ADMIN_HOME" ]] || die "No home directory for '$ADMIN_USER'."

KEYS="$ADMIN_HOME/.ssh/authorized_keys"
[[ -s "$KEYS" ]] || die "$KEYS is empty or missing. Passwords are refused after
    this script runs, so you would be locked out. From your laptop, run:
      ssh-copy-id $ADMIN_USER@<host>"
log "Found $(grep -c . "$KEYS") authorised key(s) for '$ADMIN_USER'"

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
AllowUsers $ADMIN_USER

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
  login          key-only, user '$ADMIN_USER'
  root login     disabled

DO NOT CLOSE THIS SESSION YET.

From your laptop, open a second terminal and confirm:

    ssh $ADMIN_USER@$TS_IP

If that works, you are done. If it does not, fix it from this still-open
session — the box has no monitor.

To undo: sudo rm $CONF && sudo systemctl restart ssh

EOF
