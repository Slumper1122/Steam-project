using SteamPuller.Models;
using SteamPuller.Services;

namespace SteamPuller.Commands;

public static class HistoryCommand
{
    public static int Run(int appId, int limit, string dbPath)
    {
        var db = new DatabaseService(dbPath);
        db.EnsureSchema();

        if (!db.GameExists(appId))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"No data found for AppID {appId}. Run 'pull {appId}' first.");
            Console.ResetColor();
            return 1;
        }

        var rows = db.GetHistory(appId, limit).ToList();
        if (rows.Count == 0)
        {
            Console.WriteLine($"No snapshots stored for AppID {appId}.");
            return 0;
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"  History for AppID {appId}  (last {rows.Count} snapshots)");
        Console.ResetColor();
        Console.WriteLine();

        // Header row
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(
            $"  {"#",-4} {"Captured at (UTC)",-22} {"Players",8} {"Reviews",9} {"Owners (low)",14} {"Price",8} {"Disc%",6} {"Updates",8} {"DLCs",5}");
        Console.WriteLine(new string('─', 92));
        Console.ResetColor();

        foreach (var (r, i) in rows.Select((r, i) => (r, i + 1)))
        {
            var capturedAt = TryParseDate(r.CapturedAt);
            var priceStr   = r.PriceUsd > 0 ? $"${r.PriceUsd:F2}" : "Free";

            Console.Write($"  {i,-4} {capturedAt,-22} {r.CurrentPlayers,8:N0} ");
            Console.Write($"{r.PositivePct,8:F1}%");
            Console.Write($" {r.OwnersLow,14:N0}");
            Console.Write($" {priceStr,8}");

            if (r.DiscountPct > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($" {r.DiscountPct,5}%");
                Console.ResetColor();
            }
            else
            {
                Console.Write($" {"",6}");
            }

            Console.WriteLine($" {r.UpdateCount,8} {r.DlcCount,5}");
        }

        Console.WriteLine();
        return 0;
    }

    private static string TryParseDate(string raw)
    {
        return DateTime.TryParse(raw, out var dt)
            ? dt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : raw;
    }
}
