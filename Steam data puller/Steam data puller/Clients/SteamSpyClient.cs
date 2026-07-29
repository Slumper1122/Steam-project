using System.Text.Json.Nodes;

namespace SteamPuller.Clients;

/// <summary>
/// SteamSpy public API — no key required.
/// Rate limit: ~4 requests/min. One request per game pull is well within limits.
/// </summary>
public sealed class SteamSpyClient(HttpClient http)
{
    private const string Base = "https://steamspy.com/api.php";

    public async Task<JsonObject?> GetAppAsync(int appId, CancellationToken ct = default)
    {
        var url  = $"{Base}?request=appdetails&appid={appId}";
        var json = await http.GetStringAsync(url, ct);
        return JsonNode.Parse(json)?.AsObject();
    }
}
