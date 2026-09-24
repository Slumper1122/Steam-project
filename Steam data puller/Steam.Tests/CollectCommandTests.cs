using System.Net;
using RichardSzalay.MockHttp;
using SteamPuller.Commands;
using SteamPuller.Services;

namespace Steam.Tests;

/// <summary>
/// Config-validation paths of `collect`. These run before any network or
/// database access, so no mocking is required.
/// </summary>
public class CollectCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"collect_{Guid.NewGuid():N}");

    public CollectCommandTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        // The command opens SQLite, which keeps the file handle pooled.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string WriteWatchlist(string contents)
    {
        var path = Path.Combine(_dir, "watchlist.json");
        File.WriteAllText(path, contents);
        return path;
    }

    private Task<int> Run(string watchlistPath, string? apiKey = "TESTKEY") =>
        CollectCommand.RunAsync(
            watchlistPath,
            apiKey,
            supabaseUrl: null,
            supabaseKey: null,
            outputDir: Path.Combine(_dir, "out"),
            dbPath: Path.Combine(_dir, "test.db"),
            intervalSeconds: 0,
            ct: CancellationToken.None);

    [Fact]
    public async Task MissingApiKey_ReturnsExitCode1()
    {
        var previous = Environment.GetEnvironmentVariable("STEAM_API_KEY");
        Environment.SetEnvironmentVariable("STEAM_API_KEY", null);
        try
        {
            var exit = await Run(WriteWatchlist("""{"games":[264710]}"""), apiKey: null);
            Assert.Equal(1, exit);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STEAM_API_KEY", previous);
        }
    }

    [Fact]
    public async Task MissingWatchlistFile_ReturnsExitCode1()
    {
        var exit = await Run(Path.Combine(_dir, "does-not-exist.json"));
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task MalformedWatchlistJson_ReturnsExitCode1()
    {
        var exit = await Run(WriteWatchlist("{ not json"));
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task WatchlistWithoutGamesProperty_ReturnsExitCode1()
    {
        var exit = await Run(WriteWatchlist("""{"titles":[264710]}"""));
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task EmptyWatchlist_SucceedsWithNothingToDo()
    {
        var exit = await Run(WriteWatchlist("""{"games":[]}"""));
        Assert.Equal(0, exit);
    }

    // ── Collection runs ───────────────────────────────────────────────────────

    /// <param name="appDetails">
    /// Overrides the store response. MockHttp answers with the first matching rule,
    /// so a caller cannot register a competing one afterwards.
    /// </param>
    private static MockHttpMessageHandler SteamApisRespond(
        Func<HttpRequestMessage, HttpResponseMessage>? appDetails = null)
    {
        var mock = new MockHttpMessageHandler();
        var details = mock.When("https://store.steampowered.com/api/appdetails*");
        if (appDetails is null)
            details.Respond("application/json", Fixtures.AppDetails());
        else
            details.Respond(appDetails);

        mock.When("https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/*")
            .Respond("application/json", Fixtures.CurrentPlayers());
        mock.When("https://store.steampowered.com/appreviews/*")
            .Respond("application/json", Fixtures.ReviewSummary());
        mock.When("https://api.steampowered.com/ISteamNews/GetNewsForApp/*")
            .Respond("application/json", Fixtures.News);
        mock.When("https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/*")
            .Respond("application/json", Fixtures.Achievements);
        mock.When("https://steamspy.com/api.php*")
            .Respond("application/json", Fixtures.SteamSpy);
        return mock;
    }

    /// <summary>
    /// Wraps a mock in the production retry pipeline but skips the backoff, so the
    /// retry behaviour is exercised without the test sitting through seven seconds
    /// of waiting.
    /// </summary>
    private static RetryHandler NoSleep(HttpMessageHandler inner) =>
        new(inner, delay: (_, _) => Task.CompletedTask);

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private Task<int> RunWith(HttpMessageHandler mock, string? sbUrl = null, string? sbKey = null) =>
        CollectCommand.RunAsync(
            WriteWatchlist("""{"games":[264710]}"""),
            apiKey: "TESTKEY",
            supabaseUrl: sbUrl,
            supabaseKey: sbKey,
            outputDir: Path.Combine(_dir, "out"),
            dbPath: Path.Combine(_dir, "test.db"),
            intervalSeconds: 0,
            ct: CancellationToken.None,
            httpHandler: mock);

    [Fact]
    public async Task LocalOnly_StoresSnapshot()
    {
        Assert.Equal(0, await RunWith(SteamApisRespond()));

        var files = Directory.GetFiles(Path.Combine(_dir, "out", "264710"), "*.json");
        Assert.Single(files);
    }

    [Fact]
    public async Task LocalOnly_SecondIdenticalRun_IsSkipped()
    {
        await RunWith(SteamApisRespond());
        await RunWith(SteamApisRespond());

        // The delta check must fall back to SQLite when Supabase is absent,
        // so unchanged data produces no second file.
        var files = Directory.GetFiles(Path.Combine(_dir, "out", "264710"), "*.json");
        Assert.Single(files);
    }

    [Fact]
    public async Task UnreachableSteamApi_ReturnsExitCode1()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("*").Respond(HttpStatusCode.ServiceUnavailable);

        Assert.Equal(1, await RunWith(NoSleep(mock)));
    }

    [Fact]
    public async Task RateLimitedSteamApi_RecoversOnRetry()
    {
        // Steam throttles /api/appdetails without warning; one 429 must not
        // fail the whole run.
        var attempts = 0;
        var mock = SteamApisRespond(appDetails: _ =>
            ++attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : JsonResponse(HttpStatusCode.OK, Fixtures.AppDetails()));

        Assert.Equal(0, await RunWith(NoSleep(mock)));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task WithSupabase_PushesGameAndSnapshot()
    {
        const string url = "https://testproject.supabase.co";
        var mock = SteamApisRespond();

        int lookups = 0, gamePosts = 0, snapshotPosts = 0;

        mock.When(HttpMethod.Get, $"{url}/rest/v1/snapshots*")
            .Respond(_ => { lookups++; return JsonResponse(HttpStatusCode.OK, "[]"); });
        mock.When(HttpMethod.Post, $"{url}/rest/v1/games")
            .Respond(_ => { gamePosts++; return JsonResponse(HttpStatusCode.Created, ""); });
        mock.When(HttpMethod.Post, $"{url}/rest/v1/snapshots")
            .Respond(_ => { snapshotPosts++; return JsonResponse(HttpStatusCode.Created, ""); });

        Assert.Equal(0, await RunWith(mock, url, "sb_secret_key"));

        Assert.Equal(1, lookups);        // reads the previous row before deciding
        Assert.Equal(1, gamePosts);      // upserts game metadata
        Assert.Equal(1, snapshotPosts);  // uploads the new snapshot
    }

    [Fact]
    public async Task WithSupabase_UnchangedRemoteRow_SkipsUpload()
    {
        const string url = "https://testproject.supabase.co";
        var mock = SteamApisRespond();

        // Remote already holds exactly what the fixtures produce.
        mock.When(HttpMethod.Get, $"{url}/rest/v1/snapshots*").Respond("application/json",
            """[{"current_players":3340,"total_reviews":123000,"owners_low":5000000,"price_usd":29.99,"discount_pct":0}]""");

        Assert.Equal(0, await RunWith(mock, url, "sb_secret_key"));
        Assert.False(Directory.Exists(Path.Combine(_dir, "out", "264710")));
    }

    [Fact]
    public async Task WithSupabase_LookupFails_StillStoresLocally()
    {
        const string url = "https://testproject.supabase.co";
        var mock = SteamApisRespond();
        mock.When(HttpMethod.Get,  $"{url}/rest/v1/snapshots*").Respond(HttpStatusCode.NotFound);
        mock.When(HttpMethod.Post, $"{url}/rest/v1/*").Respond(HttpStatusCode.Created);

        Assert.Equal(0, await RunWith(mock, url, "sb_secret_key"));
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "out", "264710"), "*.json"));
    }

    [Fact]
    public async Task IntervalLoop_StopsOnCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var exit = await CollectCommand.RunAsync(
            WriteWatchlist("""{"games":[]}"""),
            apiKey: "TESTKEY",
            supabaseUrl: null,
            supabaseKey: null,
            outputDir: Path.Combine(_dir, "out"),
            dbPath: Path.Combine(_dir, "test.db"),
            intervalSeconds: 3600,
            ct: cts.Token);

        Assert.Equal(0, exit);
    }
}
