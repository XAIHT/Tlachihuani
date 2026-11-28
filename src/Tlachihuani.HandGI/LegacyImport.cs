// Tlachihuani — Angela López Mendoza · XAIHT
using System.Text.Json;
using System.Text.Json.Nodes;
using HandGI.Core.Util;
using Tlachihuani.Contracts;

namespace HandGI.App;

// The standalone HandGI kept its choices and the signs its user taught in %LOCALAPPDATA%\HandGI.
// The first time HandGI runs inside Tlachihuani it copies them into its own folder, so nobody has
// to teach a sign twice. The old folder is never changed or deleted.
internal static class LegacyImport
{
    private const string Marker = "imported-from-standalone-handgi.txt";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    internal static void Run()
    {
        string from = AppPaths.LegacyDataRoot, to = AppPaths.DataRoot;
        try
        {
            if (!Directory.Exists(from) || File.Exists(Path.Combine(to, "settings.json")) || File.Exists(Path.Combine(to, Marker))) return;
            Directory.CreateDirectory(to);
            int profiles = 0;
            string oldProfiles = Path.Combine(from, "profiles");
            if (Directory.Exists(oldProfiles))
            {
                string newProfiles = Path.Combine(to, "profiles");
                Directory.CreateDirectory(newProfiles);
                foreach (var file in Directory.GetFiles(oldProfiles, "*.handgi.json"))
                {
                    string target = Path.Combine(newProfiles, Path.GetFileName(file));
                    if (File.Exists(target)) continue;
                    File.Copy(file, target);
                    profiles++;
                }
            }
            string oldSettings = Path.Combine(from, "settings.json");
            if (File.Exists(oldSettings) && JsonNode.Parse(File.ReadAllText(oldSettings)) is JsonObject settings)
            {
                // The last profile pointed into the old folder; point it at the copy.
                if ((string?)settings["LastProfile"] is { Length: > 0 } last && last.StartsWith(from + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    settings["LastProfile"] = Path.Combine(to, Path.GetRelativePath(from, last));
                File.WriteAllText(Path.Combine(to, "settings.json"), settings.ToJsonString(Indented));
            }
            File.WriteAllText(Path.Combine(to, Marker),
                $"HandGI copied the settings and signs of the standalone HandGI from {from} on {DateTime.Now:yyyy-MM-dd HH:mm}.\r\nThat folder was left unchanged.\r\n");
            TraceLog.I($"HandGI copied the standalone HandGI's settings and {profiles} sign profile(s) from {from}; that folder was left unchanged");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            TraceLog.W($"HandGI could not copy the standalone HandGI's settings from {from}: {ex.Message}");
        }
    }
}
