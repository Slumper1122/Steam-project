using System.Text.Json.Nodes;

namespace SteamPuller.Clients;

/// <summary>Wraps Steam Store API, Steam Web API, and Steam Reviews API.</summary>
public sealed class SteamApiClient(HttpClient http, string apiKey)
{
    private const string Store = "https://store.steampowered.com";
    private const string Api   = "https://api.steampowered.com";

    // ── Store details ─────────────────────────────────────────────────────────
    public async Task<JsonObject?> GetAppDetailsAsync(int appId, CancellationToken ct = default)
    {
        var url  = $"{Store}/api/appdetails?appids={appId}&cc=us&l=en";
        var json = await http.GetStringAsync(url, ct);
        var root = JsonNode.Parse(json)?.AsObject();
        var entry = root?[appId.ToString()];
        if (entry?["success"]?.GetValue<bool>() != true)
            return null;
        return entry["data"]?.AsObject();
    }

    // ── Live player count ─────────────────────────────────────────────────────
    public async Task<int> GetCurrentPlayersAsync(int appId, CancellationToken ct = default)
    {
        var url  = $"{Api}/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid={appId}&key={apiKey}";
        var json = await http.GetStringAsync(url, ct);
        return JsonNode.Parse(json)?["response"]?["player_count"]?.GetValue<int>() ?? 0;
    }

    // ── Review summary ────────────────────────────────────────────────────────
    public async Task<JsonObject?> GetReviewSummaryAsync(int appId, CancellationToken ct = default)
    {
        var url = $"{Store}/appreviews/{appId}?json=1&filter=recent&language=all&num_per_page=0";
        try
        {
            var json = await http.GetStringAsync(url, ct);
            var root = JsonNode.Parse(json)?.AsObject();
            return root?["success"]?.GetValue<int>() == 1
                ? root["query_summary"]?.AsObject()
                : null;
        }
        catch { return null; }
    }

    // ── News / update history ─────────────────────────────────────────────────
    public async Task<JsonArray?> GetNewsAsync(int appId, int count = 20, CancellationToken ct = default)
    {
        var url = $"{Api}/ISteamNews/GetNewsForApp/v2/?appid={appId}&count={count}&key={apiKey}&feeds=steam_community_announcements";
        try
        {
            var json = await http.GetStringAsync(url, ct);
            return JsonNode.Parse(json)?["appnews"]?["newsitems"]?.AsArray();
        }
        catch { return null; }
    }

    // ── Achievement unlock percentages ────────────────────────────────────────
    public async Task<JsonArray?> GetAchievementsAsync(int appId, CancellationToken ct = default)
    {
        var url = $"{Api}/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={appId}&key={apiKey}";
        try
        {
            var json = await http.GetStringAsync(url, ct);
            return JsonNode.Parse(json)?["achievementpercentages"]?["achievements"]?.AsArray();
        }
        catch { return null; }
    }
}
