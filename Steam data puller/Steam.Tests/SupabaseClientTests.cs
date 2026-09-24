using System.Net;
using RichardSzalay.MockHttp;
using SteamPuller.Clients;
using SteamPuller.Models;

namespace Steam.Tests;

public class SupabaseClientTests
{
    private const string Url = "https://testproject.supabase.co";
    private const string Key = "sb_secret_testkey";

    private static GameSnapshot MakeSnap(int appId = 264710) => new()
    {
        AppId      = appId,
        Name       = "Subnautica",
        CapturedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        Info       = new GameInfo { Developer = "Unknown Worlds", Publisher = "Unknown Worlds" },
        Players    = new PlayerStats { CurrentPlayers = 2483, PeakCcu24h = 2719 },
        Owners     = new OwnerStats  { EstimateLow = 5_000_000, EstimateHigh = 10_000_000 },
        Reviews    = new ReviewStats { ScoreDescription = "Overwhelmingly Positive", TotalReviews = 123000 },
        Playtime   = new PlaytimeStats(),
        Price      = new PriceInfo   { CurrentUsd = 29.99m },
        Updates    = new UpdateStats(),
        Achievements = new AchievementStats(),
        Dlc        = new DlcInfo(),
    };

    // ── GetLatestSnapshotAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestSnapshot_RowExists_ReturnsObject()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"{Url}/rest/v1/snapshots*")
            .Respond("application/json", """[{"current_players":2483,"total_reviews":123000}]""");

        var client = new SupabaseClient(mock.ToHttpClient(), Url, Key);
        var row    = await client.GetLatestSnapshotAsync(264710);

        Assert.NotNull(row);
        Assert.Equal(2483, row["current_players"]?.GetValue<int>());
    }

    [Fact]
    public async Task GetLatestSnapshot_EmptyResult_ReturnsNull()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"{Url}/rest/v1/snapshots*").Respond("application/json", "[]");

        var client = new SupabaseClient(mock.ToHttpClient(), Url, Key);

        Assert.Null(await client.GetLatestSnapshotAsync(264710));
    }

    [Fact]
    public async Task GetLatestSnapshot_HttpError_Throws()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"{Url}/rest/v1/snapshots*").Respond(HttpStatusCode.NotFound);

        var client = new SupabaseClient(mock.ToHttpClient(), Url, Key);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetLatestSnapshotAsync(264710));
    }

    [Fact]
    public async Task GetLatestSnapshot_SendsAuthHeaders()
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect($"{Url}/rest/v1/snapshots*")
            .WithHeaders("apikey", Key)
            .WithHeaders("Authorization", $"Bearer {Key}")
            .Respond("application/json", "[]");

        var client = new SupabaseClient(mock.ToHttpClient(), Url, Key);
        await client.GetLatestSnapshotAsync(264710);

        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetLatestSnapshot_QueriesNewestRowForTheGame()
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect($"{Url}/rest/v1/snapshots")
            .WithQueryString("app_id", "eq.264710")
            .WithQueryString("order", "captured_at.desc")
            .WithQueryString("limit", "1")
            .Respond("application/json", "[]");

        var client = new SupabaseClient(mock.ToHttpClient(), Url, Key);
        await client.GetLatestSnapshotAsync(264710);

        mock.VerifyNoOutstandingExpectation();
    }

    // ── UpsertGameAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertGame_SendsMergeDuplicatesHeader()
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect(HttpMethod.Post, $"{Url}/rest/v1/games")
            .WithHeaders("Prefer", "resolution=merge-duplicates")
            .Respond(HttpStatusCode.Created);

        var client = new SupabaseClient(mock.ToHttpClient(), Url, Key);
        await client.UpsertGameAsync(MakeSnap());

        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task UpsertGame_ServerRejects_ThrowsWithStatusAndBody()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{Url}/rest/v1/games")
            .Respond(HttpStatusCode.Unauthorized, "text/plain", "invalid api key");

        var client = new SupabaseClient(mock.ToHttpClient(), Url, Key);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.UpsertGameAsync(MakeSnap()));

        Assert.Contains("401", ex.Message);
        Assert.Contains("invalid api key", ex.Message);
    }

    // ── InsertSnapshotAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task InsertSnapshot_PostsSnapshotFields()
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect(HttpMethod.Post, $"{Url}/rest/v1/snapshots")
            .WithPartialContent("\"current_players\":2483")
            .WithPartialContent("\"app_id\":264710")
            .Respond(HttpStatusCode.Created);

        var client = new SupabaseClient(mock.ToHttpClient(), Url, Key);
        await client.InsertSnapshotAsync(MakeSnap());

        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task InsertSnapshot_ServerError_Throws()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{Url}/rest/v1/snapshots")
            .Respond(HttpStatusCode.InternalServerError, "text/plain", "boom");

        var client = new SupabaseClient(mock.ToHttpClient(), Url, Key);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.InsertSnapshotAsync(MakeSnap()));
    }
}
