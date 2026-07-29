using Dapper;
using Microsoft.Data.Sqlite;
using SteamPuller.Models;

namespace SteamPuller.Services;

/// <summary>SQLite persistence layer using Dapper.</summary>
public sealed class DatabaseService(string dbPath)
{
    public void EnsureSchema()
    {
        using var conn = Open();
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS games (
                app_id          INTEGER PRIMARY KEY,
                name            TEXT    NOT NULL,
                developer       TEXT,
                publisher       TEXT,
                release_date    TEXT,
                is_early_access INTEGER DEFAULT 0,
                genres          TEXT,
                tags            TEXT,
                first_seen_at   TEXT    NOT NULL
            );

            CREATE TABLE IF NOT EXISTS snapshots (
                id                           INTEGER PRIMARY KEY AUTOINCREMENT,
                app_id                       INTEGER NOT NULL REFERENCES games(app_id),
                captured_at                  TEXT    NOT NULL,
                json_file                    TEXT,
                current_players              INTEGER,
                peak_ccu_24h                 INTEGER,
                owners_low                   INTEGER,
                owners_high                  INTEGER,
                review_score_desc            TEXT,
                total_positive               INTEGER,
                total_negative               INTEGER,
                total_reviews                INTEGER,
                positive_pct                 REAL,
                avg_playtime_forever_min     INTEGER,
                median_playtime_forever_min  INTEGER,
                avg_playtime_2w_min          INTEGER,
                price_usd                    REAL,
                discount_pct                 INTEGER,
                update_count                 INTEGER,
                achievement_count            INTEGER,
                dlc_count                    INTEGER
            );
            """);
    }

    public void UpsertGame(GameSnapshot s)
    {
        using var conn = Open();
        conn.Execute("""
            INSERT INTO games
                (app_id, name, developer, publisher, release_date, is_early_access,
                 genres, tags, first_seen_at)
            VALUES
                (@AppId, @Name, @Developer, @Publisher, @ReleaseDate, @IsEarlyAccess,
                 @Genres, @Tags, @FirstSeenAt)
            ON CONFLICT(app_id) DO UPDATE SET
                name            = excluded.name,
                developer       = excluded.developer,
                publisher       = excluded.publisher,
                release_date    = excluded.release_date,
                is_early_access = excluded.is_early_access,
                genres          = excluded.genres,
                tags            = excluded.tags;
            """,
            new
            {
                AppId         = s.AppId,
                Name          = s.Name,
                Developer     = s.Info.Developer,
                Publisher     = s.Info.Publisher,
                ReleaseDate   = s.Info.ReleaseDate,
                IsEarlyAccess = s.Info.IsEarlyAccess ? 1 : 0,
                Genres        = string.Join(", ", s.Info.Genres),
                Tags          = string.Join(", ", s.Info.Tags),
                FirstSeenAt   = s.CapturedAt.ToString("O"),
            });
    }

    public long InsertSnapshot(GameSnapshot s, string jsonFile)
    {
        using var conn = Open();
        conn.Execute("""
            INSERT INTO snapshots (
                app_id, captured_at, json_file,
                current_players, peak_ccu_24h,
                owners_low, owners_high,
                review_score_desc, total_positive, total_negative, total_reviews, positive_pct,
                avg_playtime_forever_min, median_playtime_forever_min, avg_playtime_2w_min,
                price_usd, discount_pct,
                update_count, achievement_count, dlc_count
            ) VALUES (
                @AppId, @CapturedAt, @JsonFile,
                @CurrentPlayers, @PeakCcu,
                @OwnLow, @OwnHigh,
                @ScoreDesc, @Positive, @Negative, @Total, @PosPct,
                @AvgForever, @MedForever, @Avg2w,
                @Price, @Discount,
                @Updates, @Achievements, @Dlcs
            );
            """,
            new
            {
                AppId        = s.AppId,
                CapturedAt   = s.CapturedAt.ToString("O"),
                JsonFile     = jsonFile,
                CurrentPlayers = s.Players.CurrentPlayers,
                PeakCcu      = s.Players.PeakCcu24h,
                OwnLow       = s.Owners.EstimateLow,
                OwnHigh      = s.Owners.EstimateHigh,
                ScoreDesc    = s.Reviews.ScoreDescription,
                Positive     = s.Reviews.TotalPositive,
                Negative     = s.Reviews.TotalNegative,
                Total        = s.Reviews.TotalReviews,
                PosPct       = s.Reviews.PositivePercent,
                AvgForever   = s.Playtime.AverageForeverMinutes,
                MedForever   = s.Playtime.MedianForeverMinutes,
                Avg2w        = s.Playtime.AverageTwoWeeksMinutes,
                Price        = (double)s.Price.CurrentUsd,
                Discount     = s.Price.DiscountPercent,
                Updates      = s.Updates.FetchedCount,
                Achievements = s.Achievements.TotalCount,
                Dlcs         = s.Dlc.Count,
            });
        return conn.ExecuteScalar<long>("SELECT last_insert_rowid();");
    }

    public IEnumerable<SnapshotRow> GetHistory(int appId, int limit = 20)
    {
        using var conn = Open();
        return conn.Query<SnapshotRow>("""
            SELECT
                id                         AS Id,
                captured_at                AS CapturedAt,
                json_file                  AS JsonFile,
                current_players            AS CurrentPlayers,
                owners_low                 AS OwnersLow,
                review_score_desc          AS ReviewScoreDesc,
                positive_pct               AS PositivePct,
                avg_playtime_forever_min   AS AvgPlaytimeForeverMin,
                price_usd                  AS PriceUsd,
                discount_pct               AS DiscountPct,
                update_count               AS UpdateCount,
                dlc_count                  AS DlcCount
            FROM snapshots
            WHERE app_id = @AppId
            ORDER BY captured_at DESC
            LIMIT @Limit;
            """,
            new { AppId = appId, Limit = limit });
    }

    public (SnapshotRow? prev, SnapshotRow? curr) GetLastTwo(int appId)
    {
        var rows = GetHistory(appId, 2).ToList();
        return rows.Count >= 2 ? (rows[1], rows[0]) : (null, rows.FirstOrDefault());
    }

    public bool GameExists(int appId)
    {
        using var conn = Open();
        return conn.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM games WHERE app_id = @AppId;",
            new { AppId = appId }) > 0;
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        return conn;
    }
}
