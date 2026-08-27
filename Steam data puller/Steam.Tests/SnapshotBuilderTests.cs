using RichardSzalay.MockHttp;
using SteamPuller.Clients;
using SteamPuller.Services;

namespace Steam.Tests;

public class SnapshotBuilderTests
{
    private static (SteamApiClient steam, SteamSpyClient spy) BuildMockedClients(
        string? appDetails   = null,
        string? players      = null,
        string? reviews      = null,
        string? news         = null,
        string? achievements = null,
        string? steamSpy     = null)
    {
        var mock = new MockHttpMessageHandler();

        mock.When("https://store.steampowered.com/api/appdetails*")
            .Respond("application/json", appDetails ?? Fixtures.AppDetails());
        mock.When("https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/*")
            .Respond("application/json", players ?? Fixtures.CurrentPlayers());
        mock.When("https://store.steampowered.com/appreviews/*")
            .Respond("application/json", reviews ?? Fixtures.ReviewSummary());
        mock.When("https://api.steampowered.com/ISteamNews/GetNewsForApp/*")
            .Respond("application/json", news ?? Fixtures.News);
        mock.When("https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/*")
            .Respond("application/json", achievements ?? Fixtures.Achievements);
        mock.When("https://steamspy.com/api.php*")
            .Respond("application/json", steamSpy ?? Fixtures.SteamSpy);

        var http  = mock.ToHttpClient();
        return (new SteamApiClient(http, "KEY"), new SteamSpyClient(http));
    }

    [Fact]
    public async Task BuildAsync_FullResponse_PopulatesAllFields()
    {
        var (steam, spy) = BuildMockedClients();
        var builder = new SnapshotBuilder(steam, spy);

        var snap = await builder.BuildAsync(Fixtures.AppId);

        Assert.Equal(Fixtures.AppId, snap.AppId);
        Assert.Equal("Subnautica", snap.Name);
        Assert.Equal(3340, snap.Players.CurrentPlayers);
        Assert.Equal(2719, snap.Players.PeakCcu24h);
        Assert.Equal("Overwhelmingly Positive", snap.Reviews.ScoreDescription);
        Assert.Equal(1, snap.Updates.FetchedCount);
        Assert.Equal(2, snap.Achievements.TotalCount);
        Assert.Equal(1, snap.Dlc.Count);
        Assert.Equal(5_000_000, snap.Owners.EstimateLow);
        Assert.Equal(10_000_000, snap.Owners.EstimateHigh);
    }

    [Fact]
    public async Task BuildAsync_AppDetailsReturnsNull_Throws()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("*api/appdetails*")
            .Respond("application/json", $"{{\"99999\":{{\"success\":false}}}}");
        mock.When("*").Respond("application/json", "{}");

        var http    = mock.ToHttpClient();
        var steam   = new SteamApiClient(http, "KEY");
        var spy     = new SteamSpyClient(http);
        var builder = new SnapshotBuilder(steam, spy);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => builder.BuildAsync(99999));
    }

    [Fact]
    public async Task BuildAsync_PriceCalculatedCorrectly()
    {
        var (steam, spy) = BuildMockedClients();
        var snap = await new SnapshotBuilder(steam, spy).BuildAsync(Fixtures.AppId);

        Assert.Equal(29.99m, snap.Price.CurrentUsd);
        Assert.Equal(0, snap.Price.DiscountPercent);
        Assert.False(snap.Price.IsFree);
    }

    [Fact]
    public async Task BuildAsync_ReviewPercentCalculatedCorrectly()
    {
        var (steam, spy) = BuildMockedClients(
            reviews: Fixtures.ReviewSummary(positive: 9000, negative: 1000));
        var snap = await new SnapshotBuilder(steam, spy).BuildAsync(Fixtures.AppId);

        Assert.Equal(90.0, snap.Reviews.PositivePercent);
    }

    [Fact]
    public async Task BuildAsync_SteamSpyFails_SnapshotStillBuilt()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("https://store.steampowered.com/api/appdetails*").Respond("application/json", Fixtures.AppDetails());
        mock.When("https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/*").Respond("application/json", Fixtures.CurrentPlayers());
        mock.When("https://store.steampowered.com/appreviews/*").Respond("application/json", Fixtures.ReviewSummary());
        mock.When("https://api.steampowered.com/ISteamNews/GetNewsForApp/*").Respond("application/json", Fixtures.News);
        mock.When("https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/*").Respond("application/json", Fixtures.Achievements);
        mock.When("https://steamspy.com/api.php*").Respond(System.Net.HttpStatusCode.ServiceUnavailable);

        var http    = mock.ToHttpClient();
        var builder = new SnapshotBuilder(new SteamApiClient(http, "KEY"), new SteamSpyClient(http));
        var snap    = await builder.BuildAsync(Fixtures.AppId);

        Assert.Equal("Subnautica", snap.Name);
        Assert.Equal(0, snap.Owners.EstimateLow);
    }
}
