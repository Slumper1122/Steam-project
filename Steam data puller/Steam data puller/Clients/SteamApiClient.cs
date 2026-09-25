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
        if (root is null) return null;

        // Steam documents the envelope as being keyed by the requested AppID, but
        // has been observed answering with an unrelated id while data.steam_appid
        // still holds the request. Falling back to the sole entry keeps us working
        // either way; a response carrying several entries is still matched by key,
        // so we can never pick the wrong game out of a batch.
        var entry = root[appId.ToString()]?.AsObject()
                 ?? (root.Count == 1 ? root.First().Value?.AsObject() : null);

        if (entry?["success"]?.GetValue<bool>() != true)
            return null;

        var data = entry["data"]?.AsObject();

        // Guard against attributing one game's numbers to another.
        var returnedId = data?["steam_appid"]?.GetValue<int>();
        if (returnedId is not null && returnedId != appId)
            return null;

        return data;
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
