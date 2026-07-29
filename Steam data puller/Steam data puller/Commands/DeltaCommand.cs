using SteamPuller.Models;
using SteamPuller.Services;

namespace SteamPuller.Commands;

/// <summary>Compares the two most recent snapshots and prints what changed.</summary>
public static class DeltaCommand
{
    public static int Run(int appId, string dbPath)
    {
        var db = new DatabaseService(dbPath);
        db.EnsureSchema();

        var (prev, curr) = db.GetLastTwo(appId);

        if (curr is null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"No snapshots found for AppID {appId}. Run 'pull {appId}' first.");
            Console.ResetColor();
            return 1;
        }

        if (prev is null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Only one snapshot exists. Pull again later to see a delta.");
            Console.ResetColor();
            return 0;
        }

        var prevTime = TryParseDate(prev.CapturedAt);
        var currTime = TryParseDate(curr.CapturedAt);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"  Delta for AppID {appId}");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Previous : {prevTime}");
        Console.WriteLine($"  Current  : {currTime}");
        Console.ResetColor();
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {"Metric",-26} {"Previous",16} {"Current",16}  {"Change",-20}");
        Console.WriteLine(new string('─', 84));
        Console.ResetColor();

        PrintDeltaInt   ("Current players",   prev.CurrentPlayers,        curr.CurrentPlayers);
        PrintDeltaInt   ("Owners (low est.)",  prev.OwnersLow,             curr.OwnersLow, fmtN: true);
        PrintDeltaDouble("Review score",       prev.PositivePct,           curr.PositivePct,   suffix: "%");
        PrintDeltaDouble("Price (USD)",        prev.PriceUsd,              curr.PriceUsd,      prefix: "$");
        PrintDeltaInt   ("Discount %",         prev.DiscountPct,           curr.DiscountPct,   suffix: "%");
        PrintDeltaInt   ("Avg playtime (min)", prev.AvgPlaytimeForeverMin, curr.AvgPlaytimeForeverMin);
        PrintDeltaInt   ("Updates fetched",    prev.UpdateCount,           curr.UpdateCount);
        PrintDeltaInt   ("DLC count",          prev.DlcCount,              curr.DlcCount);

        if (prev.ReviewScoreDesc != curr.ReviewScoreDesc)
        {
            Console.Write($"  {"Review label",-26} {prev.ReviewScoreDesc,16} {curr.ReviewScoreDesc,16}  ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("CHANGED");
            Console.ResetColor();
        }

        Console.WriteLine();
        return 0;
    }

    private static void PrintDeltaInt(string label, int prev, int curr,
        bool fmtN = false, string suffix = "")
    {
        var prevStr = fmtN ? $"{prev:N0}{suffix}" : $"{prev}{suffix}";
        var currStr = fmtN ? $"{curr:N0}{suffix}" : $"{curr}{suffix}";
        var diff    = curr - prev;

        Console.Write($"  {label,-26} {prevStr,16} {currStr,16}  ");
        WriteArrow(diff);
        Console.WriteLine();
    }

    private static void PrintDeltaDouble(string label, double prev, double curr,
        string prefix = "", string suffix = "")
    {
        var prevStr = $"{prefix}{prev:F2}{suffix}";
        var currStr = $"{prefix}{curr:F2}{suffix}";
        var diff    = curr - prev;

        Console.Write($"  {label,-26} {prevStr,16} {currStr,16}  ");
        WriteArrow(diff);
        Console.WriteLine();
    }

    private static void WriteArrow(double diff)
    {
        if (diff > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"↑ +{diff:F0}");
        }
        else if (diff < 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"↓ {diff:F0}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("→ no change");
        }
        Console.ResetColor();
    }

    private static string TryParseDate(string raw)
    {
        return DateTime.TryParse(raw, out var dt)
            ? dt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss UTC")
            : raw;
    }
}
