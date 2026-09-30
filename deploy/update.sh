#!/usr/bin/env bash
#
# Pulls the newest published image and restarts the collector if it changed.
#
# Invoked by steamdata-update.timer, but safe to run by hand:
#   sudo /opt/steamdata/update.sh
#
# Pull-based on purpose: the box never accepts an inbound deploy connection and
# GitHub never holds a credential for it. The only thing that crosses the
# boundary is an outbound, read-only registry pull.

set -euo pipefail

APP_DIR="${APP_DIR:-/opt/steamdata}"
TOKEN_FILE="${TOKEN_FILE:-/etc/steamdata/ghcr.token}"
COMPOSE=(docker compose -f "$APP_DIR/docker-compose.prod.yml")

log() { printf '%s  %s\n' "$(date -Is)" "$*"; }
die() { printf '%s  ERROR: %s\n' "$(date -Is)" "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "Run with sudo."
[[ -f "$APP_DIR/docker-compose.prod.yml" ]] || die "Not deployed: $APP_DIR/docker-compose.prod.yml is missing."

cd "$APP_DIR"

IMAGE="$("${COMPOSE[@]}" config --images | head -n1)"
[[ -n "$IMAGE" ]] || die "Could not determine the image name from the compose file."

# ── Registry login ────────────────────────────────────────────────────────────
# The package is private. The token is read-only (read:packages) and lives in a
# root-only file rather than in a long-lived ~/.docker/config.json.
if [[ -r "$TOKEN_FILE" ]]; then
    GHCR_USER="$(sed -n '1p' "$TOKEN_FILE")"
    GHCR_TOKEN="$(sed -n '2p' "$TOKEN_FILE")"
    [[ -n "$GHCR_USER" && -n "$GHCR_TOKEN" ]] \
        || die "$TOKEN_FILE must hold the username on line 1 and the token on line 2."
    printf '%s' "$GHCR_TOKEN" | docker login ghcr.io -u "$GHCR_USER" --password-stdin >/dev/null
    log "Authenticated to ghcr.io as $GHCR_USER"
else
    log "No $TOKEN_FILE — assuming a public image"
fi

# ── Pull ──────────────────────────────────────────────────────────────────────
BEFORE="$(docker image inspect "$IMAGE" --format '{{.Id}}' 2>/dev/null || echo 'sha256:(none)')"

log "Pulling $IMAGE"
if ! "${COMPOSE[@]}" pull --quiet; then
    # A flaky home connection must not leave the collector stopped. The running
    # container keeps working on the previous image until the next timer fire.
    log "Pull failed; keeping the running version."
    exit 0
fi

AFTER="$(docker image inspect "$IMAGE" --format '{{.Id}}')"

# ── Restart only when something actually changed ──────────────────────────────
if [[ "$BEFORE" == "$AFTER" ]] && "${COMPOSE[@]}" ps --status running --quiet | grep -q .; then
    log "Already up to date (${AFTER:7:12}); collector running."
    exit 0
fi

log "Deploying ${AFTER:7:12} (was ${BEFORE:7:12})"
"${COMPOSE[@]}" up -d

# Reclaim the superseded image; a laptop disk fills up quickly otherwise.
docker image prune -f --filter "until=24h" >/dev/null || true

log "Done. Collector status:"
"${COMPOSE[@]}" ps --format 'table {{.Name}}\t{{.Image}}\t{{.Status}}'
