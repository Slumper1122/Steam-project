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
