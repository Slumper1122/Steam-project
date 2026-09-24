using System.Globalization;
using SteamPuller.Commands;
using SteamPuller.Models;
using SteamPuller.Services;

namespace Steam.Tests;

/// <summary>
/// The read-only reporting commands (`history` and `delta`). They only touch
/// SQLite and the console, so they are driven against a temporary database.
/// </summary>
public class ReportCommandTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _db;
    private readonly TextWriter _originalOut;
    private readonly StringWriter _captured = new();
    private readonly CultureInfo _originalCulture;

    public ReportCommandTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"report_{Guid.NewGuid():N}.db");
        _db     = new DatabaseService(_dbPath);
        _db.EnsureSchema();

        // Production runs with InvariantGlobalization, so pin the culture here
        // too. Otherwise "1,111" becomes "1 111" on a Hungarian workstation and
        // these assertions would only pass on some machines.
        _originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        _originalOut = Console.Out;
        Console.SetOut(_captured);
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        CultureInfo.CurrentCulture = _originalCulture;
        _captured.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private string Output => _captured.ToString();

    private static GameSnapshot MakeSnap(
        int players = 3340, double posPct = 97.2, decimal price = 29.99m,
        int discount = 0, string scoreDesc = "Overwhelmingly Positive",
        DateTime? capturedAt = null) => new()
    {
        AppId      = 264710,
        Name       = "Subnautica",
        CapturedAt = capturedAt ?? DateTime.UtcNow,
        Info       = new GameInfo { Developer = "Unknown Worlds" },
        Players    = new PlayerStats { CurrentPlayers = players },
        Owners     = new OwnerStats  { EstimateLow = 5_000_000, EstimateHigh = 10_000_000 },
        Reviews    = new ReviewStats { ScoreDescription = scoreDesc, TotalReviews = 123000, PositivePercent = posPct },
        Playtime   = new PlaytimeStats(),
        Price      = new PriceInfo   { CurrentUsd = price, DiscountPercent = discount },
        Updates    = new UpdateStats { FetchedCount = 20 },
        Achievements = new AchievementStats { TotalCount = 17 },
        Dlc        = new DlcInfo     { Count = 1 },
    };

    private void Store(GameSnapshot snap)
    {
        _db.UpsertGame(snap);
        _db.InsertSnapshot(snap, "snap.json");
    }

    private static DateTime At(int hour) => new(2026, 1, 1, hour, 0, 0, DateTimeKind.Utc);

    // ── history ───────────────────────────────────────────────────────────────

    [Fact]
    public void History_UnknownGame_ReturnsExitCode1()
    {
        Assert.Equal(1, HistoryCommand.Run(999999, 10, _dbPath));
        Assert.Contains("No data found", Output);
    }

    [Fact]
    public void History_GameWithSnapshots_PrintsRows()
    {
        Store(MakeSnap(players: 1111, capturedAt: At(10)));
        Store(MakeSnap(players: 2222, capturedAt: At(11)));

        Assert.Equal(0, HistoryCommand.Run(264710, 10, _dbPath));
        Assert.Contains("History for AppID 264710", Output);
        Assert.Contains("1,111", Output);
        Assert.Contains("2,222", Output);
    }

    [Fact]
    public void History_RespectsLimit()
    {
        Store(MakeSnap(players: 1111, capturedAt: At(10)));
        Store(MakeSnap(players: 2222, capturedAt: At(11)));
        Store(MakeSnap(players: 3333, capturedAt: At(12)));

        HistoryCommand.Run(264710, 1, _dbPath);

        Assert.Contains("last 1 snapshots", Output);
        Assert.Contains("3,333", Output);
        Assert.DoesNotContain("1,111", Output);
    }

    [Fact]
    public void History_DiscountedAndFreeRowsRender()
    {
        Store(MakeSnap(price: 0m, capturedAt: At(10)));
        Store(MakeSnap(price: 14.99m, discount: 50, capturedAt: At(11)));

        HistoryCommand.Run(264710, 10, _dbPath);

        Assert.Contains("Free", Output);
        Assert.Contains("$14.99", Output);
        Assert.Contains("50%", Output);
    }

    // ── delta ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Delta_NoSnapshots_ReturnsExitCode1()
    {
        Assert.Equal(1, DeltaCommand.Run(264710, _dbPath));
        Assert.Contains("No snapshots found", Output);
    }

    [Fact]
    public void Delta_SingleSnapshot_ExplainsNothingToCompare()
    {
        Store(MakeSnap(capturedAt: At(10)));

        Assert.Equal(0, DeltaCommand.Run(264710, _dbPath));
        Assert.Contains("Only one snapshot exists", Output);
    }

    [Fact]
    public void Delta_PlayersIncreased_ShowsUpwardChange()
    {
        Store(MakeSnap(players: 1000, capturedAt: At(10)));
        Store(MakeSnap(players: 1500, capturedAt: At(11)));

        Assert.Equal(0, DeltaCommand.Run(264710, _dbPath));
        Assert.Contains("Delta for AppID 264710", Output);
        Assert.Contains("↑ +500", Output);
    }

    [Fact]
    public void Delta_PlayersDropped_ShowsDownwardChange()
    {
        Store(MakeSnap(players: 1500, capturedAt: At(10)));
        Store(MakeSnap(players: 1000, capturedAt: At(11)));

        DeltaCommand.Run(264710, _dbPath);

        Assert.Contains("↓ -500", Output);
    }

    [Fact]
    public void Delta_IdenticalSnapshots_ShowsNoChange()
    {
        Store(MakeSnap(players: 1000, capturedAt: At(10)));
        Store(MakeSnap(players: 1000, capturedAt: At(11)));

        DeltaCommand.Run(264710, _dbPath);

        Assert.Contains("→ no change", Output);
    }

    [Fact]
    public void Delta_ReviewLabelChanged_IsHighlighted()
    {
        Store(MakeSnap(scoreDesc: "Very Positive", capturedAt: At(10)));
        Store(MakeSnap(scoreDesc: "Overwhelmingly Positive", capturedAt: At(11)));

        DeltaCommand.Run(264710, _dbPath);

        Assert.Contains("Review label", Output);
        Assert.Contains("CHANGED", Output);
    }

    [Fact]
    public void Delta_ReviewLabelUnchanged_IsOmitted()
    {
        Store(MakeSnap(capturedAt: At(10)));
        Store(MakeSnap(players: 9999, capturedAt: At(11)));

        DeltaCommand.Run(264710, _dbPath);

        Assert.DoesNotContain("Review label", Output);
    }

    [Fact]
    public void Delta_SaleStarted_ShowsPriceDrop()
    {
        Store(MakeSnap(price: 29.99m, discount: 0,  capturedAt: At(10)));
        Store(MakeSnap(price: 14.99m, discount: 50, capturedAt: At(11)));

        DeltaCommand.Run(264710, _dbPath);

        Assert.Contains("Price (USD)", Output);
        Assert.Contains("$29.99", Output);
        Assert.Contains("$14.99", Output);
        Assert.Contains("Discount %", Output);
    }
}
