# Steam Data Puller

A C# CLI tool that fetches and stores game metrics from Steam APIs for a single game.
Designed to capture the full lifecycle of a singleplayer game — from early access through maturity.

## Features

- **10 metrics** per snapshot: owners, CCU, reviews, playtime, price, updates, achievements, DLC
- **JSON storage** — one timestamped file per pull under `data/<appid>/`
- **SQLite database** — all snapshots persisted for querying over time
- **Delta view** — compare any two consecutive snapshots metric by metric
- **History table** — tabular view of all stored snapshots

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    CLI (steamdata.exe)                   │
│  pull <appid>  │  history <appid>  │  delta <appid>      │
└───────────────────────┬─────────────────────────────────┘
                        │
          ┌─────────────▼──────────────┐
          │       SnapshotBuilder       │
          │   orchestrates all fetches  │
          └──┬──────┬──────┬───────┬───┘
             │      │      │       │
    ┌────────▼─┐ ┌──▼───┐ ┌▼────┐ ┌▼──────────┐
    │ Steam    │ │Steam │ │Steam│ │ SteamSpy  │
    │ Store    │ │ Web  │ │ Rev │ │    API    │
    │   API    │ │  API │ │ API │ │           │
    └──────────┘ └──────┘ └─────┘ └───────────┘
                        │
          ┌─────────────▼──────────────┐
          │      GameSnapshot model     │
          └──────┬──────────────┬───────┘
                 │              │
          ┌──────▼────┐  ┌──────▼────┐
          │ JSON file │  │  SQLite   │
          │  storage  │  │    DB     │
          └───────────┘  └───────────┘
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
        └── Commands/
            ├── PullCommand.cs
            ├── HistoryCommand.cs
            └── DeltaCommand.cs
```

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `System.CommandLine` | 2.0.0-beta4 | CLI argument parsing |
| `Microsoft.Data.Sqlite` | 10.0.10 | SQLite database driver |
| `Dapper` | 2.1.79 | Lightweight SQL mapper |

## API Keys

**Steam Web API key** (free):
1. Log in at [steamcommunity.com/dev/apikey](https://steamcommunity.com/dev/apikey)
2. Register a domain (any name works for personal use)
3. Use via `--key` flag or `STEAM_API_KEY` environment variable

## License

MIT — see [LICENSE](LICENSE)
