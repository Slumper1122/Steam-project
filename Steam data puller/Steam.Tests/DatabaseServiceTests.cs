using SteamPuller.Models;
using SteamPuller.Services;

namespace Steam.Tests;

public class DatabaseServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _db;

    public DatabaseServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.db");
        _db     = new DatabaseService(_dbPath);
        _db.EnsureSchema();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static GameSnapshot MakeSnap(int appId = 264710, int players = 3340) => new()
    {
        AppId      = appId,
        Name       = "Subnautica",
        CapturedAt = DateTime.UtcNow,
        Info       = new GameInfo { Developer = "Dev", ReleaseDate = "Jan 23, 2018" },
        Players    = new PlayerStats { CurrentPlayers = players },
        Owners     = new OwnerStats  { EstimateLow = 5_000_000, EstimateHigh = 10_000_000, RawRange = "5,000,000 .. 10,000,000" },
        Reviews    = new ReviewStats { ScoreDescription = "Overwhelmingly Positive", TotalPositive = 120000, TotalReviews = 123000, PositivePercent = 97.2 },
        Playtime   = new PlaytimeStats(),
        Price      = new PriceInfo   { CurrentUsd = 29.99m },
        Updates    = new UpdateStats { FetchedCount = 5 },
        Achievements = new AchievementStats { TotalCount = 17 },
        Dlc        = new DlcInfo     { Count = 1 },
    };

    [Fact]
    public void EnsureSchema_RunTwice_DoesNotThrow()
    {
        _db.EnsureSchema();
    }

    [Fact]
    public void UpsertGame_NewGame_CanBeRetrieved()
    {
        var snap = MakeSnap();
        _db.UpsertGame(snap);

        Assert.True(_db.GameExists(264710));
    }

    [Fact]
    public void InsertSnapshot_ReturnsPositiveId()
    {
        var snap = MakeSnap();
        _db.UpsertGame(snap);
        var id = _db.InsertSnapshot(snap, "path/snap.json");

        Assert.True(id > 0);
    }

    [Fact]
    public void GetHistory_ReturnsSnapshotsNewestFirst()
    {
        var snap1 = MakeSnap(players: 1000);
        var snap2 = MakeSnap(players: 2000);
        _db.UpsertGame(snap1);
        _db.InsertSnapshot(snap1, "a.json");
        _db.InsertSnapshot(snap2, "b.json");

        var rows = _db.GetHistory(264710, 10).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(2000, rows[0].CurrentPlayers);
        Assert.Equal(1000, rows[1].CurrentPlayers);
    }

    [Fact]
    public void GetLastTwo_OneSnapshot_ReturnsNullPrev()
    {
        var snap = MakeSnap();
        _db.UpsertGame(snap);
        _db.InsertSnapshot(snap, "a.json");

        var (prev, curr) = _db.GetLastTwo(264710);

        Assert.Null(prev);
        Assert.NotNull(curr);
    }

    [Fact]
    public void GameExists_UnknownAppId_ReturnsFalse()
    {
        Assert.False(_db.GameExists(99999));
    }
}
