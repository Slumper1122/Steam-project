namespace SteamPuller.Models;

/// <summary>Full snapshot of a game's metrics at a single point in time.</summary>
public sealed class GameSnapshot
{
    public int      AppId       { get; init; }
    public string   Name        { get; init; } = string.Empty;
    public DateTime CapturedAt  { get; init; }
    public string   SchemaVersion { get; init; } = "1.0";

    public GameInfo        Info         { get; init; } = new();
    public PlayerStats     Players      { get; init; } = new();
    public OwnerStats      Owners       { get; init; } = new();
    public ReviewStats     Reviews      { get; init; } = new();
    public PlaytimeStats   Playtime     { get; init; } = new();
    public PriceInfo       Price        { get; init; } = new();
    public UpdateStats     Updates      { get; init; } = new();
    public AchievementStats Achievements { get; init; } = new();
    public DlcInfo         Dlc          { get; init; } = new();
}

// ── Metric 1 – static game info ─────────────────────────────────────────────
public sealed class GameInfo
{
    public string       Developer        { get; init; } = string.Empty;
    public string       Publisher        { get; init; } = string.Empty;
    public string       ReleaseDate      { get; init; } = string.Empty;
    public bool         IsEarlyAccess    { get; init; }
    public string       ShortDescription { get; init; } = string.Empty;
    public List<string> Genres           { get; init; } = [];
    public List<string> Tags             { get; init; } = [];
}

// ── Metric 2 – player count (live + 24h peak from SteamSpy) ─────────────────
public sealed class PlayerStats
{
    public int CurrentPlayers  { get; init; }
    public int PeakCcu24h      { get; init; }
}

// ── Metric 3 – owner / sales estimate (SteamSpy) ────────────────────────────
public sealed class OwnerStats
{
    public int    EstimateLow  { get; init; }
    public int    EstimateHigh { get; init; }
    public string RawRange     { get; init; } = string.Empty;
}

// ── Metric 4+5 – review score + velocity ────────────────────────────────────
public sealed class ReviewStats
{
    public string ScoreDescription { get; init; } = string.Empty;
    public int    TotalPositive    { get; init; }
    public int    TotalNegative    { get; init; }
    public int    TotalReviews     { get; init; }
    public double PositivePercent  { get; init; }
}

// ── Metric 6 – playtime (SteamSpy) ──────────────────────────────────────────
public sealed class PlaytimeStats
{
    public int AverageForeverMinutes  { get; init; }
    public int MedianForeverMinutes   { get; init; }
    public int AverageTwoWeeksMinutes { get; init; }
    public int MedianTwoWeeksMinutes  { get; init; }
}

// ── Metric 7 – price + discount (Steam Store) ───────────────────────────────
public sealed class PriceInfo
{
    public bool    IsFree          { get; init; }
    public decimal CurrentUsd      { get; init; }
    public decimal OriginalUsd     { get; init; }
    public int     DiscountPercent { get; init; }
}

// ── Metric 8 – update / patch history (Steam News) ──────────────────────────
public sealed class UpdateStats
{
    public int              FetchedCount { get; init; }
    public List<UpdateEntry> Items       { get; init; } = [];
}

public sealed class UpdateEntry
{
    public string   Title          { get; init; } = string.Empty;
    public string   Url            { get; init; } = string.Empty;
    public long     UnixTimestamp  { get; init; }
    public DateTime PublishedAt    { get; init; }
    public string   Author         { get; init; } = string.Empty;
    public string   FeedLabel      { get; init; } = string.Empty;
}

// ── Metric 9 – achievement unlock rates (Steam Web API) ─────────────────────
public sealed class AchievementStats
{
    public int                   TotalCount { get; init; }
    public List<AchievementEntry> Top10     { get; init; } = [];
}

public sealed class AchievementEntry
{
    public string Name          { get; init; } = string.Empty;
    public double UnlockPercent { get; init; }
}

// ── Metric 10 – DLC / content expansions (Steam Store) ──────────────────────
public sealed class DlcInfo
{
    public int       Count  { get; init; }
    public List<int> AppIds { get; init; } = [];
}

// ── DB row model ─────────────────────────────────────────────────────────────
public sealed class SnapshotRow
{
    public long   Id                      { get; set; }
    public string CapturedAt              { get; set; } = string.Empty;
    public int    CurrentPlayers          { get; set; }
    public int    OwnersLow               { get; set; }
    public string ReviewScoreDesc         { get; set; } = string.Empty;
    public double PositivePct             { get; set; }
    public int    AvgPlaytimeForeverMin   { get; set; }
    public double PriceUsd                { get; set; }
    public int    DiscountPct             { get; set; }
    public int    UpdateCount             { get; set; }
    public int    DlcCount               { get; set; }
    public string JsonFile               { get; set; } = string.Empty;
}
