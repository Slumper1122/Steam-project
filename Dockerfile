# syntax=docker/dockerfile:1.7
# ==============================================================================
# Steam Data Puller — production container
#
# Build:  docker build -t steamdata:latest .
# Run:    docker compose up -d
#
# Design notes (see README "Container" section for the full rationale):
#   * multi-stage    — the 800 MB SDK never reaches the final image
#   * chiseled base  — no shell, no package manager, no root user
#   * non-root       — runs as UID 1654, cannot write outside /data
#   * layer caching  — restore runs before the sources are copied
# ==============================================================================

# ── Stage 1: restore + publish ────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0-noble AS build

ARG TARGETARCH=amd64
WORKDIR /src

# Copy only the project file first so `dotnet restore` is cached as long as the
# dependency list is unchanged. Editing C# files then skips the NuGet download.
COPY ["Steam data puller/Steam data puller/Steam data puller.csproj", "app/"]
RUN dotnet restore "app/Steam data puller.csproj" \
        --runtime "linux-$( [ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64 )"

COPY ["Steam data puller/Steam data puller/", "app/"]

# PublishSingleFile is disabled here: bundling gains nothing inside an image and
# it forces the runtime to unpack itself into a temp dir on every start, which
# breaks a read-only root filesystem.
# InvariantGlobalization avoids a dependency on ICU, which the chiseled base
# image deliberately does not ship (~30 MB saved).
RUN dotnet publish "app/Steam data puller.csproj" \
        --configuration Release \
        --no-restore \
        --runtime "linux-$( [ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64 )" \
        --self-contained false \
        -p:PublishSingleFile=false \
        -p:InvariantGlobalization=true \
        -p:SatelliteResourceLanguages=en \
        -p:DebugType=none \
        -p:GenerateDocumentationFile=false \
        --output /publish

# The runtime stage has no shell, so every directory the app writes to has to be
# created here with the right ownership.
RUN mkdir -p /seed/data/snapshots

# ── Stage 2: runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/runtime:8.0-noble-chiseled AS runtime

# Matches APP_UID from the chiseled base image. Declared explicitly so the
# COPY --chown below does not silently fall back to root.
ARG APP_UID=1654

LABEL org.opencontainers.image.title="Steam Data Puller" \
      org.opencontainers.image.description="Collects Steam game metrics into JSON, SQLite and Supabase" \
      org.opencontainers.image.source="https://github.com/Slumper1122/Steam-project" \
      org.opencontainers.image.licenses="MIT"

WORKDIR /app

COPY --from=build --chown=root:root --chmod=555 /publish/ ./
COPY --chown=root:root --chmod=444 watchlist.json /app/watchlist.json
COPY --from=build --chown=${APP_UID}:${APP_UID} /seed/data /data

# Never buffer logs behind a pipe — `docker logs` should show progress live.
ENV DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    COLLECT_INTERVAL_SECONDS=3600

# /data is the only writable path, so the root filesystem can be mounted
# read-only (see docker-compose.yml).
VOLUME ["/data"]

USER ${APP_UID}

ENTRYPOINT ["/app/steamdata"]
CMD ["collect", \
     "--watchlist", "/app/watchlist.json", \
     "--output",    "/data/snapshots", \
     "--db",        "/data/steam_data.db"]
