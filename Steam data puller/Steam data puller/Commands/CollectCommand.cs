using System.Text.Json;
using SteamPuller.Clients;
using SteamPuller.Services;

namespace SteamPuller.Commands;

/// <summary>
/// Reads watchlist.json, pulls data for every game,
/// checks delta, and persists to Supabase if something changed.
/// Designed to run unattended (GitHub Actions cron).
/// </summary>
public static class CollectCommand
{
    public static async Task<int> RunAsync(
        string  watchlistPath,
        string? apiKey,
        string? supabaseUrl,
        string? supabaseKey,
        string  outputDir,
        string  dbPath,
        CancellationToken ct = default)
    {
        // ── Validate config ───────────────────────────────────────────────────
        var steamKey = ResolveEnv(apiKey, "STEAM_API_KEY");
        if (steamKey is null)
        {
            Err("[CONFIG] STEAM_API_KEY is missing. Set --key or the environment variable.");
            return 1;
        }

        var sbUrl = ResolveEnv(supabaseUrl, "SUPABASE_URL");
        var sbKey = ResolveEnv(supabaseKey, "SUPABASE_KEY");
        bool useSupabase = sbUrl is not null && sbKey is not null;
        if (!useSupabase)
            Warn("[CONFIG] SUPABASE_URL / SUPABASE_KEY not set — skipping cloud upload, saving locally only.");

        // ── Load watchlist ────────────────────────────────────────────────────
        if (!File.Exists(watchlistPath))
        {
            Err($"[CONFIG] watchlist file not found: {watchlistPath}");
            return 1;
        }

        int[] appIds;
        try
        {
            var doc = JsonDocument.Parse(File.ReadAllText(watchlistPath));
            appIds = [.. doc.RootElement.GetProperty("games").EnumerateArray()
                       .Select(e => e.GetInt32())];
        }
        catch (Exception ex)
        {
            Err($"[CONFIG] Failed to parse watchlist: {ex.Message}");
            return 1;
        }

        Info($"[COLLECT] Watchlist: {appIds.Length} game(s) — {string.Join(", ", appIds)}");

        // ── Setup services ────────────────────────────────────────────────────
        using var http    = BuildHttpClient();
        var steam         = new SteamApiClient(http, steamKey);
        var spy           = new SteamSpyClient(http);
        var builder       = new SnapshotBuilder(steam, spy);
        var db            = new DatabaseService(dbPath);
        db.EnsureSchema();

        SupabaseClient? supabase = useSupabase
            ? new SupabaseClient(http, sbUrl!, sbKey!)
            : null;

        int saved = 0, skipped = 0, errors = 0;

        foreach (var appId in appIds)
        {
            Info($"\n[GAME] AppID {appId}");
            try
            {
                var snap = await builder.BuildAsync(appId, ct);

                // ── Delta check ───────────────────────────────────────────────
                System.Text.Json.Nodes.JsonObject? lastRemote = null;
                if (supabase is not null)
                {
                    try { lastRemote = await supabase.GetLatestSnapshotAsync(appId, ct); }
                    catch (Exception ex) { Warn($"  [DELTA] Could not fetch last remote snapshot: {ex.Message}"); }
                }

                var lastLocal = db.GetHistory(appId, 1).FirstOrDefault();
                bool changed = DeltaService.HasChanged(snap, lastRemote);

                if (!changed)
                {
                    Info($"  [DELTA] No change detected — skipping storage.");
                    skipped++;
                    continue;
                }

                // ── Save locally ──────────────────────────────────────────────
                var jsonPath = JsonStorage.Save(snap, outputDir);
                Info($"  [STORE] JSON → {jsonPath}");
                db.UpsertGame(snap);
                var snapshotId = db.InsertSnapshot(snap, jsonPath);
                Info($"  [DB]    Local snapshot #{snapshotId}");

                // ── Push to Supabase ──────────────────────────────────────────
                if (supabase is not null)
                {
                    await supabase.UpsertGameAsync(snap, ct);
                    await supabase.InsertSnapshotAsync(snap, ct);
                    Info($"  [CLOUD] Pushed to Supabase ✓");
                }

                saved++;
            }
            catch (Exception ex)
            {
                Err($"  [ERROR] AppID {appId}: {ex.Message}");
                errors++;
            }
        }

        // ── Summary ───────────────────────────────────────────────────────────
        Console.WriteLine();
        Info($"[DONE] saved={saved}  skipped={skipped}  errors={errors}");
        return errors > 0 ? 1 : 0;
    }

    private static string? ResolveEnv(string? cliValue, string envVar)
    {
        if (!string.IsNullOrWhiteSpace(cliValue)) return cliValue;
        var env = Environment.GetEnvironmentVariable(envVar);
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }

    private static HttpClient BuildHttpClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SteamDataPuller/1.0");
        return c;
    }

    private static void Info(string msg)  { Console.ForegroundColor = ConsoleColor.Cyan;  Console.WriteLine(msg); Console.ResetColor(); }
    private static void Warn(string msg)  { Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine(msg); Console.ResetColor(); }
    private static void Err(string msg)   { Console.ForegroundColor = ConsoleColor.Red;    Console.Error.WriteLine(msg); Console.ResetColor(); }
}
