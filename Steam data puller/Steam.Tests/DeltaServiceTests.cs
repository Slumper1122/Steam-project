using System.Text.Json.Nodes;
using SteamPuller.Models;
using SteamPuller.Services;

namespace Steam.Tests;

public class DeltaServiceTests
{
    private static GameSnapshot MakeSnap(
        int players = 3340, int reviews = 123000,
        int ownLow  = 5_000_000, decimal price = 29.99m, int disc = 0) => new()
    {
        AppId      = 264710,
        Name       = "Subnautica",
        CapturedAt = DateTime.UtcNow,
        Info       = new GameInfo(),
        Players    = new PlayerStats { CurrentPlayers = players },
        Owners     = new OwnerStats  { EstimateLow = ownLow },
        Reviews    = new ReviewStats { TotalReviews = reviews },
        Price      = new PriceInfo   { CurrentUsd = price, DiscountPercent = disc },
        Playtime   = new PlaytimeStats(),
        Updates    = new UpdateStats(),
        Achievements = new AchievementStats(),
        Dlc        = new DlcInfo(),
    };

    private static JsonObject MakeLastRow(
        int players = 3340, int reviews = 123000,
        int ownLow  = 5_000_000, double price = 29.99, int disc = 0)
    {
        var obj = new JsonObject
        {
            ["current_players"] = players,
            ["total_reviews"]   = reviews,
            ["owners_low"]      = ownLow,
            ["price_usd"]       = price,
            ["discount_pct"]    = disc,
        };
        return obj;
    }

    [Fact]
    public void HasChanged_NullLast_ReturnsTrue()
    {
        Assert.True(DeltaService.HasChanged(MakeSnap(), (DeltaKey?)null));
        Assert.True(DeltaService.HasChanged(MakeSnap(), (JsonObject?)null));
    }

    [Fact]
    public void HasChanged_IdenticalData_ReturnsFalse()
    {
        Assert.False(DeltaService.HasChanged(MakeSnap(), MakeLastRow()));
    }

    [Fact]
    public void HasChanged_PlayerCountChanged_ReturnsTrue()
    {
        Assert.True(DeltaService.HasChanged(MakeSnap(players: 4000), MakeLastRow(players: 3340)));
    }

    [Fact]
    public void HasChanged_NewReview_ReturnsTrue()
    {
        Assert.True(DeltaService.HasChanged(MakeSnap(reviews: 123001), MakeLastRow(reviews: 123000)));
    }

    [Fact]
    public void HasChanged_DiscountStarted_ReturnsTrue()
    {
        Assert.True(DeltaService.HasChanged(
            MakeSnap(price: 14.99m, disc: 50),
            MakeLastRow(price: 29.99, disc: 0)));
    }

    [Fact]
    public void HasChanged_OnlyTimestampDiffers_ReturnsFalse()
    {
        var snap = MakeSnap();
        Assert.False(DeltaService.HasChanged(snap, MakeLastRow()));
    }

    [Fact]
    public void HasChanged_SubCentPriceDrift_ReturnsFalse()
    {
        // decimal -> double conversions must not be reported as a price change
        Assert.False(DeltaService.HasChanged(MakeSnap(price: 29.99m), MakeLastRow(price: 29.990001)));
    }

    [Fact]
    public void HasChanged_OwnerEstimateChanged_ReturnsTrue()
    {
        Assert.True(DeltaService.HasChanged(
            MakeSnap(ownLow: 10_000_000), MakeLastRow(ownLow: 5_000_000)));
    }

    [Fact]
    public void FromSupabaseRow_NullRow_ReturnsNull()
    {
        Assert.Null(DeltaService.FromSupabaseRow(null));
    }

    [Fact]
    public void FromSupabaseRow_MapsEveryField()
    {
        var key = DeltaService.FromSupabaseRow(
            MakeLastRow(players: 111, reviews: 222, ownLow: 333, price: 4.44, disc: 55));

        Assert.NotNull(key);
        Assert.Equal(111, key.CurrentPlayers);
        Assert.Equal(222, key.TotalReviews);
        Assert.Equal(333, key.OwnersLow);
        Assert.Equal(4.44, key.PriceUsd);
        Assert.Equal(55,  key.DiscountPct);
    }

    [Fact]
    public void FromSupabaseRow_MissingFields_DefaultToZero()
    {
        var key = DeltaService.FromSupabaseRow(new JsonObject());

        Assert.NotNull(key);
        Assert.Equal(0, key.CurrentPlayers);
        Assert.Equal(0, key.TotalReviews);
    }

    // ── AgeOf ─────────────────────────────────────────────────────────────────
    // Drives the standby collector: a null age means "cannot tell", which the
    // caller must treat as a reason to collect rather than to stand by.

    private static readonly DateTimeOffset Now =
        new(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);

    private static JsonObject RowCapturedAt(string? capturedAt)
    {
        var row = MakeLastRow();
        if (capturedAt is not null) row["captured_at"] = capturedAt;
        return row;
    }

    [Fact]
    public void AgeOf_NullRow_ReturnsNull()
        => Assert.Null(DeltaService.AgeOf(null, Now));

    [Fact]
    public void AgeOf_RowWithoutTimestamp_ReturnsNull()
        => Assert.Null(DeltaService.AgeOf(RowCapturedAt(null), Now));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a date")]
    public void AgeOf_UnparsableTimestamp_ReturnsNull(string raw)
        => Assert.Null(DeltaService.AgeOf(RowCapturedAt(raw), Now));

    [Fact]
    public void AgeOf_NonStringTimestamp_ReturnsNull()
    {
        var row = MakeLastRow();
        row["captured_at"] = 1759255200;

        Assert.Null(DeltaService.AgeOf(row, Now));
    }

    [Theory]
    // Supabase returns timestamptz; these are the shapes it actually emits.
    [InlineData("2026-09-30T17:30:00+00:00")]
    [InlineData("2026-09-30T17:30:00Z")]
    [InlineData("2026-09-30T19:30:00+02:00")]
    public void AgeOf_ParsesTimestampFormats(string raw)
        => Assert.Equal(TimeSpan.FromMinutes(30), DeltaService.AgeOf(RowCapturedAt(raw), Now));

    [Fact]
    public void AgeOf_KeepsSubSecondPrecision()
    {
        // Postgres stores microseconds; truncating them would be harmless here
        // but silently rounding the age is not something to rely on untested.
        var age = DeltaService.AgeOf(RowCapturedAt("2026-09-30T17:30:00.123456+00:00"), Now);

        // 0.123456 s = 1,234,560 ticks.
        Assert.Equal(TimeSpan.FromMinutes(30) - TimeSpan.FromTicks(1_234_560), age);
    }

    [Fact]
    public void AgeOf_TimestampWithoutZone_IsTreatedAsUtc()
    {
        // A naive string must not be read as local time, or a collector in a
        // non-UTC zone would compute an age that is hours off.
        Assert.Equal(TimeSpan.FromMinutes(45),
            DeltaService.AgeOf(RowCapturedAt("2026-09-30T17:15:00"), Now));
    }

    [Fact]
    public void AgeOf_FutureTimestamp_ClampsToZero()
    {
        // Clock skew between this machine and the database must not produce a
        // negative age that reads as "very stale".
        Assert.Equal(TimeSpan.Zero,
            DeltaService.AgeOf(RowCapturedAt("2026-09-30T18:05:00Z"), Now));
    }

    [Fact]
    public void AgeOf_OldTimestamp_ReturnsFullAge()
        => Assert.Equal(TimeSpan.FromHours(6),
            DeltaService.AgeOf(RowCapturedAt("2026-09-30T12:00:00Z"), Now));
}
