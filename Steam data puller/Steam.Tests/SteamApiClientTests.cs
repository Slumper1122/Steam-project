using RichardSzalay.MockHttp;
using SteamPuller.Clients;

namespace Steam.Tests;

public class SteamApiClientTests
{
    private const string FakeKey = "TESTKEY";

    // ── GetAppDetails ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAppDetails_ValidApp_ReturnsDataNode()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"https://store.steampowered.com/api/appdetails*")
            .Respond("application/json", Fixtures.AppDetails());

        var client = new SteamApiClient(mock.ToHttpClient(), FakeKey);
        var result = await client.GetAppDetailsAsync(Fixtures.AppId);

        Assert.NotNull(result);
        Assert.Equal("Subnautica", result["name"]?.GetValue<string>());
    }

    [Fact]
    public async Task GetAppDetails_InvalidApp_ReturnsNull()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("*").Respond("application/json", """{"99999":{"success":false}}""");

        var client = new SteamApiClient(mock.ToHttpClient(), FakeKey);
        var result = await client.GetAppDetailsAsync(99999);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAppDetails_UnexpectedEnvelopeKey_StillResolvesByPayload()
    {
        // Observed in production: Steam answered a request for 264710 under the
        // key 1619300 while data.steam_appid stayed correct, which silently
        // failed every collection run.
        var mock = new MockHttpMessageHandler();
        mock.When("*").Respond("application/json",
            Fixtures.AppDetails(Fixtures.AppId, envelopeKey: 1619300));

        var client = new SteamApiClient(mock.ToHttpClient(), FakeKey);
        var result = await client.GetAppDetailsAsync(Fixtures.AppId);

        Assert.NotNull(result);
        Assert.Equal("Subnautica", result["name"]?.GetValue<string>());
    }

    [Fact]
    public async Task GetAppDetails_PayloadForADifferentGame_ReturnsNull()
    {
        // Accepting this would file one game's players and price under another.
        var mock = new MockHttpMessageHandler();
        mock.When("*").Respond("application/json",
            Fixtures.AppDetails(appId: 730, envelopeKey: 264710));

        var client = new SteamApiClient(mock.ToHttpClient(), FakeKey);
        var result = await client.GetAppDetailsAsync(427520);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAppDetails_MultipleEntriesWithoutAMatchingKey_ReturnsNull()
    {
        // With more than one entry there is no unambiguous fallback.
        var mock = new MockHttpMessageHandler();
        mock.When("*").Respond("application/json",
            """{"111":{"success":true,"data":{"steam_appid":111}},"222":{"success":true,"data":{"steam_appid":222}}}""");

        var client = new SteamApiClient(mock.ToHttpClient(), FakeKey);
        var result = await client.GetAppDetailsAsync(264710);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAppDetails_EmptyEnvelope_ReturnsNull()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("*").Respond("application/json", "{}");

        var client = new SteamApiClient(mock.ToHttpClient(), FakeKey);

        Assert.Null(await client.GetAppDetailsAsync(264710));
    }

    // ── GetCurrentPlayers ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetCurrentPlayers_ReturnsCorrectCount()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("*GetNumberOfCurrentPlayers*")
            .Respond("application/json", Fixtures.CurrentPlayers(5234));

        var client = new SteamApiClient(mock.ToHttpClient(), FakeKey);
        var count  = await client.GetCurrentPlayersAsync(Fixtures.AppId);

        Assert.Equal(5234, count);
    }

    // ── GetReviewSummary ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetReviewSummary_ReturnsScoreDesc()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("*appreviews*")
            .Respond("application/json", Fixtures.ReviewSummary());

        var client = new SteamApiClient(mock.ToHttpClient(), FakeKey);
        var result = await client.GetReviewSummaryAsync(Fixtures.AppId);

        Assert.NotNull(result);
        Assert.Equal("Overwhelmingly Positive", result["review_score_desc"]?.GetValue<string>());
    }

    [Fact]
    public async Task GetReviewSummary_HttpError_ReturnsNull()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("*appreviews*")
            .Respond(System.Net.HttpStatusCode.InternalServerError);

        var client = new SteamApiClient(mock.ToHttpClient(), FakeKey);
        var result = await client.GetReviewSummaryAsync(Fixtures.AppId);

        Assert.Null(result);
    }

    // ── GetAchievements ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetAchievements_ReturnsArray()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("*GetGlobalAchievementPercentages*")
            .Respond("application/json", Fixtures.Achievements);

        var client = new SteamApiClient(mock.ToHttpClient(), FakeKey);
        var result = await client.GetAchievementsAsync(Fixtures.AppId);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetAchievements_HttpError_ReturnsNull()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("*").Respond(System.Net.HttpStatusCode.Forbidden);

        var client = new SteamApiClient(mock.ToHttpClient(), FakeKey);
        var result = await client.GetAchievementsAsync(Fixtures.AppId);

        Assert.Null(result);
    }

    // ── GetNews ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetNews_ReturnsItems()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("*GetNewsForApp*")
            .Respond("application/json", Fixtures.News);

        var client = new SteamApiClient(mock.ToHttpClient(), FakeKey);
        var result = await client.GetNewsAsync(Fixtures.AppId);

        Assert.NotNull(result);
        Assert.Single(result);
    }
}
