// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (the window reads a run's state several times a second; every transition is written explicitly)
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tlachihuani.Contracts;

namespace Tlachihuani.FlowPills;

public enum RunState { Queued, Preparing, Running, Paused, Stopping, Completed, Stopped, Failed }
public enum NodeState { Idle, Waiting, Running, Done, Paused, Failed }

// One execution of one flow. It lives on a thread of its own, independent of every other run
// and of the window: the window only reads its state and its logs, and asks it to pause,
// resume or stop. Every run keeps its record in FlowPills\runs\{id}: the flow exactly as it
// ran, events.log (what happened, line by line) and run.json (how it ended).
public abstract class FlowRun : IDisposable
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static readonly UTF8Encoding Utf8 = new(false);
    private static long sequence;
    private readonly ConcurrentDictionary<string, NodeState> nodes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> traversed = new(StringComparer.Ordinal);
    private readonly List<LogTail> logs = [];
    private readonly object stateGate = new(), fileGate = new();
    private long version;
    private int disposed;
    private volatile Thread? worker;

    protected FlowRun(FlowDocument document, TlamatiniInstall install)
    {
        Document = document;
        Install = install;
        Number = Interlocked.Increment(ref sequence);
        Id = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6];
        Folder = Path.Combine(FlowPillsPaths.Runs, Id);
        Directory.CreateDirectory(Folder);
        Events = new LogTail("Run events", Path.Combine(Folder, "events.log"));
        logs.Add(Events);
        foreach (var node in document.Nodes) nodes[node.Id] = NodeState.Idle;
        Created = DateTimeOffset.Now;
    }

    public long Number { get; }
    public string Id { get; }
    public FlowDocument Document { get; }
    public TlamatiniInstall Install { get; }
    public string Folder { get; }
    public LogTail Events { get; }
    public DateTimeOffset Created { get; }
    public DateTimeOffset? Started { get; private set; }
    public DateTimeOffset? Ended { get; private set; }
    public RunState State { get; private set; } = RunState.Queued;
    public string Status { get; private set; } = "Waiting to start";
    public long Version => Interlocked.Read(ref version);
    public bool IsActive => State is RunState.Preparing or RunState.Running or RunState.Paused or RunState.Stopping;
    public bool IsFinished => State is RunState.Completed or RunState.Stopped or RunState.Failed;
    public abstract bool CanPause { get; }
    public abstract bool CanResume { get; }
    public bool CanStop => !IsFinished;

    // Nothing of the run is still at work: it never started, or its thread has written run.json
    // and announced its end. A run can be Stopped a moment before it is settled.
    public bool IsSettled => worker is not { IsAlive: true };

    // Waits, at most the given time, for the run's thread to finish.
    public bool Settle(TimeSpan timeout) => worker is not { } thread || thread.Join(timeout);
    public virtual ulong PeakMemory => 0;
    public int RunningNodes => nodes.Values.Count(state => state == NodeState.Running);
    public IReadOnlyList<LogTail> Logs { get { lock (logs) return logs.ToArray(); } }
    public NodeState NodeStateOf(string id) => nodes.TryGetValue(id, out var state) ? state : NodeState.Idle;
    public bool WasTraversed(string edgeId) => traversed.ContainsKey(edgeId);
    protected volatile bool StopRequested;

    // Raised once, on the run's own thread (or the caller's for a run stopped while queued).
    public event Action<FlowRun>? Finished;

    public TimeSpan Elapsed => Started is { } started ? (Ended ?? DateTimeOffset.Now) - started : TimeSpan.Zero;

    // The registry admits the run: from here on it lives on its own thread.
    internal bool TryBegin()
    {
        lock (stateGate)
        {
            if (State != RunState.Queued) return false;
            State = RunState.Preparing;
            Status = "Starting";
            Started = DateTimeOffset.Now;
            worker = new Thread(Live, 512 * 1024) { IsBackground = true, Name = "Flow Pills run " + Id };
            worker.Start();
            return true;
        }
    }

    // While it waits for room, a queued run shows why.
    internal void Waiting(string reason)
    {
        lock (stateGate) if (State == RunState.Queued && Status != reason) Status = reason; else return;
        Touch();
    }

    public abstract void Pause();
    public abstract void Resume();

    // Safe from any thread at any moment. A second Stop while stopping, or a forced one, ends the
    // run at once.
    public void Stop(bool force = false)
    {
        bool queued, again;
        lock (stateGate)
        {
            if (IsFinished) return;
            queued = State == RunState.Queued;
            again = StopRequested;
            StopRequested = true;
            if (queued)
            {
                State = RunState.Stopped;
                Status = "Stopped before it started";
                Ended = DateTimeOffset.Now;
            }
        }
        if (queued)
        {
            Event("Stopped before it started");
            WriteSummary();
            Touch();
            RaiseFinished();
            return;
        }
        OnStop(force: force || again);
    }

    protected abstract void OnStop(bool force);
    protected abstract void Execute();

    private void Live()
    {
        Event($"Run {Id} begins: {Document.FilePath} ({Document.KindLabel}, {Document.Nodes.Count} nodes)");
        TraceLog.I($"Flow Pills run {Id} begins: {Document.FilePath}");
        WriteSummary();
        try { Execute(); }
        catch (FlowPillsException ex) { Finish(RunState.Failed, ex.Message); }
        catch (OperationCanceledException) when (StopRequested) { Finish(RunState.Stopped, "Stopped"); }
        catch (Exception ex)
        {
            TraceLog.E($"Flow Pills run {Id} failed", ex);
            Finish(RunState.Failed, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (!IsFinished) Finish(StopRequested ? RunState.Stopped : RunState.Completed, StopRequested ? "Stopped" : "Completed");
            RaiseFinished();
        }
    }

    private void RaiseFinished()
    {
        try { Finished?.Invoke(this); }
        catch (Exception ex) { TraceLog.E("Flow Pills: a run-finished handler failed", ex); }
    }

    protected void SetState(RunState state, string status)
    {
        lock (stateGate)
        {
            if (IsFinished) return;
            State = state;
            Status = status;
        }
        Event(status);
        Touch();
    }

    protected void Finish(RunState state, string status)
    {
        lock (stateGate)
        {
            if (IsFinished) return;
            State = state;
            Status = status;
            Ended = DateTimeOffset.Now;
        }
        Event($"{state}: {status}");
        TraceLog.I($"Flow Pills run {Id} ended {state}: {status}");
        WriteSummary();
        Touch();
    }

    // One line of events.log, read back by the window through its own tail.
    protected void Event(string message)
    {
        string text = message.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "\n              ", StringComparison.Ordinal);
        string line = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + text + "\n";
        lock (fileGate)
        {
            try { File.AppendAllText(Events.FilePath, line, Utf8); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Events.Poll();
        Touch();
    }

    protected void SetNode(string id, NodeState state)
    {
        if (nodes.TryGetValue(id, out var current) && current == state) return;
        nodes[id] = state;
        Touch();
    }

    protected void Traverse(string edgeId)
    {
        if (traversed.TryAdd(edgeId, 0)) Touch();
    }

    protected void AddLog(LogTail tail)
    {
        lock (logs) logs.Add(tail);
        Touch();
    }

    protected void Touch() => Interlocked.Increment(ref version);

    private void WriteSummary()
    {
        var summary = new JsonObject
        {
            ["id"] = Id,
            ["file"] = Document.FilePath,
            ["kind"] = Document.KindLabel,
            ["state"] = State.ToString(),
            ["status"] = Status,
            ["created"] = Created.ToString("O", CultureInfo.InvariantCulture),
            ["started"] = Started?.ToString("O", CultureInfo.InvariantCulture),
            ["ended"] = Ended?.ToString("O", CultureInfo.InvariantCulture),
            ["agents_root"] = Install.AgentsRoot,
        };
        lock (fileGate)
        {
            try { File.WriteAllText(Path.Combine(Folder, "run.json"), summary.ToJsonString(Indented) + "\n", Utf8); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1) return;
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) { }
}
