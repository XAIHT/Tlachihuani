// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (the logger never traces itself)
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tlachihuani.Contracts;

public enum TraceLevel { Trace = 0, Debug = 1, Info = 2, Warn = 3, Error = 4, Off = 9 }

// Tlachihuani's verbose trace log. At Angela's request (20 November 2026) the level is
// Trace by default: the build writes a TraceLog.Step before every statement of every
// Tlachihuani source file and a TraceLog.Enter at every method, and the processes add
// every IPC frame, HTTP request, process, state change and thrown exception. Only lower
// it (TLACHIHUANI_LOG_LEVEL or Level) when Angela asks.
//
// One line per event:
//   2026-11-20 15:04:05.123456 -06:00  TRACE  xt  [1234:7]  ServicesForm.cs:57 RefreshState  · var scm = ...
// Writing happens on a background thread; callers never wait for the disk unless more
// than a million lines are queued. Secrets (keys, tokens, passwords) are masked.
public static partial class TraceLog
{
    private static Sink? sink;
    [ThreadStatic] private static bool busy;
    private static readonly int pid = Environment.ProcessId;

    public static TraceLevel Level { get; set; } = ParseLevel(Environment.GetEnvironmentVariable("TLACHIHUANI_LOG_LEVEL"));
    public static string? FilePath => sink?.Path;
    public static string Component => sink?.Component ?? "-";

    // The first Configure in a process wins. Candidate paths are tried in order; the
    // first one that can be opened for appending is used.
    public static void Configure(string component, IEnumerable<string> candidatePaths, long rotateBytes = 256L * 1024 * 1024, int keepFiles = 4)
    {
        if (sink is not null) return;
        Sink? opened = null;
        foreach (var candidate in candidatePaths)
        {
            try { opened = new Sink(component, Path.GetFullPath(candidate), rotateBytes, keepFiles); break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
        if (opened is null) return;
        sink = opened;
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            if (Level > TraceLevel.Trace || busy) return;
            var site = e.Exception.TargetSite;
            Write(TraceLevel.Trace, $"exception thrown: {e.Exception.GetType().FullName}: {e.Exception.Message} (in {site?.DeclaringType?.FullName}.{site?.Name})", "", "", 0);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { Write(TraceLevel.Error, "UNHANDLED EXCEPTION: " + e.ExceptionObject, "", "", 0); Flush(TimeSpan.FromSeconds(3)); };
        TaskScheduler.UnobservedTaskException += (_, e) => Write(TraceLevel.Error, "unobserved task exception: " + e.Exception, "", "", 0);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { if (!quietExit) Write(TraceLevel.Info, $"process {pid} exiting", "", "", 0); Flush(TimeSpan.FromSeconds(3)); };
        Write(TraceLevel.Info, $"trace log opened: component {component}, level {Level}, pid {pid}, image {Environment.ProcessPath}, " +
            $"arguments [{string.Join(" ", Environment.GetCommandLineArgs().Skip(1))}], Windows user {Environment.UserDomainName}\\{Environment.UserName}, " +
            $"session {Process.GetCurrentProcess().SessionId}, machine {Environment.MachineName}, .NET {Environment.Version}, OS {Environment.OSVersion}, " +
            $"file {opened.Path}, rotation {rotateBytes / (1024 * 1024)} MB x {keepFiles}", "", "", 0);
    }

    public static bool IsOn(TraceLevel level) => sink is not null && level >= Level && Level != TraceLevel.Off;

    public static void Write(TraceLevel level, string message, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var target = sink;
        if (target is null || level < Level || Level == TraceLevel.Off || busy) return;
        busy = true;
        try { var text = Redact(message); Emit(target, new Entry(level, text, file, line, member, null), 'M', file, line, text); }
        catch (Exception) { }
        finally { busy = false; }
    }

    public static void T(string message, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => Write(TraceLevel.Trace, message, member, file, line);
    public static void D(string message, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => Write(TraceLevel.Debug, message, member, file, line);
    public static void I(string message, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => Write(TraceLevel.Info, message, member, file, line);
    public static void W(string message, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => Write(TraceLevel.Warn, message, member, file, line);
    public static void E(string message, Exception? error = null, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(TraceLevel.Error, error is null ? message : message + " | " + error, member, file, line);

    // A header line followed by a block (for example a JSON message) indented by four spaces.
    public static void Block(TraceLevel level, string header, string block, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var target = sink;
        if (target is null || level < Level || Level == TraceLevel.Off || busy) return;
        busy = true;
        try
        {
            var head = Redact(header);
            var body = Redact(block);
            Emit(target, new Entry(level, head, file, line, member, body), 'B', file, line, head + body);
        }
        catch (Exception) { }
        finally { busy = false; }
    }

    // Woven before every statement: the statement's own text at its own file and line.
    public static void Step(string code, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var target = sink;
        if (target is null || Level > TraceLevel.Trace || busy) return;
        busy = true;
        try { Emit(target, new Entry(TraceLevel.Trace, "· " + Redact(code), file, line, member, null), 'S', file, line, null); }
        catch (Exception) { }
        finally { busy = false; }
    }

    // Woven at the start of every method: arguments in, time spent and the way out.
    public static Scope Enter(string where, string names, params object?[] values)
    {
        var target = sink;
        if (target is null || Level > TraceLevel.Trace || busy) return default;
        busy = true;
        try
        {
            var text = new StringBuilder("→ ").Append(where).Append('(');
            var keys = names.Length == 0 ? [] : names.Split(',');
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) text.Append(", ");
                string name = i < keys.Length ? keys[i] : "?";
                text.Append(name).Append('=').Append(SecretName().IsMatch(name) ? "***" : Show(values[i]));
            }
            text.Append(')');
            Emit(target, new Entry(TraceLevel.Trace, Redact(text.ToString()), "", 0, "", null), 'E', where, 0, null);
            return new Scope(where, Stopwatch.GetTimestamp());
        }
        catch (Exception) { return default; }
        finally { busy = false; }
    }

    public readonly struct Scope(string where, long started) : IDisposable
    {
        public void Dispose()
        {
            if (where is null) return;
            var target = sink;
            if (target is null || Level > TraceLevel.Trace || busy) return;
            busy = true;
            try { Emit(target, new Entry(TraceLevel.Trace, $"← {where} ({Stopwatch.GetElapsedTime(started).TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)} ms)", "", 0, "", null), 'X', where, 0, null); }
            catch (Exception) { }
            finally { busy = false; }
        }
    }

    // Lines that already carry their own timestamp and component (copied from another log).
    public static void Raw(string line)
    {
        var target = sink;
        if (target is null || Level == TraceLevel.Off) return;
        target.Enqueue(line);
    }

    public static string Json(JsonNode? node, int max = 64 * 1024)
    {
        if (node is null) return "null";
        string text;
        try { text = Masked(node.DeepClone()).ToJsonString(); }
        catch (Exception) { text = node.ToJsonString(); }
        return text.Length <= max ? text : text[..max] + $"… ({text.Length} characters)";
    }

    public static string Show(object? value)
    {
        try
        {
            string text = value switch
            {
                null => "null",
                string s => "\"" + s + "\"",
                JsonNode node => Json(node, 4096),
                IDictionary dictionary => $"{value.GetType().Name}[{dictionary.Count}]",
                ICollection collection => $"{value.GetType().Name}[{collection.Count}]",
                Delegate d => "delegate " + d.Method.Name,
                _ => value.ToString() ?? value.GetType().Name
            };
            return text.Length <= 512 ? text : text[..512] + "…";
        }
        catch (Exception) { return value?.GetType().Name ?? "null"; }
    }

    public static void Flush(TimeSpan timeout) => sink?.Flush(timeout);

    // Masks secrets in free text: computer keys, bearer tokens, cookies and JSON fields
    // whose name says they are secret.
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = PrivateKeyBlock().Replace(text, "[private key redacted]");
        text = SecretHeader().Replace(text, "$1: ***");
        text = ComputerKey().Replace(text, "xrc_***");
        text = BearerToken().Replace(text, "$1***");
        text = SecretJsonField().Replace(text, "\"$1\":\"***\"");
        text = SecretAssignment().Replace(text, "$1=***");
        text = SecretQuery().Replace(text, "$1***");
        return text;
    }

    private static JsonNode Masked(JsonNode node)
    {
        if (node is JsonObject obj)
            foreach (var key in obj.Select(p => p.Key).ToList())
            {
                if (SecretName().IsMatch(key) && obj[key] is JsonValue) obj[key] = "***";
                else if (obj[key] is JsonNode child) Masked(child);
            }
        else if (node is JsonArray array)
            foreach (var item in array) if (item is not null) Masked(item);
        return node;
    }

    [GeneratedRegex(@"xrc_[A-Za-z0-9_\-]{8,}")] private static partial Regex ComputerKey();
    [GeneratedRegex(@"-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----[\s\S]*?-----END (?:[A-Z]+ )?PRIVATE KEY-----")] private static partial Regex PrivateKeyBlock();
    [GeneratedRegex(@"(?im)\b(Authorization|Proxy-Authorization|Cookie|Set-Cookie)\s*:\s*[^\r\n]+", RegexOptions.None, 1000)] private static partial Regex SecretHeader();
    [GeneratedRegex(@"(?i)(bearer\s+)[A-Za-z0-9._~+/=\-]{8,}")] private static partial Regex BearerToken();
    [GeneratedRegex(@"(?i)""((?:access_|refresh_|id_)?token|secret|password|passwd|authorization|cookie|credential|api_?key|private_key|key|invitation|code_verifier|client_secret)""\s*:\s*""(?:\\.|[^""\\])*""")]
    private static partial Regex SecretJsonField();
    [GeneratedRegex(@"(?i)\b((?:access_|refresh_)?token|password|passwd|secret|api_?key|Key)\s*=\s*(?!\*\*\*)[^\s,;)}\]]+")] private static partial Regex SecretAssignment();
    // One-time codes in web addresses (RC invitations, sign-in callbacks, tickets).
    [GeneratedRegex(@"(?i)([?&](?:ticket|invite|invitation|code|state|session|token|access_token|key|password|secret|nonce|sig|signature|otp)=)[^&\s#""]+")] private static partial Regex SecretQuery();
    [GeneratedRegex(@"(?i)^(.*token.*|.*secret.*|.*passw.*|authorization|cookie|.*credential.*|api_?key|key|invitation|code_verifier|bearer)$")] private static partial Regex SecretName();

    private static TraceLevel ParseLevel(string? text) => Enum.TryParse<TraceLevel>(text, true, out var level) ? level : TraceLevel.Trace;

    private static string Format(Entry entry, string component)
    {
        var text = new StringBuilder(160 + entry.Message.Length)
            .Append(entry.Time.ToString("yyyy-MM-dd HH:mm:ss.ffffff zzz", CultureInfo.InvariantCulture)).Append("  ")
            .Append(entry.Level switch { TraceLevel.Trace => "TRACE", TraceLevel.Debug => "DEBUG", TraceLevel.Info => "INFO ", TraceLevel.Warn => "WARN ", _ => "ERROR" })
            .Append("  ").Append(component).Append("  [").Append(pid).Append(':').Append(entry.Thread).Append("]  ");
        if (entry.File.Length > 0) text.Append(Path.GetFileName(entry.File)).Append(':').Append(entry.Line).Append(' ').Append(entry.Member).Append("  ");
        foreach (char c in entry.Message) text.Append(c is '\r' or '\n' ? ' ' : c);
        if (entry.Block is not null)
            foreach (var part in entry.Block.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                text.Append(Environment.NewLine).Append("    ").Append(part);
        return text.ToString();
    }

    private sealed class Sink
    {
        public string Component { get; }
        public string Path { get; }
        private readonly long rotateBytes;
        private readonly int keepFiles;
        private readonly ConcurrentQueue<string> queue = new();
        private readonly AutoResetEvent signal = new(false);
        private readonly Mutex fileLock;
        private long pending;
        private long dropped;
        private static readonly UTF8Encoding Utf8 = new(false);

        public Sink(string component, string path, long rotateBytes, int keepFiles)
        {
            Component = component; Path = path; this.rotateBytes = Math.Max(1024 * 1024, rotateBytes); this.keepFiles = Math.Max(1, keepFiles);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using (new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { }
            // Several processes may append to the same file (XT, the control tool, setup):
            // one named lock per file keeps their lines whole and in order.
            fileLock = new Mutex(false, @"Local\XAIHT.TraceLog." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Utf8.GetBytes(path.ToUpperInvariant())))[..32]);
            new Thread(Run) { IsBackground = true, Name = "Tlachihuani trace writer", Priority = ThreadPriority.BelowNormal }.Start();
        }

        public void Enqueue(string line)
        {
            // Never let the queue take the process's memory (XT ran out of memory on
            // 21 November 2026 while the old build copied ~2 MB/s of service logs): far behind,
            // the caller waits up to 2 s for the writer; beyond 2 million waiting lines a line
            // is counted and left out, and the writer says how many were left out.
            if (Volatile.Read(ref pending) > 2_000_000) { Interlocked.Increment(ref dropped); signal.Set(); return; }
            queue.Enqueue(line);
            if (Interlocked.Increment(ref pending) > 1_000_000)
            {
                var wait = Stopwatch.StartNew();
                while (Volatile.Read(ref pending) > 500_000 && wait.ElapsedMilliseconds < 2000) Thread.Sleep(1);
            }
            signal.Set();
        }

        public void Flush(TimeSpan timeout)
        {
            var wait = Stopwatch.StartNew();
            while (Volatile.Read(ref pending) > 0 && wait.Elapsed < timeout) { signal.Set(); Thread.Sleep(5); }
        }

        private void Run()
        {
            busy = true;
            // One buffer for the life of the process: no new multi-megabyte string or array per
            // batch (those fragment the large-object heap until memory runs out).
            var buffer = new byte[8 << 20];
            while (true)
            {
                signal.WaitOne(50);
                int count = 0, used = 0;
                long left = Interlocked.Exchange(ref dropped, 0);
                if (left > 0)
                    used = Append(buffer, used, DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff zzz", CultureInfo.InvariantCulture)
                        + "  WARN   " + Component + $"  [{pid}:0]  {left} lines were left out: the log could not be written fast enough");
                while (used < (4 << 20) && queue.TryDequeue(out var line))
                {
                    count++;
                    if (Utf8.GetMaxByteCount(line.Length) + 2 > buffer.Length - used) { WriteOut(buffer, used); used = 0; }
                    if (Utf8.GetMaxByteCount(line.Length) + 2 > buffer.Length)
                    {
                        var big = Utf8.GetBytes(line + "\r\n");
                        WriteOut(big, big.Length);
                        continue;
                    }
                    used = Append(buffer, used, line);
                }
                if (used > 0) WriteOut(buffer, used);
                if (count > 0) Interlocked.Add(ref pending, -count);
            }
        }

        private static int Append(byte[] buffer, int used, string line)
        {
            used += Utf8.GetBytes(line, buffer.AsSpan(used));
            buffer[used++] = (byte)'\r';
            buffer[used++] = (byte)'\n';
            return used;
        }

        private void WriteOut(byte[] bytes, int length)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                bool locked = false;
                try
                {
                    try { locked = fileLock.WaitOne(2000); } catch (AbandonedMutexException) { locked = true; }
                    var info = new FileInfo(Path);
                    if (info.Exists && info.Length + length > rotateBytes) Rotate();
                    using var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    stream.Write(bytes, 0, length);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Thread.Sleep(20); }
                finally { if (locked) fileLock.ReleaseMutex(); }
            }
        }

        // Tlachihuani.log → Tlachihuani.1.log → … → Tlachihuani.<keep>.log (the oldest is deleted).
        private void Rotate()
        {
            string Numbered(int n) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!,
                System.IO.Path.GetFileNameWithoutExtension(Path) + "." + n + System.IO.Path.GetExtension(Path));
            File.Delete(Numbered(keepFiles));
            for (int n = keepFiles - 1; n >= 1; n--) if (File.Exists(Numbered(n))) File.Move(Numbered(n), Numbered(n + 1));
            File.Move(Path, Numbered(1));
        }
    }
}
