using System.CommandLine;
using SteamPuller.Commands;

// ── Shared options ────────────────────────────────────────────────────────────
var keyOption = new Option<string?>(
    ["--key", "-k"],
    "Steam Web API key. Falls back to STEAM_API_KEY environment variable.");

var outputOption = new Option<string>(
    ["--output", "-o"],
    () => "data",
    "Directory where JSON snapshot files are stored. Default: ./data");

var dbOption = new Option<string>(
    "--db",
    () => "steam_data.db",
    "Path to the SQLite database file. Default: ./steam_data.db");

// ── Root command ──────────────────────────────────────────────────────────────
var root = new RootCommand("Steam Data Puller — collect game metrics from Steam APIs")
{
    TreatUnmatchedTokensAsErrors = true,
};
root.AddGlobalOption(keyOption);
root.AddGlobalOption(outputOption);
root.AddGlobalOption(dbOption);

// ── pull <appid> ──────────────────────────────────────────────────────────────
var pullCmd   = new Command("pull", "Fetch a fresh snapshot for a game and save to JSON + DB");
var appIdArg  = new Argument<int>("appid", "Steam App ID  (e.g. 264710 for Subnautica)");
pullCmd.AddArgument(appIdArg);
pullCmd.SetHandler(async (context) =>
{
    var appId  = context.ParseResult.GetValueForArgument(appIdArg);
    var key    = context.ParseResult.GetValueForOption(keyOption);
    var output = context.ParseResult.GetValueForOption(outputOption)!;
    var db     = context.ParseResult.GetValueForOption(dbOption)!;
    context.ExitCode = await PullCommand.RunAsync(appId, key, output, db,
        context.GetCancellationToken());
});

// ── history <appid> ───────────────────────────────────────────────────────────
var historyCmd   = new Command("history", "Show stored snapshot history for a game");
var histAppIdArg = new Argument<int>("appid", "Steam App ID");
var limitOption  = new Option<int>("--limit", () => 10, "Maximum rows to display");
historyCmd.AddArgument(histAppIdArg);
historyCmd.AddOption(limitOption);
historyCmd.SetHandler((context) =>
{
    var appId = context.ParseResult.GetValueForArgument(histAppIdArg);
    var limit = context.ParseResult.GetValueForOption(limitOption);
    var db    = context.ParseResult.GetValueForOption(dbOption)!;
    context.ExitCode = HistoryCommand.Run(appId, limit, db);
});

// ── delta <appid> ─────────────────────────────────────────────────────────────
var deltaCmd   = new Command("delta", "Show what changed between the last two snapshots");
var deltaAppId = new Argument<int>("appid", "Steam App ID");
deltaCmd.AddArgument(deltaAppId);
deltaCmd.SetHandler((context) =>
{
    var appId = context.ParseResult.GetValueForArgument(deltaAppId);
    var db    = context.ParseResult.GetValueForOption(dbOption)!;
    context.ExitCode = DeltaCommand.Run(appId, db);
});

root.AddCommand(pullCmd);
root.AddCommand(historyCmd);
root.AddCommand(deltaCmd);

return await root.InvokeAsync(args);
