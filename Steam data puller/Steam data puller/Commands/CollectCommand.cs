using System.Text.Json;
using SteamPuller.Clients;
using SteamPuller.Services;

namespace SteamPuller.Commands;

/// <summary>
/// Reads watchlist.json, pulls data for every game,
/// checks delta, and persists to Supabase if something changed.
/// Runs once by default; with a positive interval it loops forever,
/// which is how it runs inside a container (no cron or shell needed).
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
        int     intervalSeconds = 0,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null)
    {
        if (intervalSeconds <= 0)
            return await RunOnceAsync(watchlistPath, apiKey, supabaseUrl, supabaseKey, outputDir, dbPath, ct, httpHandler);

        Info($"[SCHEDULE] Looping every {intervalSeconds}s. Send SIGTERM/Ctrl+C to stop.");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(watchlistPath, apiKey, supabaseUrl, supabaseKey, outputDir, dbPath, ct, httpHandler);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed cycle must never kill a long-running container.
                Err($"[CYCLE] Unhandled error: {ex.Message}");
            }

            Info($"\n[SCHEDULE] Next run in {intervalSeconds}s ({DateTime.UtcNow.AddSeconds(intervalSeconds):u}).");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Info("[SCHEDULE] Shutting down cleanly.");
        return 0;
    }

    private static async Task<int> RunOnceAsync(
        string  watchlistPath,
        string? apiKey,
        string? supabaseUrl,
        string? supabaseKey,
        string  outputDir,
        string  dbPath,
        CancellationToken ct,
        HttpMessageHandler? httpHandler)
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
        using var http    = HttpClientFactory.Create(httpHandler);
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
                // Supabase is the source of truth when configured, so a snapshot
                // missing from the cloud is always uploaded. Without it we fall
                // back to the local database so an offline container still skips
                // unchanged rows.
                DeltaKey? last;
                if (supabase is not null)
                {
                    System.Text.Json.Nodes.JsonObject? lastRemote = null;
                    try { lastRemote = await supabase.GetLatestSnapshotAsync(appId, ct); }
                    catch (Exception ex) { Warn($"  [DELTA] Could not fetch last remote snapshot: {ex.Message}"); }
                    last = DeltaService.FromSupabaseRow(lastRemote);
                }
                else
                {
                    last = db.GetLastDeltaKey(appId);
                }

                if (!DeltaService.HasChanged(snap, last))
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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

    private static void Info(string msg)  { Console.ForegroundColor = ConsoleColor.Cyan;  Console.WriteLine(msg); Console.ResetColor(); }
    private static void Warn(string msg)  { Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine(msg); Console.ResetColor(); }
    private static void Err(string msg)   { Console.ForegroundColor = ConsoleColor.Red;    Console.Error.WriteLine(msg); Console.ResetColor(); }
}
