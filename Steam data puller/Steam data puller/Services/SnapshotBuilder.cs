using System.Text.Json.Nodes;
using SteamPuller.Clients;
using SteamPuller.Models;

namespace SteamPuller.Services;

/// <summary>Orchestrates all API calls and assembles a GameSnapshot.</summary>
public sealed class SnapshotBuilder(SteamApiClient steam, SteamSpyClient spy)
{
    public async Task<GameSnapshot> BuildAsync(int appId, CancellationToken ct = default)
    {
        // ── Steam Store details ───────────────────────────────────────────────
        Console.Write("  [FETCH] Steam Store details ...");
        var details = await steam.GetAppDetailsAsync(appId, ct);
        if (details == null)
            throw new InvalidOperationException(
                $"Steam Store API returned no data for AppID {appId}.\n" +
                $"  Hint: verify the App ID at https://store.steampowered.com/app/{appId}");
        var name = details["name"]?.GetValue<string>() ?? $"AppID {appId}";
        Ok($" ✓  {name}");

        // ── Live player count ─────────────────────────────────────────────────
        Console.Write("  [FETCH] Current player count  ...");
        var players = await steam.GetCurrentPlayersAsync(appId, ct);
        Ok($" ✓  {players:N0} players online");

        // ── Review summary ────────────────────────────────────────────────────
        Console.Write("  [FETCH] Review summary         ...");
        var reviewSummary = await steam.GetReviewSummaryAsync(appId, ct);
        Ok(reviewSummary != null ? " ✓" : " ⚠  unavailable");

        // ── News ──────────────────────────────────────────────────────────────
        Console.Write("  [FETCH] Recent updates (news)  ...");
        var newsItems = await steam.GetNewsAsync(appId, 20, ct);
        Ok($" ✓  {newsItems?.Count ?? 0} items");

        // ── Achievements ──────────────────────────────────────────────────────
        Console.Write("  [FETCH] Achievement rates      ...");
        var achArray = await steam.GetAchievementsAsync(appId, ct);
        Ok($" ✓  {achArray?.Count ?? 0} achievements");

        // ── SteamSpy ──────────────────────────────────────────────────────────
        Console.Write("  [FETCH] SteamSpy data          ...");
        JsonObject? spyData = null;
        try
        {
            spyData = await spy.GetAppAsync(appId, ct);
            Ok(" ✓");
        }
        catch (Exception ex)
        {
            Warn($" ⚠  {ex.Message} (owner/playtime data will be missing)");
        }

        return Assemble(appId, name, details, players, reviewSummary, newsItems, achArray, spyData);
    }

    // ── Assembly ──────────────────────────────────────────────────────────────

    private static GameSnapshot Assemble(
        int appId, string name,
        JsonObject details,
        int currentPlayers,
        JsonObject? reviewSummary,
        JsonArray?  newsItems,
        JsonArray?  achArray,
        JsonObject? spyData)
    {
        var now = DateTime.UtcNow;

        // GameInfo
        var devs  = details["developers"]?.AsArray();
        var pubs  = details["publishers"]?.AsArray();
        var relDate = details["release_date"]?["date"]?.GetValue<string>() ?? "";

        var genres = details["genres"]?.AsArray()
            ?.Select(g => g?["description"]?.GetValue<string>() ?? "")
            .Where(s => s.Length > 0)
            .ToList() ?? [];

        var cats = details["categories"]?.AsArray();
        var isEa = cats?.Any(c =>
            c?["description"]?.GetValue<string>()
             ?.Contains("Early Access", StringComparison.OrdinalIgnoreCase) == true) ?? false;

        var tags = new List<string>();
        if (spyData?["tags"] is JsonObject tagsObj)
            tags = [.. tagsObj.Select(kvp => kvp.Key).Take(10)];

        // Owners
        var ownersRaw           = spyData?["owners"]?.GetValue<string>() ?? "";
        var (ownerLow, ownerHigh) = ParseOwners(ownersRaw);

        // Reviews
        var totalPos  = reviewSummary?["total_positive"]?.GetValue<int>() ?? 0;
        var totalNeg  = reviewSummary?["total_negative"]?.GetValue<int>() ?? 0;
        var totalRev  = reviewSummary?["total_reviews"]?.GetValue<int>() ?? 0;
        var scoreDesc = reviewSummary?["review_score_desc"]?.GetValue<string>() ?? "";
        var posPct    = totalRev > 0 ? Math.Round((double)totalPos / totalRev * 100, 1) : 0.0;

        // Playtime
        var avgForever = ParseInt(spyData?["average_forever"]);
        var medForever = ParseInt(spyData?["median_forever"]);
        var avg2w      = ParseInt(spyData?["average_2weeks"]);
        var med2w      = ParseInt(spyData?["median_2weeks"]);

        // Price
        var isFree      = details["is_free"]?.GetValue<bool>() ?? false;
        var priceNode   = details["price_overview"]?.AsObject();
        var curCents    = ParseInt(priceNode?["final"]);
        var origCents   = ParseInt(priceNode?["initial"]);
        var discountPct = ParseInt(priceNode?["discount_percent"]);

        // CCU 24h peak
        var peakCcu = ParseInt(spyData?["ccu"]);

        // Updates
        var updates = new List<UpdateEntry>();
        if (newsItems != null)
        {
            foreach (var item in newsItems.Where(i => i != null))
            {
                var ts = item!["date"]?.GetValue<long>() ?? 0;
                updates.Add(new UpdateEntry
                {
                    Title         = item["title"]?.GetValue<string>() ?? "",
                    Url           = item["url"]?.GetValue<string>() ?? "",
                    UnixTimestamp = ts,
                    PublishedAt   = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime,
                    Author        = item["author"]?.GetValue<string>() ?? "",
                    FeedLabel     = item["feedlabel"]?.GetValue<string>() ?? "",
                });
            }
        }

        // Achievements
        var achList = achArray?
            .Where(a => a != null)
            .Select(a => new AchievementEntry
            {
                Name          = a!["name"]?.GetValue<string>() ?? "",
                UnlockPercent = ParseDouble(a["percent"]),
            })
            .OrderByDescending(a => a.UnlockPercent)
            .Take(10)
            .ToList() ?? [];

        // DLC
        var dlcIds = details["dlc"]?.AsArray()
            ?.Select(d => d?.GetValue<int>() ?? 0)
            .Where(d => d > 0)
            .ToList() ?? [];

        return new GameSnapshot
        {
            AppId      = appId,
            Name       = name,
            CapturedAt = now,
            Info = new GameInfo
            {
                Developer        = devs?[0]?.GetValue<string>() ?? "",
                Publisher        = pubs?[0]?.GetValue<string>() ?? "",
                ReleaseDate      = relDate,
                IsEarlyAccess    = isEa,
                ShortDescription = details["short_description"]?.GetValue<string>() ?? "",
                Genres           = genres,
                Tags             = tags,
            },
            Players = new PlayerStats
            {
                CurrentPlayers = currentPlayers,
                PeakCcu24h     = peakCcu,
            },
            Owners = new OwnerStats
            {
                EstimateLow  = ownerLow,
                EstimateHigh = ownerHigh,
                RawRange     = ownersRaw,
            },
            Reviews = new ReviewStats
            {
                ScoreDescription = scoreDesc,
                TotalPositive    = totalPos,
                TotalNegative    = totalNeg,
                TotalReviews     = totalRev,
                PositivePercent  = posPct,
            },
            Playtime = new PlaytimeStats
            {
                AverageForeverMinutes  = avgForever,
                MedianForeverMinutes   = medForever,
                AverageTwoWeeksMinutes = avg2w,
                MedianTwoWeeksMinutes  = med2w,
            },
            Price = new PriceInfo
            {
                IsFree          = isFree,
                CurrentUsd      = isFree ? 0 : curCents / 100m,
                OriginalUsd     = isFree ? 0 : origCents / 100m,
                DiscountPercent = discountPct,
            },
            Updates = new UpdateStats
            {
                FetchedCount = updates.Count,
                Items        = updates,
            },
            Achievements = new AchievementStats
            {
                TotalCount = achArray?.Count ?? 0,
                Top10      = achList,
            },
            Dlc = new DlcInfo
            {
                Count  = dlcIds.Count,
                AppIds = dlcIds,
            },
        };
    }

    private static double ParseDouble(JsonNode? node)
    {
        if (node is null) return 0;
        try { return node.GetValue<double>(); } catch { }
        if (double.TryParse(node.ToString(), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
        return 0;
    }

    private static int ParseInt(JsonNode? node)
    {
        if (node is null) return 0;
        try { return node.GetValue<int>(); } catch { }
        if (int.TryParse(node.ToString(), out var v)) return v;
        return 0;
    }

    private static (int low, int high) ParseOwners(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (0, 0);
        var parts = raw.Replace(",", "").Split("..", StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var lo) &&
            int.TryParse(parts[1], out var hi))
            return (lo, hi);
        return (0, 0);
    }

    private static void Ok(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(msg);
        Console.ResetColor();
    }

    private static void Warn(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(msg);
        Console.ResetColor();
    }
}
