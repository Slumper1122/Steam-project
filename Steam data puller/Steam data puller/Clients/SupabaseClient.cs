using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SteamPuller.Models;

namespace SteamPuller.Clients;

/// <summary>
/// Thin REST client for Supabase PostgREST API.
/// No SDK dependency — uses plain HttpClient.
/// </summary>
public sealed class SupabaseClient(HttpClient http, string supabaseUrl, string anonKey)
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Upsert game metadata (idempotent).</summary>
    public async Task UpsertGameAsync(GameSnapshot snap, CancellationToken ct = default)
    {
        var body = new
        {
            app_id          = snap.AppId,
            name            = snap.Name,
            developer       = snap.Info.Developer,
            publisher       = snap.Info.Publisher,
            release_date    = snap.Info.ReleaseDate,
            is_early_access = snap.Info.IsEarlyAccess,
            genres          = string.Join(", ", snap.Info.Genres),
            tags            = string.Join(", ", snap.Info.Tags),
            first_seen_at   = snap.CapturedAt,
        };
        await PostAsync("games", body, upsert: true, ct);
    }

    /// <summary>Insert a new snapshot row.</summary>
    public async Task InsertSnapshotAsync(GameSnapshot snap, CancellationToken ct = default)
    {
        var body = new
        {
            app_id                        = snap.AppId,
            captured_at                   = snap.CapturedAt,
            current_players               = snap.Players.CurrentPlayers,
            peak_ccu_24h                  = snap.Players.PeakCcu24h,
            owners_low                    = snap.Owners.EstimateLow,
            owners_high                   = snap.Owners.EstimateHigh,
            review_score_desc             = snap.Reviews.ScoreDescription,
            total_positive                = snap.Reviews.TotalPositive,
            total_negative                = snap.Reviews.TotalNegative,
            total_reviews                 = snap.Reviews.TotalReviews,
            positive_pct                  = snap.Reviews.PositivePercent,
            avg_playtime_forever_min      = snap.Playtime.AverageForeverMinutes,
            median_playtime_forever_min   = snap.Playtime.MedianForeverMinutes,
            price_usd                     = snap.Price.CurrentUsd,
            discount_pct                  = snap.Price.DiscountPercent,
            update_count                  = snap.Updates.FetchedCount,
            achievement_count             = snap.Achievements.TotalCount,
            dlc_count                     = snap.Dlc.Count,
        };
        await PostAsync("snapshots", body, upsert: false, ct);
    }

    /// <summary>Returns the most recent snapshot for a game, or null.</summary>
    public async Task<JsonObject?> GetLatestSnapshotAsync(int appId, CancellationToken ct = default)
    {
        var url = $"{supabaseUrl}/rest/v1/snapshots" +
                  $"?app_id=eq.{appId}" +
                  $"&order=captured_at.desc" +
                  $"&limit=1" +
                  $"&select=current_players,total_reviews,owners_low,price_usd,discount_pct";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddHeaders(req);

        var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        var arr  = JsonNode.Parse(json)?.AsArray();
        return arr?.Count > 0 ? arr[0]?.AsObject() : null;
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private async Task PostAsync(string table, object body, bool upsert, CancellationToken ct)
    {
        var url  = $"{supabaseUrl}/rest/v1/{table}";
        var json = JsonSerializer.Serialize(body);

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        AddHeaders(req);
        if (upsert)
            req.Headers.Add("Prefer", "resolution=merge-duplicates");
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Supabase POST /{table} failed ({(int)resp.StatusCode}): {err}");
        }
    }

    private void AddHeaders(HttpRequestMessage req)
    {
        req.Headers.Add("apikey", anonKey);
        req.Headers.Add("Authorization", $"Bearer {anonKey}");
    }
}
