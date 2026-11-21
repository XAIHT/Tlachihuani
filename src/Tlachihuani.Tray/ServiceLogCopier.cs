// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (a copy loop that ran through the statement tracer would log itself forever)
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tlachihuani.Contracts;
using Tlachihuani.Windows;

namespace Tlachihuani.Tray;

// The services cannot write into the Windows user's profile, so each writes its own log
// under %ProgramData%\XAIHT\<service>\logs (and scripts write theirs under script-logs).
// While XT runs, this copies every new line of those logs into Tlachihuani.log, about three
// times a second, so See log shows everything that happens on the computer in one place.
// Where it stopped in each file is kept in log-copy.json, so nothing is copied twice and
// nothing written while XT was closed is lost. A rotated file is finished from name.1.log.
internal sealed partial class ServiceLogCopier : IDisposable
{
    private readonly byte[] buffer = new byte[1024 * 1024];
    private readonly Thread thread;
    private readonly ManualResetEventSlim stopping = new(false);
    private readonly Dictionary<string, Position> positions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> lastProblem = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> announced = new(StringComparer.OrdinalIgnoreCase);
    private bool dirty;
    private DateTime lastSave = DateTime.UtcNow;

    private sealed class Position { public long Offset { get; set; } public long Created { get; set; } }

    private static string StateFile => Path.Combine(LogPaths.UserFolder, "log-copy.json");

    [GeneratedRegex(@"\.\d+\.log$", RegexOptions.IgnoreCase)] private static partial Regex Rotated();

    internal ServiceLogCopier() => thread = new Thread(Run) { IsBackground = true, Name = "Tlachihuani service log copier", Priority = ThreadPriority.BelowNormal };

    internal void Start()
    {
        Load();
        TraceLog.I($"service log copier started: folders {string.Join(", ", LogPaths.CopiedFolders)}; positions from {StateFile}");
        thread.Start();
    }

    private void Run()
    {
        while (!stopping.IsSet)
        {
            try { Pass(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Problem("copier", "copy pass failed: " + ex.Message); }
            if (dirty && DateTime.UtcNow - lastSave > TimeSpan.FromSeconds(5)) Save();
            stopping.Wait(300);
        }
        Save();
    }

    private void Pass()
    {
        foreach (var folder in LogPaths.CopiedFolders)
        {
            string[] files;
            try
            {
                if (!Directory.Exists(folder)) { Problem(folder, "folder not present yet; copying starts when it appears"); continue; }
                files = Directory.GetFiles(folder, "*.log", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Problem(folder, "folder cannot be listed: " + ex.Message); continue; }
            foreach (var file in files)
            {
                if (stopping.IsSet) return;
                if (Rotated().IsMatch(file)) continue;
                try { CopyFile(file); lastProblem.Remove(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Problem(file, "cannot be read: " + ex.Message); }
            }
        }
    }

    private void CopyFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return;
        long created = info.CreationTimeUtc.Ticks;
        if (!positions.TryGetValue(path, out var position))
        {
            position = new Position { Offset = 0, Created = created };
            positions[path] = position;
            dirty = true;
        }
        if (announced.Add(path)) TraceLog.I($"copying service log {path} ({info.Length} bytes) from byte {position.Offset}");
        if (position.Created != created || info.Length < position.Offset)
        {
            // The service rotated its log: finish the old file (now name.1.log), then start the new one.
            var previous = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".1.log");
            if (File.Exists(previous) && new FileInfo(previous).CreationTimeUtc.Ticks == position.Created)
                while (CopyChunk(previous, position, Script(path)) && !stopping.IsSet) { }
            TraceLog.I($"service log {path} was rotated; continuing in the new file");
            position.Offset = 0;
            position.Created = created;
            dirty = true;
        }
        CopyChunk(path, position, Script(path));
    }

    // Script transcripts are plain console text: each line is stamped, labelled with its
    // script and masked here. Service lines are already stamped and masked by their writer.
    private static string? Script(string path)
        => path.StartsWith(LogPaths.ScriptLogs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(path) : null;

    // Copies whole lines from position.Offset; returns true when more is waiting.
    private bool CopyChunk(string path, Position position, string? script)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long length = stream.Length;
        if (length <= position.Offset) return false;
        stream.Seek(position.Offset, SeekOrigin.Begin);
        // One reused buffer and one small string per line: no multi-megabyte arrays or strings
        // per pass (they fragmented the large-object heap until XT ran out of memory).
        int want = (int)Math.Min(buffer.Length, length - position.Offset);
        int read = 0;
        while (read < want) { int n = stream.Read(buffer, read, want - read); if (n == 0) break; read += n; }
        if (read == 0) return false;
        int end = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
        int take = end >= 0 ? end + 1 : read == buffer.Length ? read : 0;
        if (take == 0) return false;
        int start = 0;
        while (start < take)
        {
            int stop = Array.IndexOf(buffer, (byte)'\n', start, take - start);
            int next = stop < 0 ? take : stop + 1;
            int count = (stop < 0 ? take : stop) - start;
            if (count > 0 && buffer[start + count - 1] == (byte)'\r') count--;
            if (count >= 3 && buffer[start] == 0xEF && buffer[start + 1] == 0xBB && buffer[start + 2] == 0xBF) { start += 3; count -= 3; }
            if (count > 0)
            {
                var line = Encoding.UTF8.GetString(buffer, start, count);
                if (script is null) TraceLog.Raw(line);
                else TraceLog.Write(TraceLevel.Info, $"script {script} | {line}", "", "", 0);
            }
            start = next;
        }
        position.Offset += take;
        dirty = true;
        return position.Offset < length;
    }

    private void Problem(string where, string message)
    {
        if (lastProblem.TryGetValue(where, out var known) && known == message) return;
        lastProblem[where] = message;
        TraceLog.W($"service log copier: {where}: {message}");
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(StateFile)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, Position>>(File.ReadAllBytes(StateFile));
            if (saved is null) return;
            foreach (var (path, position) in saved) positions[path] = position;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            TraceLog.W($"service log copier: {StateFile} could not be read ({ex.Message}); every service log is copied from its start");
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(LogPaths.UserFolder);
            var temporary = StateFile + ".tmp";
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(positions));
            File.Move(temporary, StateFile, overwrite: true);
            dirty = false;
            lastSave = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Problem(StateFile, "positions could not be saved: " + ex.Message); }
    }

    public void Dispose()
    {
        if (stopping.IsSet) return;
        stopping.Set();
        // The event is not disposed: a copy pass still running after the join may still wait on it.
        if (thread.IsAlive) thread.Join(TimeSpan.FromSeconds(3));
    }
}
