using System.Globalization;
using System.Net;
using RichardSzalay.MockHttp;
using SteamPuller.Commands;
using SteamPuller.Services;

namespace Steam.Tests;

/// <summary>
/// End-to-end coverage of `pull`: every Steam API is mocked, so the command
/// runs its real storage and reporting code without touching the network.
/// </summary>
public class PullCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pull_{Guid.NewGuid():N}");
    private readonly TextWriter _originalOut;
    private readonly StringWriter _captured = new();
    private readonly CultureInfo _originalCulture;

    public PullCommandTests()
    {
        Directory.CreateDirectory(_dir);
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
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string Output  => _captured.ToString();
    private string OutDir  => Path.Combine(_dir, "data");
    private string DbPath  => Path.Combine(_dir, "test.db");

    private static MockHttpMessageHandler AllApisRespond(
        string? appDetails = null, string? steamSpy = null, string? achievements = null)
    {
        var mock = new MockHttpMessageHandler();
        mock.When("https://store.steampowered.com/api/appdetails*")
            .Respond("application/json", appDetails ?? Fixtures.AppDetails());
        mock.When("https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/*")
            .Respond("application/json", Fixtures.CurrentPlayers());
        mock.When("https://store.steampowered.com/appreviews/*")
            .Respond("application/json", Fixtures.ReviewSummary());
        mock.When("https://api.steampowered.com/ISteamNews/GetNewsForApp/*")
            .Respond("application/json", Fixtures.News);
        mock.When("https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/*")
            .Respond("application/json", achievements ?? Fixtures.Achievements);
        mock.When("https://steamspy.com/api.php*")
            .Respond("application/json", steamSpy ?? Fixtures.SteamSpy);
        return mock;
    }

    private Task<int> Run(HttpMessageHandler mock, int appId = Fixtures.AppId) =>
        PullCommand.RunAsync(appId, "TESTKEY", OutDir, DbPath, CancellationToken.None, mock);

    [Fact]
    public async Task MissingApiKey_ReturnsExitCode1()
    {
        var previous = Environment.GetEnvironmentVariable("STEAM_API_KEY");
        Environment.SetEnvironmentVariable("STEAM_API_KEY", null);
        try
        {
            var exit = await PullCommand.RunAsync(
                Fixtures.AppId, null, OutDir, DbPath, CancellationToken.None, AllApisRespond());
            Assert.Equal(1, exit);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STEAM_API_KEY", previous);
        }
    }

    [Fact]
    public async Task SuccessfulPull_ReturnsExitCode0()
    {
        Assert.Equal(0, await Run(AllApisRespond()));
    }

    [Fact]
    public async Task SuccessfulPull_WritesJsonAndDatabase()
    {
        await Run(AllApisRespond());

        var files = Directory.GetFiles(Path.Combine(OutDir, "264710"), "*.json");
        Assert.Single(files);
        Assert.True(File.Exists(DbPath));
        Assert.Contains("snapshot #1", Output);
    }

    [Fact]
    public async Task SuccessfulPull_PrintsSummary()
    {
        await Run(AllApisRespond());

        Assert.Contains("Subnautica", Output);
        Assert.Contains("AppID 264710", Output);
        Assert.Contains("Current players", Output);
        Assert.Contains("3,340", Output);
        Assert.Contains("5,000,000", Output);   // owner estimate low
        Assert.Contains("$29.99", Output);
        Assert.Contains("Top achievements by unlock rate", Output);
    }

    [Fact]
    public async Task UnknownGame_ReturnsExitCode1()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("https://store.steampowered.com/api/appdetails*")
            .Respond("application/json", """{"99999":{"success":false}}""");
        mock.When("*").Respond("application/json", "{}");

        var exit = await Run(mock, appId: 99999);

        Assert.Equal(1, exit);
        Assert.DoesNotContain("snapshot #", Output);
    }

    [Fact]
    public async Task FreeGame_SummaryShowsFree()
    {
        var freeGame = Fixtures.AppDetails().Replace("\"is_free\": false", "\"is_free\": true");

        await Run(AllApisRespond(appDetails: freeGame));

        Assert.Contains("Free", Output);
    }

    [Fact]
    public async Task SteamSpyUnavailable_StillSucceedsWithNaOwners()
    {
        var withoutSpy = new MockHttpMessageHandler();
        withoutSpy.When("https://store.steampowered.com/api/appdetails*")
                  .Respond("application/json", Fixtures.AppDetails());
        withoutSpy.When("https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/*")
                  .Respond("application/json", Fixtures.CurrentPlayers());
        withoutSpy.When("https://store.steampowered.com/appreviews/*")
                  .Respond("application/json", Fixtures.ReviewSummary());
        withoutSpy.When("https://api.steampowered.com/ISteamNews/GetNewsForApp/*")
                  .Respond("application/json", Fixtures.News);
        withoutSpy.When("https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/*")
                  .Respond("application/json", Fixtures.Achievements);
        withoutSpy.When("https://steamspy.com/api.php*")
                  .Respond(HttpStatusCode.ServiceUnavailable);

        // SteamSpy stays down through every retry, so run without the backoff.
        var exit = await Run(new RetryHandler(withoutSpy, delay: (_, _) => Task.CompletedTask));

        Assert.Equal(0, exit);
        Assert.Contains("N/A", Output);
    }

    [Fact]
    public async Task TwoPulls_ProduceTwoSnapshots()
    {
        await Run(AllApisRespond());
        await Run(AllApisRespond());

        Assert.Contains("snapshot #1", Output);
        Assert.Contains("snapshot #2", Output);
    }
}
