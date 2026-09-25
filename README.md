# Steam Data Puller

A C# CLI tool that fetches and stores game metrics from Steam APIs.
Designed to capture the full lifecycle of singleplayer games — from early access through maturity.

![CI](https://github.com/Slumper1122/Steam-project/actions/workflows/ci.yml/badge.svg)
![Collect](https://github.com/Slumper1122/Steam-project/actions/workflows/collect.yml/badge.svg)
![Docker](https://github.com/Slumper1122/Steam-project/actions/workflows/docker.yml/badge.svg)

## Features

- **10 metrics** per snapshot: owners, CCU, reviews, playtime, price, updates, achievements, DLC
- **JSON storage** — one timestamped file per pull under `data/<appid>/`
- **SQLite database** — all snapshots persisted for querying over time
- **Supabase cloud** — hourly snapshots pushed to PostgreSQL, accessible via dashboard or SQL
- **Delta detection** — only stores a snapshot when something actually changed (saves space)
- **Delta view** — compare any two consecutive snapshots metric by metric
- **History table** — tabular view of all stored snapshots
- **`collect` command** — unattended batch pull for all games in `watchlist.json`
- **GitHub Actions** — hourly data collection + CI test gate on every PR
- **Containerized** — 87 MB hardened image: non-root, read-only filesystem, no shell

## Architecture

```
External cron ──► GitHub Actions (workflow_dispatch, hourly)
         │
         ▼
┌────────────────────────────────────────────────────────────┐
│                     CLI (steamdata)                         │
│  pull <appid> │ history │ delta │ collect (watchlist.json)  │
└───────────────────────┬────────────────────────────────────┘
                        │
          ┌─────────────▼──────────────┐
          │       SnapshotBuilder       │
          │   orchestrates all fetches  │
          └──┬──────┬──────┬───────┬───┘
             │      │      │       │
    ┌────────▼─┐ ┌──▼───┐ ┌▼────┐ ┌▼──────────┐
    │ Steam    │ │Steam │ │Steam│ │ SteamSpy  │
    │ Store    │ │ Web  │ │ Rev │ │    API    │
    │   API    │ │  API │ │ API │ │ (owners)  │
    └──────────┘ └──────┘ └─────┘ └───────────┘
                        │
          ┌─────────────▼──────────────┐
          │      GameSnapshot model     │
          │    DeltaService (changed?)  │
          └──────┬──────────┬───────────┘
                 │          │
          ┌──────▼────┐  ┌──▼────────────────┐
          │ JSON file │  │  SQLite (local)    │
          │  storage  │  │  Supabase (cloud)  │
          └───────────┘  └────────────────────┘
```

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A free [Steam Web API key](https://steamcommunity.com/dev/apikey)

## Clone and Build

```bash
git clone https://github.com/Slumper1122/Steam-project.git
cd Steam-project
cd "Steam data puller/Steam data puller"
dotnet build
```

## Usage

### Pull a snapshot

```bash
# Using --key flag
dotnet run -- pull 264710 --key YOUR_STEAM_API_KEY

# Using environment variable (recommended)
$env:STEAM_API_KEY = "YOUR_KEY"
dotnet run -- pull 264710

# Common games
dotnet run -- pull 264710  # Subnautica
dotnet run -- pull 427520  # Factorio
dotnet run -- pull 892970  # Hollow Knight
```

**Terminal output example:**
```
  Steam Data Puller — AppID 264710
  [FETCH] Steam Store details ... ✓  Subnautica
  [FETCH] Current player count  ... ✓  3 340 players online
  [FETCH] Review summary         ... ✓
  [FETCH] Recent updates (news)  ... ✓  20 items
  [FETCH] Achievement rates      ... ✓  17 achievements
  [FETCH] SteamSpy data          ... ✓
  [STORE] Writing JSON file     ... ✓  data/264710/264710_2026-07-29T17-34-07Z.json
  [DB]    Inserting snapshot    ... ✓  snapshot #1

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  Subnautica  (AppID 264710)  —  snapshot #1
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  Owners (est.)         5 000 000 – 10 000 000
  Current players       3 340
  Reviews               97.2%  (Overwhelmingly Positive)
  Price                 $29.99
  Updates fetched       20
  DLC count             1
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### View snapshot history

```bash
dotnet run -- history 264710
dotnet run -- history 264710 --limit 20
```

### View delta between last two snapshots

```bash
dotnet run -- delta 264710
```

**Example output:**
```
  Delta for AppID 264710
  Previous : 2026-07-29 08:00:00 UTC
  Current  : 2026-07-30 08:00:00 UTC

  Metric                     Previous     Current  Change
  ─────────────────────────────────────────────────────
  Current players               3 340       4 127  ↑ +787
  Owners (low est.)         5 000 000   5 000 000  → no change
  Review score                  97.2%       97.3%  ↑ +0
  Price (USD)                  $29.99      $14.99  ↓ -15  (sale!)
  Discount %                       0%         50%  ↑ +50
```

### Custom output and database paths

```bash
dotnet run -- pull 264710 --key YOUR_KEY --output ./snapshots --db ./mydata.db
```

## Collected Metrics

| # | Metric | Source |
|---|--------|--------|
| 1 | Owner estimate (low / high) | SteamSpy |
| 2 | Current players | Steam Web API |
| 3 | 24h peak CCU | SteamSpy |
| 4 | Review score + label | Steam Reviews API |
| 5 | Total review count | Steam Reviews API |
| 6 | Avg / Median playtime | SteamSpy |
| 7 | Recent updates (last 20) | Steam News API |
| 8 | Current price + discount | Steam Store API |
| 9 | Achievement unlock rates (top 10) | Steam Web API |
| 10 | DLC count | Steam Store API |

## JSON Output Structure

```json
{
  "appId": 264710,
  "name": "Subnautica",
  "capturedAt": "2026-07-29T17:34:07Z",
  "schemaVersion": "1.0",
  "info": {
    "developer": "Unknown Worlds Entertainment",
    "releaseDate": "Jan 23, 2018",
    "isEarlyAccess": false,
    "genres": ["Action", "Adventure"],
    "tags": ["Underwater", "Open World", "Survival", ...]
  },
  "players":  { "currentPlayers": 3340, "peakCcu24h": 2719 },
  "owners":   { "estimateLow": 5000000, "estimateHigh": 10000000 },
  "reviews":  { "scoreDescription": "Overwhelmingly Positive", "positivePercent": 97.2, ... },
  "playtime": { "averageForeverMinutes": 0, "medianForeverMinutes": 0, ... },
  "price":    { "currentUsd": 29.99, "discountPercent": 0 },
  "updates":  { "fetchedCount": 20, "items": [...] },
  "achievements": { "totalCount": 17, "top10": [...] },
  "dlc":      { "count": 1, "appIds": [2012840] }
}
```

## Project Structure

```
Steam-project/
├── README.md
├── requirements.md
├── .gitignore
└── Steam data puller/
    ├── Steam data puller.slnx
    └── Steam data puller/
        ├── Steam data puller.csproj
        ├── Program.cs
        ├── Models/
        │   └── GameSnapshot.cs        ← all data models
        ├── Clients/
        │   ├── SteamApiClient.cs      ← Steam Store + Web + Reviews APIs
        │   └── SteamSpyClient.cs      ← SteamSpy API
        ├── Services/
        │   ├── SnapshotBuilder.cs     ← orchestrates all API calls
        │   ├── DatabaseService.cs     ← SQLite (Dapper)
        │   └── JsonStorage.cs         ← JSON file I/O
        ├── Commands/
        │   ├── PullCommand.cs
        │   ├── HistoryCommand.cs
        │   ├── DeltaCommand.cs
        │   └── CollectCommand.cs      ← batch pull for watchlist
        ├── Clients/
        │   ├── SteamApiClient.cs
        │   ├── SteamSpyClient.cs
        │   └── SupabaseClient.cs      ← REST push to Supabase
        └── Services/
            ├── SnapshotBuilder.cs
            ├── DatabaseService.cs
            ├── JsonStorage.cs
            └── DeltaService.cs        ← skip unchanged snapshots
    Steam.Tests/                       ← xUnit smoke tests (45 tests)
watchlist.json                         ← list of App IDs to monitor
supabase_schema.sql                    ← run once in Supabase SQL Editor
Dockerfile                             ← multi-stage, chiseled, non-root
docker-compose.yml                     ← hardened runtime (read-only, no caps)
.dockerignore                          ← keeps secrets and build output out
.env.example                           ← template for local secrets
.github/workflows/
    ci.yml                             ← build + test + coverage on every PR
    collect.yml                        ← hourly data collection
    docker.yml                         ← image build + Trivy scan + GHCR push
```

---

## CI / Automation setup

### 1. Enable GitHub Actions

Push to GitHub — Actions run automatically.

| Workflow | Trigger | What it does |
|----------|---------|--------------|
| `ci.yml` | Every push / PR | Build → test → coverage check → block merge if < 60% |
| `collect.yml` | `workflow_dispatch`, hourly from an external cron | Pull Steam data → delta check → push to Supabase |
| `docker.yml` | Push to `main` / PR | Build image → Trivy scan → publish to GHCR |

### 2. Add GitHub Secrets

Go to your repo → **Settings → Secrets and variables → Actions → New repository secret**:

| Secret name | Value |
|-------------|-------|
| `STEAM_API_KEY` | Your Steam Web API key |
| `SUPABASE_URL` | `https://<project-id>.supabase.co` |
| `SUPABASE_KEY` | Your Supabase **anon** key |

### 3. Set up Supabase (free, 500 MB)

1. Create a free project at [supabase.com](https://supabase.com)
2. Open **SQL Editor** and run `supabase_schema.sql` (included in this repo)
3. Copy **Project URL** and **anon public key** from **Settings → API**
4. Add them as GitHub Secrets (see above)

### 4. Configure watchlist

Edit `watchlist.json` to add the App IDs you want to monitor:
```json
{
  "games": [264710, 427520, 892970]
}
```

### 5. Run collect manually (local)

```bash
export STEAM_API_KEY=your_key
export SUPABASE_URL=https://xxx.supabase.co
export SUPABASE_KEY=your_anon_key

dotnet run --project "Steam data puller/Steam data puller" -- collect
```

### 6. View data in Supabase

Open your Supabase project → **Table Editor** or run SQL:
```sql
SELECT app_id, captured_at, current_players, total_reviews, price_usd
FROM snapshots
WHERE app_id = 264710
ORDER BY captured_at DESC
LIMIT 24;
```

---

## Container

The collector ships as a Docker image. This is the recommended way to run it on
a Linux host or VPS.

### Quick start

```bash
cp .env.example .env        # then fill in STEAM_API_KEY (+ Supabase, optional)
docker compose up -d
docker compose logs -f
```

The container pulls once on start, then every hour. Scheduling happens inside
the app (`--interval` / `COLLECT_INTERVAL_SECONDS`) rather than via cron, so the
image needs no shell, no cron daemon and no init system.

```bash
docker compose down            # stop, keep data
docker compose down -v         # stop and delete the data volume
docker run --rm steamdata --help
```

### Where the data goes

| Path | Contents | Writable |
|------|----------|----------|
| `/app` | Application binaries, `watchlist.json` | No — read-only, owned by root |
| `/data/snapshots` | Timestamped JSON files | Yes — named volume |
| `/data/steam_data.db` | SQLite database | Yes — named volume |

With Supabase configured the container also pushes each snapshot to the cloud.
Without it, the delta check falls back to the local SQLite database so an
offline container still skips unchanged rows.

To change which games are tracked, edit `watchlist.json` and rebuild, or mount
your own over it:

```bash
docker run -v ./watchlist.json:/app/watchlist.json:ro ...
```

---

### Image size

Final image: **~87 MB** (~50 MB compressed on the registry), of which the
application itself is 2.1 MB. Four things get it there:

**1. Multi-stage build.** The .NET SDK needed to compile is ~800 MB. It lives in
the `build` stage only; the final image copies the compiled output and nothing
else.

**2. Chiseled base image.** `runtime:8.0-noble-chiseled` is Ubuntu stripped down
to what .NET needs — no shell, no package manager, no `apt`, no busybox:

| Base image | Size | Notes |
|------------|------|-------|
| `sdk:8.0` | ~800 MB | Build only, never ship this |
| `aspnet:8.0` | ~220 MB | Includes the web stack we don't use |
| `runtime:8.0` | ~190 MB | Full Ubuntu userland |
| `runtime:8.0-alpine` | ~85 MB | musl libc, needs `linux-musl-x64` |
| **`runtime:8.0-noble-chiseled`** | **~85 MB** | ← in use: glibc, but no shell |

**3. `InvariantGlobalization=true`.** Drops the ICU dependency (~30 MB) that the
chiseled image does not ship anyway. The app only formats dates and numbers, so
invariant culture is sufficient.

**4. `SatelliteResourceLanguages=en`.** The NuGet dependencies ship translated
exception messages in 13 languages. Removing them saves 240 KB.

**How to go smaller.** Publishing self-contained + trimmed onto
`runtime-deps:8.0-noble-chiseled` would land around 45–55 MB, and Native AOT
around 25 MB. Neither is enabled here: Dapper and `System.Text.Json` both
resolve properties by reflection, which the trimmer cannot see, so the build
would succeed and then fail at runtime with an empty result set. Going down that
path means switching to source-generated JSON contexts and adding an end-to-end
test against a real database before trusting it.

---

### Security

**Non-root by default.** The chiseled base defines UID 1654 and the Dockerfile
ends with `USER 1654`. Application files are copied as `root:root` with mode
`555`, so the running process can read and execute them but cannot modify them —
a compromised process cannot patch its own binary. The only path it owns is
`/data`.

**Read-only root filesystem.** `docker-compose.yml` sets `read_only: true`, so
every path except the `/data` volume and a 64 MB `noexec` tmpfs at `/tmp` is
immutable. This is also why `PublishSingleFile` is disabled: a single-file build
unpacks itself into a temp directory at startup and would fail here.

**No privilege escalation.** `cap_drop: ALL` removes every Linux capability, and
`no-new-privileges:true` blocks setuid binaries from raising privileges.

```yaml
read_only: true
security_opt: [no-new-privileges:true]
cap_drop: [ALL]
tmpfs: [/tmp:rw,noexec,nosuid,size=64m]
```

Verify a running container:

```bash
docker compose exec collector id          # fails: no shell in the image
docker inspect steam-collector --format '{{.Config.User}}'          # 1654
docker inspect steam-collector --format '{{.HostConfig.ReadonlyRootfs}}'  # true
```

**Secrets are never baked into the image.** API keys arrive as environment
variables from `.env`, which is gitignored and excluded by `.dockerignore`.
Anyone who pulls the image gets the code, not the credentials.

**Private registry access.** The image is published to GitHub Container Registry
and the package is private, so a pull requires a token that you issue:

```bash
# On the machine that should run the collector
echo $GHCR_TOKEN | docker login ghcr.io -u <your-github-username> --password-stdin
docker pull ghcr.io/slumper1122/steam-project:latest
```

Create `GHCR_TOKEN` at **GitHub → Settings → Developer settings → Personal
access tokens → Fine-grained**, scoped to this repository with only
`read:packages`. A read-only token cannot overwrite your images even if the
machine is compromised. Confirm the package is private at
**github.com/Slumper1122?tab=packages → steam-project → Package settings**.

**Vulnerability scanning.** `.github/workflows/docker.yml` runs Trivy on every
build and fails on any fixable HIGH or CRITICAL CVE, so a vulnerable image never
reaches the registry. Results are uploaded to the repository Security tab. The
same workflow asserts the image is not configured to run as root.

---

### Scheduling options compared

The container schedules itself, but that is one of several options:

| Approach | Reliability | Cost | Notes |
|----------|-------------|------|-------|
| GitHub Actions `schedule:` | Poor | Free | Dropped — runs arrived 5–6 h apart against an hourly cron |
| **External cron → `workflow_dispatch`** | Good | Free | ← in use for the hosted collector; see below |
| **Container `--interval` loop** | Good | VPS cost | ← in use for the container; no external dependency, drifts a few seconds per cycle |
| Host `cron` → `docker run` | Very good | VPS cost | Exact wall-clock times; set `COLLECT_INTERVAL_SECONDS=0` |
| systemd timer | Very good | VPS cost | Adds `Persistent=true` to catch up after downtime |
| Kubernetes `CronJob` | Very good | Cluster cost | Overkill for two games |

#### External cron setup

`collect.yml` has no `schedule:` trigger; it only responds to `workflow_dispatch`,
which an external cron calls hourly:

```
POST https://api.github.com/repos/<owner>/<repo>/actions/workflows/collect.yml/dispatches

Accept:                application/vnd.github+json
Authorization:         Bearer <fine-grained PAT>
Content-Type:          application/json
X-GitHub-Api-Version:  2022-11-28

{"ref":"main"}
```

The token needs exactly one repository permission: **Actions: Read and write**.
A successful dispatch answers `204 No Content` with an empty body.

Fine-grained tokens expire within a year, so the collection stops silently once it
lapses. Enable the cron service's failure notification to catch that.

To hand scheduling to the host instead, disable the internal loop and run the
container one-shot from crontab:

```cron
0 * * * * docker run --rm --env-file /opt/steam/.env \
  -e COLLECT_INTERVAL_SECONDS=0 \
  -v steam-data:/data ghcr.io/slumper1122/steam-project:latest
```

### Steam response quirks

Steam's `appdetails` endpoint documents the response as being keyed by the
requested AppID:

```json
{ "264710": { "success": true, "data": { "steam_appid": 264710, ... } } }
```

In practice it has been seen answering with an **unrelated key** while the payload
stays correct — a request for `264710` came back under `1619300`, and `427520`
under `3311770`. Because it is served as HTTP 200, retries do not help: the client
looked up the requested key, found nothing, and reported the game as missing.

`GetAppDetailsAsync` therefore trusts the payload over the key. It still matches by
key when present, falls back to the sole entry otherwise, and rejects the response
if `data.steam_appid` belongs to a different game, so one game's numbers can never
be filed under another.

### Transient failure handling

Steam rate-limits `store.steampowered.com/api/appdetails` without warning and
occasionally answers with `502`/`503`. Because a collection run exits non-zero if
any game fails, a single unlucky response used to turn a whole scheduled run red —
the same commit would succeed at 13:00 and fail at 18:00.

`RetryHandler` sits in the `HttpClient` pipeline, so every client (Steam, SteamSpy,
Supabase) inherits the same policy:

| Behaviour | Detail |
|-----------|--------|
| Retried statuses | `408`, `429`, `500`, `502`, `503`, `504` |
| Retried failures | Connection errors and per-attempt timeouts (30 s each) |
| Attempts | 4, backing off 1 s → 2 s → 4 s plus jitter, capped at 30 s |
| `Retry-After` | Honoured when present, as both a delta and a date |
| Non-idempotent requests | `POST` is replayed only on `429`, where the server definitively rejected it — otherwise a flaky connection could insert the same snapshot twice |

The per-attempt timeout lives in the handler rather than `HttpClient.Timeout`,
because that budget covers the whole pipeline and would be eaten by the backoff.

---

## Data accuracy notes

| Metric | Accuracy | Source |
|--------|----------|--------|
| Current player count | ✅ 100% exact | Steam Web API (`GetNumberOfCurrentPlayers`) |
| Review counts / score | ✅ 100% exact | Steam Store API |
| Price / discount | ✅ 100% exact | Steam Store API |
| Owner count | ⚠️ Estimate only | SteamSpy (Steam doesn't publish this publicly) |
| Wishlist count | ❌ Not available | No public API — Steam only exposes this to developers |
| Playtime | ⚠️ Limited | SteamSpy free tier often returns 0 |

---

## Dependencies

| Package | Purpose |
|---------|---------|
| `System.CommandLine` 2.0.0-beta4 | CLI argument parsing |
| `Microsoft.Data.Sqlite` 10.0.11 | SQLite database driver |
| `Dapper` 2.1.79 | Lightweight SQL mapper |

### Test dependencies

| Package | Purpose |
|---------|---------|
| `xUnit` | Test framework |
| `coverlet.collector` | Code coverage collection |
| `RichardSzalay.MockHttp` | HTTP mocking for unit tests |

---

## API Keys

**Steam Web API key** (free):
1. Log in at [steamcommunity.com/dev/apikey](https://steamcommunity.com/dev/apikey)
2. Register a domain (any name works for personal use)
3. Use via `--key` flag or `STEAM_API_KEY` environment variable

## License

MIT — see [LICENSE](LICENSE)
