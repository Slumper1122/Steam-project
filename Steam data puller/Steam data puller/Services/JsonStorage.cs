using System.Text.Json;
using SteamPuller.Models;

namespace SteamPuller.Services;

/// <summary>Saves and loads GameSnapshot JSON files.</summary>
public static class JsonStorage
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Saves snapshot to  &lt;outputDir&gt;/&lt;appId&gt;/&lt;appId&gt;_&lt;timestamp&gt;.json
    /// Returns the full path of the written file.
    /// </summary>
    public static string Save(GameSnapshot snap, string outputDir)
    {
        var dir  = Path.Combine(outputDir, snap.AppId.ToString());
        Directory.CreateDirectory(dir);

        var ts   = snap.CapturedAt.ToString("yyyy-MM-ddTHH-mm-ss-fffZ");
        var path = Path.Combine(dir, $"{snap.AppId}_{ts}.json");

        var json = JsonSerializer.Serialize(snap, Opts);
        File.WriteAllText(path, json);
        return path;
    }

    public static GameSnapshot? Load(string path)
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<GameSnapshot>(File.ReadAllText(path), Opts);
    }
}
