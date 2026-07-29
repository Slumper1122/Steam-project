# Requirements

## Functional Requirements

### FR-1 — Single-game data pull
The program accepts a Steam App ID as a CLI argument and fetches all defined metrics for that one game in a single run. No real-time polling.

### FR-2 — Supported metrics (10 dimensions of a game's life)

| # | Metric | Source API | Description |
|---|--------|-----------|-------------|
| 1 | Owner count | SteamSpy | Estimated number of owners (low / high range) |
| 2 | Wishlist count | _(not available via public API — planned)_ | Interest before release |
| 3 | Current / 24h peak CCU | Steam Web API | Live and recent peak concurrent player count |
| 4 | Review score | Steam Reviews API | Positive/negative ratio + score label |
| 5 | Review velocity | Steam Reviews API | Total review count (delta between snapshots shows velocity) |
| 6 | Avg / Median playtime | SteamSpy | Engagement depth in minutes (all time + last 2 weeks) |
| 7 | Update history | Steam News API | Recent developer announcements / patch notes |
| 8 | Price + discount | Steam Store API | Current price and active discount percentage |
| 9 | Achievement unlock rate | Steam Web API | Top-10 achievements by unlock percentage |
| 10 | DLC / Content count | Steam Store API | Number of DLC packages released |

### FR-3 — Output formats
- **JSON** — one file per pull, saved as `data/<appid>/<appid>_<timestamp>.json`
- **SQLite** — all snapshots persisted to `steam_data.db` for history and delta queries

### FR-4 — Delta tracking
The `delta` command compares the two most recent snapshots in the database and displays per-metric changes with directional indicators (↑ ↓ →).

### FR-5 — History view
The `history` command displays a tabular summary of all stored snapshots for a given App ID.

### FR-6 — Singleplayer games only
The tool is designed for singleplayer titles. Multiplayer CCU patterns and review dynamics differ fundamentally and are out of scope for v1.

---

## Non-Functional Requirements

### NFR-1 — No real-time polling
v1 is a one-shot CLI tool. Scheduling is handled externally (e.g. Windows Task Scheduler, cron, GitHub Actions).

### NFR-2 — Offline-first storage
All fetched data is stored locally (JSON + SQLite) so it can be queried without an internet connection after the initial pull.

### NFR-3 — Resilient fetching
If a non-critical API (SteamSpy, achievements) is unavailable, the pull continues and stores partial data with a warning. Only the Steam Store details API is considered critical.

### NFR-4 — .NET 8 LTS
The project targets .NET 8.0 LTS for stability and long-term support.

### NFR-5 — No external services required
Beyond the free public APIs listed in FR-2, no paid services, accounts, or external infrastructure are needed to run the tool.

---

## API Dependencies

| API | Key Required | Rate Limit | Terms |
|-----|-------------|-----------|-------|
| Steam Store API | No | ~200 req / 5 min | [Steam API ToS](https://store.steampowered.com/api/) |
| Steam Web API | Yes (free) | 100,000 req / day | [Steam Web API](https://steamcommunity.com/dev) |
| Steam Reviews API | No | ~200 req / 5 min | Steam API ToS |
| SteamSpy | No | ~4 req / min | [SteamSpy](https://steamspy.com/api.php) |

---

## Out of Scope (v1)

- Wishlist count (no public Steam API endpoint)
- Price history (requires ITAD API key)
- Multiplayer / competitive games
- Real-time streaming or webhooks
- Web UI or dashboard
- Machine learning / prediction
