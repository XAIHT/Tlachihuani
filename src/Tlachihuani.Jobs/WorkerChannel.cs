using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace Tlachihuani.Jobs;

// Exactly one request is written at a time. One reader per engine process copies its
// replies into a queue and is never cancelled, so a reply is never cut in half. A caller
// that stops waiting leaves its reply owed; the next caller discards owed replies before
// it writes, so no caller can consume another caller's reply.
//
// A busy engine is not a dead one (Angela, 26 November 2026). The first tick after a start
// can outlast one request's budget; closing the engine then only started the same slow tick
// again, until XT reported that Start Services could not confirm readiness. The engine is
// closed only when it exits, stops reading requests, or owes a reply for HungAfter.
internal sealed class WorkerChannel : IDisposable
{
    internal static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(15);
    // Waits only for READY and returns as soon as it arrives or the engine exits. A modest
    // computer can need many seconds to load Python and libtorrent from a slow disk.
    internal static readonly TimeSpan StartupBudget = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan HungAfter = TimeSpan.FromMinutes(3);
    private Process? process;
    private Channel<string>? replies;
    private int owed;
    private long owedSince;
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool Running
    {
        get { var current = process; try { return current is { HasExited: false }; } catch (InvalidOperationException) { return false; } }
    }
    internal static Process Spawn(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (string arg in arguments) start.ArgumentList.Add(arg);
        // The web facade and the torrent engine write their trace logs next to jobs.log.
        if (Tlachihuani.Contracts.TraceLog.FilePath is { } jobsLog) start.Environment["TLACHIHUANI_LOG_DIR"] = Path.GetDirectoryName(jobsLog);
        start.Environment["TLACHIHUANI_LOG_LEVEL"] = Tlachihuani.Contracts.TraceLog.Level.ToString();
        var child = Process.Start(start) ?? throw new IOException("Could not start provider process.");
        Tlachihuani.Contracts.TraceLog.I($"started provider process {child.Id}: {executable} {string.Join(" ", arguments)}");
        try
        {
            OwnedProcesses.Attach(child);
            child.StandardInput.WriteLine("START"); child.StandardInput.Flush();
            return child;
        }
        catch
        {
            try { if (!child.HasExited) child.Kill(true); } finally { child.Dispose(); }
            throw;
        }
    }
    public void Start(JobsSettings settings)
    {
        gate.Wait();
        try
        {
            CloseProcess();
            process = Spawn(Path.Combine(AppContext.BaseDirectory, "runtime", "python.exe"),
                ["-I", "-B", "-X", "utf8", "-u", Path.Combine(AppContext.BaseDirectory, "worker", "worker.py"), settings.State, settings.Development ? "development" : "installed"]);
            replies = ReadReplies(process);
            using var startup = new CancellationTokenSource(StartupBudget);
            string? first = null;
            try { first = replies.Reader.ReadAsync(startup.Token).AsTask().GetAwaiter().GetResult(); }
            catch (ChannelClosedException) { }
            if (first != "READY")
                throw new IOException("Torrent engine failed its startup check.");
        }
        catch { CloseProcess(); throw; }
        finally { gate.Release(); }
    }
    public async Task<JsonObject> Request(JsonObject command, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(RequestBudget);
        try { await gate.WaitAsync(deadline.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"The Torrenter engine is busy: another request held it for {RequestBudget.TotalSeconds:0} seconds.");
        }
        try
        {
            var proc = process;
            var lines = replies;
            if (proc is null || lines is null || proc.HasExited) throw new IOException("Torrent engine unavailable.");
            // The reader may already have received this reply while no caller was waiting.
            // Age alone describes an unconsumed reply, not an unresponsive engine.
            if (owed > 0 && !lines.Reader.TryPeek(out _) && Stopwatch.GetElapsedTime(owedSince) > HungAfter)
            {
                Tlachihuani.Contracts.TraceLog.W($"engine has not answered for {Stopwatch.GetElapsedTime(owedSince).TotalSeconds:0} seconds; closing it");
                CloseProcess();
                throw new IOException("Torrent engine stopped answering.");
            }
            var started = Stopwatch.GetTimestamp();
            var writing = false;
            try
            {
                // Replies owed to callers that stopped waiting arrive first; they are not ours.
                while (owed > 0)
                {
                    await lines.Reader.ReadAsync(deadline.Token);
                    owed--; owedSince = Stopwatch.GetTimestamp();
                    Tlachihuani.Contracts.TraceLog.T("engine ⇒ late reply discarded; its caller had stopped waiting");
                }
                Tlachihuani.Contracts.TraceLog.T($"engine ⇐ {Tlachihuani.Contracts.TraceLog.Json(command)}");
                writing = true;
                await proc.StandardInput.WriteLineAsync(command.ToJsonString().AsMemory(), deadline.Token);
                writing = false;
                owed = 1; owedSince = Stopwatch.GetTimestamp();
                var line = await lines.Reader.ReadAsync(deadline.Token);
                owed = 0;
                var reply = JsonNode.Parse(line)!.AsObject();
                Tlachihuani.Contracts.TraceLog.T($"engine ⇒ ({Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.#} ms) {Tlachihuani.Contracts.TraceLog.Json(reply)}");
                return reply;
            }
            catch (OperationCanceledException) when (writing)
            {
                // A request that cannot even be written means the engine stopped reading its
                // input, and a partly written line would corrupt the next request.
                Tlachihuani.Contracts.TraceLog.W("engine did not take a request; closing the engine");
                CloseProcess();
                if (token.IsCancellationRequested) throw;
                throw new IOException("Torrent engine stopped reading requests.");
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                Tlachihuani.Contracts.TraceLog.W($"engine busy: no reply within {RequestBudget.TotalSeconds:0} seconds; it keeps running and its late reply will be discarded");
                throw new TimeoutException($"The Torrenter engine is busy and did not answer within {RequestBudget.TotalSeconds:0} seconds.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Tlachihuani.Contracts.TraceLog.W($"engine request failed; closing the engine: {ex.GetType().Name}: {ex.Message}");
                CloseProcess();
                throw new IOException("Torrent engine unavailable.", ex);
            }
        }
        finally { gate.Release(); }
    }
    public void Dispose()
    {
        gate.Wait();
        try { CloseProcess(); } finally { gate.Release(); }
    }
    private static Channel<string> ReadReplies(Process engine)
    {
        var lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var output = engine.StandardOutput;
        _ = Task.Run(async () =>
        {
            try { while (await output.ReadLineAsync() is string line) lines.Writer.TryWrite(line); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
            finally { lines.Writer.TryComplete(); }
        });
        return lines;
    }
    private void CloseProcess()
    {
        owed = 0; replies = null;
        if (process is null) return;
        var proc = process; process = null;
        try
        {
            // An input already broken or still busy must not keep the process alive.
            try { proc.StandardInput.Close(); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
            if (!proc.WaitForExit(3000)) { proc.Kill(true); proc.WaitForExit(3000); }
        }
        catch (InvalidOperationException) { }
        finally { proc.Dispose(); }
    }
}
