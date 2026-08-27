using SteamPuller.Models;
using SteamPuller.Services;

namespace Steam.Tests;

public class JsonStorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"snap_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static GameSnapshot MakeSnap(int appId = 264710, DateTime? capturedAt = null) => new()
    {
        AppId      = appId,
        Name       = "Subnautica",
        CapturedAt = capturedAt ?? new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        Info       = new GameInfo { Developer = "Dev" },
        Players    = new PlayerStats { CurrentPlayers = 3340 },
        Owners     = new OwnerStats(),
        Reviews    = new ReviewStats(),
        Playtime   = new PlaytimeStats(),
        Price      = new PriceInfo { CurrentUsd = 29.99m },
        Updates    = new UpdateStats(),
        Achievements = new AchievementStats(),
        Dlc        = new DlcInfo(),
    };

    [Fact]
    public void Save_CreatesFileInCorrectDirectory()
    {
        var snap = MakeSnap();
        var path = JsonStorage.Save(snap, _dir);

        Assert.True(File.Exists(path));
        Assert.Contains($"{Path.DirectorySeparatorChar}264710{Path.DirectorySeparatorChar}", path);
    }

    [Fact]
    public void Save_FileNameContainsTimestamp()
    {
        var snap = MakeSnap();
        var path = JsonStorage.Save(snap, _dir);

        Assert.Contains("2026-01-01", Path.GetFileName(path));
    }

    [Fact]
    public void Save_FileIsValidJson()
    {
        var snap = MakeSnap();
        var path = JsonStorage.Save(snap, _dir);

        var text = File.ReadAllText(path);
        Assert.Contains("\"appId\"", text);
        Assert.Contains("\"subnautica\"", text.ToLower());
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsNull()
    {
        Assert.Null(JsonStorage.Load("/nonexistent/path.json"));
    }

    [Fact]
    public void Save_ThenLoad_PreservesData()
    {
        var snap = MakeSnap();
        var path = JsonStorage.Save(snap, _dir);

        var loaded = JsonStorage.Load(path);

        Assert.NotNull(loaded);
        Assert.Equal(264710, loaded.AppId);
        Assert.Equal("Subnautica", loaded.Name);
        Assert.Equal(3340, loaded.Players.CurrentPlayers);
        Assert.Equal(29.99m, loaded.Price.CurrentUsd);
    }

    [Fact]
    public void Save_MultiplePulls_CreatesMultipleFiles()
    {
        JsonStorage.Save(MakeSnap(capturedAt: new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc)), _dir);
        JsonStorage.Save(MakeSnap(capturedAt: new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc)), _dir);

        var files = Directory.GetFiles(Path.Combine(_dir, "264710"), "*.json");
        Assert.Equal(2, files.Length);
    }
}
