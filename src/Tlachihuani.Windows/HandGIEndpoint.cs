// Tlachihuani — Angela López Mendoza · XAIHT
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tlachihuani.Contracts;
using Tlachihuani.Core;

namespace Tlachihuani.Windows;

// HandGI, Tlachihuani's hand-gesture service, runs in the signed-in user's session: the camera, the
// volume and its keyboard shortcut belong to that session, never to Session 0. XT starts it, asks
// it how it is and what to do through a pipe only that user can open, and ends it through the
// process it published (UserProcessRecord). Nothing here is reachable from another account.
//
//   HandGI\service.json     the running process (PID, creation time, executable)
//   HandGI\settings.json    the user's choices (camera, start paused or on, start with services)
//   HandGI\profiles\        the signs the user taught
public static partial class HandGIEndpoint
{
    public const string ExecutableName = "Tlachihuani.HandGI.exe";
    public static string Folder => Path.Combine(LogPaths.UserFolder, "HandGI");
    public static string RecordPath => Path.Combine(Folder, "service.json");
    public static string SettingsPath => Path.Combine(Folder, "settings.json");

    // A test folder (TLACHIHUANI_USER_DIR) also gets its own names, so a test HandGI never meets
    // the user's real one.
    private static string Scope => LogPaths.UserScope;

    public static string SingletonName => @"Local\XAIHT.Tlachihuani.HandGI.v1" + Scope;

    public static string PipeName()
    {
        using var user = WindowsIdentity.GetCurrent();
        using var self = Process.GetCurrentProcess();
        string sid = user.User?.Value ?? throw new InvalidOperationException("No Windows identity.");
        return $"XAIHT.Tlachihuani.HandGI.v1{Scope}.{sid}.{self.SessionId}";
    }

    // The pages of HandGI's settings window that XT can open.
    public static readonly IReadOnlyList<string> Pages = ["signs", "camera", "try", "expert"];

    // The installed HandGI sits beside the other parts (...\Tlachihuani\HandGI\Tlachihuani.HandGI.exe).
    // A development build of XT (src\Tlachihuani.Tray\bin\<configuration>\...) uses the HandGI build
    // of the same configuration. TLACHIHUANI_HANDGI_EXE names another one, for tests.
    public static string? FindExecutable(string? baseDirectory = null)
    {
        var custom = Environment.GetEnvironmentVariable("TLACHIHUANI_HANDGI_EXE");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return File.Exists(custom) && string.Equals(Path.GetFileName(custom), ExecutableName, StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(custom) : null;
        }
        string here = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        string installed = Path.GetFullPath(Path.Combine(here, "..", "HandGI", ExecutableName));
        if (File.Exists(installed)) return installed;
        var development = DevelopmentLayout().Match(here);
        if (!development.Success) return null;
        string bin = Path.Combine(development.Groups["root"].Value, "src", "Tlachihuani.HandGI", "bin", development.Groups["configuration"].Value);
        if (!Directory.Exists(bin)) return null;
        return Directory.EnumerateFiles(bin, ExecutableName, SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    [GeneratedRegex(@"^(?<root>.+?)\\src\\Tlachihuani\.[A-Za-z.]+\\bin\\(?<configuration>[^\\]+)\\", RegexOptions.IgnoreCase)]
    private static partial Regex DevelopmentLayout();

    // The running HandGI, if any: the process its record names, still the same one.
    public static ProcessIdentity? OpenRunning(bool terminate = false)
        => UserProcessRecord.Read(RecordPath)?.OpenVerified(ExecutableName, terminate);

    public static bool IsRunning()
    {
        using var process = OpenRunning();
        return process is not null;
    }

    // Start Services includes HandGI only when the user asked for it in HandGI's settings.
    public static bool StartsWithServices()
    {
        try
        {
            var file = new FileInfo(SettingsPath);
            if (!file.Exists || file.Length > 64 * 1024) return false;
            return JsonNode.Parse(File.ReadAllText(file.FullName)) is JsonObject settings
                && settings["StartWithServices"] is JsonValue value
                && value.GetValueKind() == JsonValueKind.True;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    // Starts HandGI with the given arguments (--service, optionally --show <page>). The executable
    // is found by this app, never named by a caller.
    public static void Launch(params string[] arguments)
    {
        string executable = FindExecutable() ?? throw new FileNotFoundException("HandGI is not installed next to Tlachihuani.", ExecutableName);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var launched = Process.Start(start) ?? throw new InvalidOperationException("Windows did not start HandGI.");
        TraceLog.I($"HandGI started from {executable} as PID {launched.Id} ({string.Join(' ', arguments)})");
    }

    // One request to the running HandGI: status, arm, disarm, show or stop. The reply comes from the
    // process its record names, checked by the pipe's server PID and executable.
    public static async Task<JsonObject> CallAsync(string op, CancellationToken token, string? page = null)
    {
        if (op is not ("status" or "arm" or "disarm" or "show" or "stop")) throw new ArgumentException("Unknown HandGI request.", nameof(op));
        if (page is not null && !Pages.Contains(page)) throw new ArgumentException("Unknown HandGI page.", nameof(page));
        var record = UserProcessRecord.Read(RecordPath) ?? throw new LifecycleException("not_running", "HandGI is not running.");
        using (var process = record.OpenVerified(ExecutableName))
            if (process is null) throw new LifecycleException("not_running", "HandGI is not running.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var pipe = await LocalPipe.ConnectAsync(PipeName(), deadline.Token);
            using var server = LocalPipe.VerifyServer(pipe, record.Pid, record.Image);
            var request = new JsonObject { ["op"] = op };
            if (page is not null) request["page"] = page;
            await Protocol.WriteAsync(pipe, request, deadline.Token);
            var reply = await Protocol.ReadAsync<JsonObject>(pipe, deadline.Token);
            await pipe.WriteAsync(new byte[] { 6 }, deadline.Token);
            return reply;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("HandGI did not answer within 5 seconds.");
        }
    }
}
