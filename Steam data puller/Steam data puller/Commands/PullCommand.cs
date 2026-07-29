using SteamPuller.Clients;
using SteamPuller.Models;
using SteamPuller.Services;

namespace SteamPuller.Commands;

public static class PullCommand
{
    public static async Task<int> RunAsync(
        int     appId,
        string? apiKey,
        string  outputDir,
        string  dbPath,
        CancellationToken ct = default)
    {
        var key = ResolveKey(apiKey);
        if (key is null)
        {
            Error("Steam API key not found.");
            Error("  Provide it via  --key <your-key>");
            Error("  or set the env  STEAM_API_KEY=<your-key>");
            return 1;
        }

        Header($"Steam Data Puller — AppID {appId}");

        using var http    = BuildHttpClient();
        var steam         = new SteamApiClient(http, key);
        var spy           = new SteamSpyClient(http);
        var builder       = new SnapshotBuilder(steam, spy);
        var db            = new DatabaseService(dbPath);

        db.EnsureSchema();

        GameSnapshot snap;
        try
        {
            snap = await builder.BuildAsync(appId, ct);
        }
        catch (Exception ex)
        {
            Error($"\n[ERROR] {ex.Message}");
            return 1;
        }

        // ── Save JSON ─────────────────────────────────────────────────────────
        Console.Write("  [STORE] Writing JSON file     ...");
        var jsonPath = JsonStorage.Save(snap, outputDir);
        Ok($" ✓  {jsonPath}");

        // ── Persist to DB ─────────────────────────────────────────────────────
        Console.Write("  [DB]    Inserting snapshot    ...");
        db.UpsertGame(snap);
        var snapshotId = db.InsertSnapshot(snap, jsonPath);
        Ok($" ✓  snapshot #{snapshotId}");

        // ── Summary ───────────────────────────────────────────────────────────
        PrintSummary(snap, snapshotId);
        return 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void PrintSummary(GameSnapshot s, long id)
    {
        var sep = new string('━', 52);
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(sep);
        Console.WriteLine($"  {s.Name}  (AppID {s.AppId})  —  snapshot #{id}");
        Console.WriteLine(sep);
        Console.ResetColor();

        Row("Captured at",    s.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
        Row("Developer",      s.Info.Developer);
        Row("Release date",   s.Info.ReleaseDate + (s.Info.IsEarlyAccess ? "  [Early Access]" : ""));
        Console.WriteLine();
        Row("Owners (est.)",  s.Owners.EstimateLow > 0
            ? $"{s.Owners.EstimateLow:N0} – {s.Owners.EstimateHigh:N0}"
            : "N/A");
        Row("Current players", $"{s.Players.CurrentPlayers:N0}");
        Row("Peak CCU (24h)", $"{s.Players.PeakCcu24h:N0}");
        Console.WriteLine();
        Row("Reviews",        s.Reviews.TotalReviews > 0
            ? $"{s.Reviews.PositivePercent:F1}%  positive  ({s.Reviews.ScoreDescription})"
            : "N/A");
        Row("Avg playtime",   FormatMinutes(s.Playtime.AverageForeverMinutes) + "  (all time)");
        Row("Median playtime",FormatMinutes(s.Playtime.MedianForeverMinutes) + "  (all time)");
        Console.WriteLine();
        Row("Price",          s.Price.IsFree
            ? "Free"
            : s.Price.DiscountPercent > 0
                ? $"${s.Price.CurrentUsd:F2}  (was ${s.Price.OriginalUsd:F2}, -{s.Price.DiscountPercent}%)"
                : $"${s.Price.CurrentUsd:F2}");
        Row("Updates fetched", $"{s.Updates.FetchedCount}");
        Row("Achievements",   $"{s.Achievements.TotalCount}");
        Row("DLC count",      $"{s.Dlc.Count}");

        if (s.Achievements.Top10.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  Top achievements by unlock rate:");
            foreach (var a in s.Achievements.Top10.Take(3))
                Console.WriteLine($"    {a.UnlockPercent,5:F1}%  {a.Name}");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(sep);
        Console.ResetColor();
        Console.WriteLine();
    }

    private static string? ResolveKey(string? cliKey)
    {
        if (!string.IsNullOrWhiteSpace(cliKey)) return cliKey;
        var env = Environment.GetEnvironmentVariable("STEAM_API_KEY");
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }

    private static HttpClient BuildHttpClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SteamDataPuller/1.0");
        return client;
    }

    private static string FormatMinutes(int min)
    {
        if (min == 0) return "N/A";
        var h = min / 60;
        var m = min % 60;
        return h > 0 ? $"{h}h {m:D2}m" : $"{m}m";
    }

    private static void Header(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n  {title}");
        Console.ResetColor();
    }

    private static void Ok(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(msg);
        Console.ResetColor();
    }

    private static void Error(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(msg);
        Console.ResetColor();
    }

    private static void Row(string label, string value)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"  {label,-22}");
        Console.ResetColor();
        Console.WriteLine(value);
    }
}
