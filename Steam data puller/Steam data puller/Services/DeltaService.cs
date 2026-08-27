using System.Text.Json.Nodes;
using SteamPuller.Models;

namespace SteamPuller.Services;

/// <summary>
/// Decides whether a new snapshot is worth storing by comparing it
/// to the previous one. Avoids writing identical rows every hour.
/// </summary>
public static class DeltaService
{
    /// <summary>
    /// Returns true if the snapshot differs meaningfully from the last stored row.
    /// Pass null for <paramref name="last"/> to always accept (first ever snapshot).
    /// </summary>
    public static bool HasChanged(GameSnapshot current, JsonObject? last)
    {
        if (last is null) return true;

        var prevPlayers  = last["current_players"]?.GetValue<int>() ?? 0;
        var prevReviews  = last["total_reviews"]?.GetValue<int>() ?? 0;
        var prevOwnLow   = last["owners_low"]?.GetValue<int>() ?? 0;
        var prevPrice    = last["price_usd"]?.GetValue<double>() ?? 0;
        var prevDiscount = last["discount_pct"]?.GetValue<int>() ?? 0;

        // Any of these changes → store the snapshot
        return current.Players.CurrentPlayers != prevPlayers
            || current.Reviews.TotalReviews   != prevReviews
            || current.Owners.EstimateLow     != prevOwnLow
            || (double)current.Price.CurrentUsd != prevPrice
            || current.Price.DiscountPercent  != prevDiscount;
    }
}
