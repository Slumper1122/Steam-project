#!/usr/bin/env bash
#
# Installs the collector onto a box already prepared by setup-host.sh.
#
# Run from a checkout of the repository on the box:
#   sudo bash deploy/install.sh
#
# Safe to re-run: an existing .env and GHCR token are left alone.

set -euo pipefail

APP_USER="${APP_USER:-steam}"
APP_DIR="${APP_DIR:-/opt/steamdata}"
CONF_DIR="${CONF_DIR:-/etc/steamdata}"
SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

log()  { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m    %s\033[0m\n' "$*"; }
die()  { printf '\033[1;31m!!! %s\033[0m\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "Run with sudo."
command -v docker >/dev/null 2>&1 || die "Docker is missing. Run setup-host.sh first."
docker compose version >/dev/null 2>&1 || die "The docker compose plugin is missing."

# ── Files ─────────────────────────────────────────────────────────────────────
log "Installing to $APP_DIR"
install -d -m 0755 "$APP_DIR"
install -d -m 0700 "$CONF_DIR"

install -m 0644 "$SRC/docker-compose.prod.yml" "$APP_DIR/"
install -m 0700 "$SRC/update.sh"               "$APP_DIR/"
install -m 0700 "$SRC/harden-ssh.sh"           "$APP_DIR/"

# ── Secrets ───────────────────────────────────────────────────────────────────
# Root-only: these are the Steam key and the Supabase service key.
if [[ -f "$APP_DIR/.env" ]]; then
    log "Keeping the existing $APP_DIR/.env"
else
    log "Creating $APP_DIR/.env — fill it in before starting"
    cat > "$APP_DIR/.env" <<'EOF'
# Runtime secrets for the collector. Never committed, never baked into an image.

STEAM_API_KEY=

# Same values as the GitHub repository secrets.
SUPABASE_URL=
SUPABASE_KEY=
EOF
fi
chown root:root "$APP_DIR/.env"
chmod 0600 "$APP_DIR/.env"

if [[ -f "$CONF_DIR/ghcr.token" ]]; then
    log "Keeping the existing $CONF_DIR/ghcr.token"
else
    log "Creating $CONF_DIR/ghcr.token — fill it in before starting"
    cat > "$CONF_DIR/ghcr.token" <<'EOF'
your-github-username
github_pat_replace_me_with_a_read_packages_token
EOF
fi
chown root:root "$CONF_DIR/ghcr.token"
chmod 0600 "$CONF_DIR/ghcr.token"

# ── systemd ───────────────────────────────────────────────────────────────────
log "Installing the update timer"
install -m 0644 "$SRC/steamdata-update.service" /etc/systemd/system/
install -m 0644 "$SRC/steamdata-update.timer"   /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now steamdata-update.timer

# ── Readiness check ───────────────────────────────────────────────────────────
READY=1
grep -q '^STEAM_API_KEY=.\+' "$APP_DIR/.env"        || { warn "STEAM_API_KEY is empty in $APP_DIR/.env"; READY=0; }
grep -q '^github_pat_' "$CONF_DIR/ghcr.token"       || { warn "$CONF_DIR/ghcr.token still holds the placeholder"; READY=0; }

if [[ $READY -eq 1 ]]; then
    log "Configuration looks complete — starting the collector"
    "$APP_DIR/update.sh"
else
    cat <<EOF

Fill these in, then start the collector
---------------------------------------
  sudo nano $APP_DIR/.env            # STEAM_API_KEY, SUPABASE_URL, SUPABASE_KEY
  sudo nano $CONF_DIR/ghcr.token     # line 1: GitHub username, line 2: read:packages token
  sudo $APP_DIR/update.sh

EOF
fi

cat <<EOF

Everyday commands
-----------------
  sudo docker compose -f $APP_DIR/docker-compose.prod.yml logs -f    # what it is doing
  sudo docker compose -f $APP_DIR/docker-compose.prod.yml ps         # is it running
  sudo $APP_DIR/update.sh                                            # deploy now
  systemctl list-timers steamdata-update                             # next auto-update
  journalctl -u steamdata-update -n 50                               # update history

EOF
