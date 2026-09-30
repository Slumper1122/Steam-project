using System.Globalization;
using System.Text.Json.Nodes;
using SteamPuller.Models;

namespace SteamPuller.Services;

/// <summary>
/// The subset of metrics that decides whether a snapshot is worth storing.
/// Produced either from a Supabase row or from the local SQLite database.
/// </summary>
public sealed record DeltaKey(
    int    CurrentPlayers,
    int    TotalReviews,
    int    OwnersLow,
    double PriceUsd,
    int    DiscountPct);

/// <summary>
/// Decides whether a new snapshot is worth storing by comparing it
/// to the previous one. Avoids writing identical rows every hour.
/// </summary>
public static class DeltaService
{
    /// <summary>Half a cent — anything below this is the same price.</summary>
    private const double PriceEpsilon = 0.005;

    /// <summary>
    /// Returns true if the snapshot differs meaningfully from the last stored row.
    /// Pass null for <paramref name="last"/> to always accept (first ever snapshot).
    /// </summary>
    public static bool HasChanged(GameSnapshot current, DeltaKey? last)
    {
        if (last is null) return true;

        return current.Players.CurrentPlayers != last.CurrentPlayers
            || current.Reviews.TotalReviews   != last.TotalReviews
            || current.Owners.EstimateLow     != last.OwnersLow
            || current.Price.DiscountPercent  != last.DiscountPct
            || Math.Abs((double)current.Price.CurrentUsd - last.PriceUsd) > PriceEpsilon;
    }

    /// <summary>Overload kept for callers that hold a raw Supabase response row.</summary>
    public static bool HasChanged(GameSnapshot current, JsonObject? last)
        => HasChanged(current, FromSupabaseRow(last));

    /// <summary>Maps a Supabase REST response row to a <see cref="DeltaKey"/>.</summary>
    public static DeltaKey? FromSupabaseRow(JsonObject? row)
    {
        if (row is null) return null;

        return new DeltaKey(
            CurrentPlayers: row["current_players"]?.GetValue<int>()    ?? 0,
            TotalReviews:   row["total_reviews"]?.GetValue<int>()      ?? 0,
            OwnersLow:      row["owners_low"]?.GetValue<int>()         ?? 0,
            PriceUsd:       row["price_usd"]?.GetValue<double>()       ?? 0,
            DiscountPct:    row["discount_pct"]?.GetValue<int>()       ?? 0);
    }

    /// <summary>
    /// How long ago a Supabase row was captured, or null when the row is missing
    /// or carries no parsable timestamp. A standby collector uses this to tell
    /// whether the primary already covered the current window; returning null
    /// means "cannot tell", which callers treat as a reason to collect.
    /// </summary>
    public static TimeSpan? AgeOf(JsonObject? row, DateTimeOffset now)
    {
        if (row?["captured_at"] is not JsonValue value ||
            !value.TryGetValue<string>(out var raw)     ||
            string.IsNullOrWhiteSpace(raw))
            return null;

        if (!DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var capturedAt))
            return null;

        // A clock running behind the database would otherwise report a negative age.
        var age = now - capturedAt;
        return age < TimeSpan.Zero ? TimeSpan.Zero : age;
    }
}
