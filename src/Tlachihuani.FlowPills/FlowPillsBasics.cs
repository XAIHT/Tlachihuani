// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (paths and settings are read on every tick of every run; their callers write the outcome)
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tlachihuani.Windows;

namespace Tlachihuani.FlowPills;

/// <summary>A refusal with a name: what failed, in words the user can act on.</summary>
public sealed class FlowPillsException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

// Where the Flow Pills service keeps what it runs, under the user's Tlachihuani folder:
//   FlowPills\pools\{run}\{agent}\   the compiled agents, in Tlamatini's own pool layout
//   FlowPills\runs\{run}\            the flow that ran, events.log, transcript.log and run.json
//   FlowPills\inbox\                 flows a second launch asks the open window to load
//   FlowPills\settings.json          optional limits (docs/flow-pills.md)
//   FlowPills\process.json           the open Flow Pills process (UserProcessRecord), for XT
public static class FlowPillsPaths
{
    public static string Root => Path.Combine(LogPaths.UserFolder, "FlowPills");
    public static string Pools => Path.Combine(Root, "pools");
    public static string Runs => Path.Combine(Root, "runs");
    public static string Inbox => Path.Combine(Root, "inbox");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string ProcessRecord => Path.Combine(Root, "process.json");
    public static string CompileWorker => Path.Combine(AppContext.BaseDirectory, "flowpills", "flowpills_compile.py");
}

// The names an open Flow Pills window answers to, in the signed-in user's session. Show brings it
// forward; Stop asks it to stop every flow and close. XT's Stop Services does not ask: it ends the
// process the window published (ForcedStop.UserProcessAsync). A test folder gets its own names.
public static class FlowPillsSignals
{
    public static string Singleton => @"Local\XAIHT.Tlachihuani.FlowPills.v1" + LogPaths.UserScope;
    public static string Show => @"Local\XAIHT.Tlachihuani.FlowPills.Show.v1" + LogPaths.UserScope;
    public static string Stop => @"Local\XAIHT.Tlachihuani.FlowPills.Stop.v1" + LogPaths.UserScope;
    public const string ExecutableName = "Tlachihuani.Tray.exe";

    public static bool RequestShow() => Signal(Show);

    public static bool RequestStop() => Signal(Stop);

    // The open Flow Pills process, if its record still names that same process.
    public static ProcessIdentity? OpenRunning(bool terminate = false)
        => UserProcessRecord.Read(FlowPillsPaths.ProcessRecord)?.OpenVerified(ExecutableName, terminate);

    private static bool Signal(string name)
    {
        if (!EventWaitHandle.TryOpenExisting(name, out var handle)) return false;
        using (handle) handle.Set();
        return true;
    }
}

// What FlowPills\settings.json may set. A value written there is obeyed exactly, even one that
// is bad for the machine; a missing value is decided from the machine at run time (FlowCapacity).
public sealed record FlowPillsSettings(int? MaxRuns, long? ReserveBytes, string Python, string AgentsRoot)
{
    public static FlowPillsSettings Defaults { get; } = new(null, null, "", "");

    public static FlowPillsSettings Load()
    {
        try
        {
            var file = new FileInfo(FlowPillsPaths.SettingsFile);
            if (!file.Exists || file.Length > 64 * 1024) return Defaults;
            if (JsonNode.Parse(File.ReadAllText(file.FullName)) is not JsonObject root) return Defaults;
            long? reserveMegabytes = Whole(root["reserve_mb"], 0, 1_048_576);
            return new((int?)Whole(root["max_runs"], 1, 4096), reserveMegabytes * 1024 * 1024, Text(root["python"]), Text(root["agents_root"]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return Defaults; }
    }

    private static long? Whole(JsonNode? node, long minimum, long maximum)
        => node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue(out double number)
            && number == Math.Floor(number) && number >= minimum && number <= maximum ? (long)number : null;

    private static string Text(JsonNode? node)
        => node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>().Trim() : "";
}

// A second "Tlachihuani.Tray.exe --flow-pills a.flw" hands its files to the window that is
// already open: one small text file per launch in FlowPills\inbox, taken (read and deleted)
// when the window is asked to show itself. The files only name flows to open, never to run.
public static class FlowPillsInbox
{
    public static void Post(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return;
        Directory.CreateDirectory(FlowPillsPaths.Inbox);
        string name = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".txt";
        string partial = Path.Combine(FlowPillsPaths.Inbox, name + ".part");
        File.WriteAllLines(partial, files.Select(file => Path.GetFullPath(file)), new UTF8Encoding(false));
        File.Move(partial, Path.Combine(FlowPillsPaths.Inbox, name));
    }

    public static IReadOnlyList<string> Take()
    {
        var files = new List<string>();
        try
        {
            if (!Directory.Exists(FlowPillsPaths.Inbox)) return files;
            foreach (var note in Directory.GetFiles(FlowPillsPaths.Inbox, "*.txt").Order(StringComparer.Ordinal))
            {
                try
                {
                    files.AddRange(File.ReadAllLines(note).Where(line => line.Length > 0));
                    File.Delete(note);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return files;
    }
}
