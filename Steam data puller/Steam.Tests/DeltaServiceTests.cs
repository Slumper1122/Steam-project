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
}
