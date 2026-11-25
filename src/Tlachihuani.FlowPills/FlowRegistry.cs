// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (admits waiting runs every second; every submission, admission and end is written explicitly)
using Tlachihuani.Contracts;

namespace Tlachihuani.FlowPills;

// The Flow Pills service: every flow it runs, each independent of the others. A new run waits
// until the machine has room for it (FlowCapacity) and then lives on its own thread. The window
// reads the runs and asks them to pause, resume or stop; it never runs anything itself.
public sealed class FlowRegistry : IDisposable
{
    private readonly object gate = new();
    private readonly List<FlowRun> runs = [];
    private readonly Queue<FlowRun> waiting = new();
    private readonly Timer admitter;
    private bool disposed;

    public FlowRegistry(FlowPillsSettings settings)
    {
        Capacity = new FlowCapacity(settings);
        admitter = new Timer(_ => Admit(), null, 1000, 1000);
        TraceLog.I($"Flow Pills service: up to {Capacity.MaxRuns} flows at once ({Capacity.MaxRunsSource}); "
            + $"keeps {FlowCapacity.Size(Capacity.ReserveBytes)} of memory free ({Capacity.ReserveSource})");
    }

    public FlowCapacity Capacity { get; }
    public IReadOnlyList<FlowRun> Runs { get { lock (gate) return runs.ToArray(); } }
    public int Active { get { lock (gate) return runs.Count(run => run.IsActive); } }
    public int Waiting { get { lock (gate) return runs.Count(run => run.State == RunState.Queued); } }

    // Nothing runs, nothing waits, and every run has finished writing how it ended.
    public bool Idle { get { lock (gate) return runs.All(run => run.IsFinished && run.IsSettled); } }

    public FlowRun Submit(FlowDocument document, TlamatiniInstall install)
    {
        FlowRun run = document.Kind == FlowKind.Agents ? new AgentFlowRun(document, install) : new PromptFlowRun(document, install);
        run.Finished += _ => Admit();
        lock (gate)
        {
            if (disposed)
            {
                run.Dispose();
                throw new ObjectDisposedException(nameof(FlowRegistry));
            }
            runs.Add(run);
            waiting.Enqueue(run);
        }
        TraceLog.I($"Flow Pills: {document.FilePath} submitted as run {run.Id}");
        Admit();
        return run;
    }

    // Forgets a run that has ended; its record stays in FlowPills\runs.
    public bool Remove(FlowRun run)
    {
        lock (gate)
        {
            if (!run.IsFinished || !runs.Remove(run)) return false;
        }
        run.Dispose();
        return true;
    }

    public void StopAll()
    {
        foreach (var run in Runs) run.Stop();
    }

    private void Admit()
    {
        lock (gate)
        {
            if (disposed) return;
            while (waiting.Count > 0)
            {
                var next = waiting.Peek();
                if (next.State != RunState.Queued)
                {
                    waiting.Dequeue();
                    continue;
                }
                string? why = Capacity.WhyNot(runs.Count(run => run.IsActive), next.Document.Kind);
                if (why is not null)
                {
                    foreach (var queued in waiting) queued.Waiting(why);
                    return;
                }
                waiting.Dequeue();
                if (next.TryBegin()) TraceLog.I($"Flow Pills: run {next.Id} starts ({runs.Count(run => run.IsActive)} active)");
            }
        }
    }

    // Whatever still runs is stopped at once. Each run then gets up to three seconds in all to
    // write how it ended before its job is closed, which ends anything left behind.
    public void Dispose()
    {
        FlowRun[] all;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            all = runs.ToArray();
            runs.Clear();
            waiting.Clear();
        }
        admitter.Dispose();
        foreach (var run in all) run.Stop(force: true);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        foreach (var run in all)
        {
            var left = deadline - DateTime.UtcNow;
            if (left > TimeSpan.Zero && run.Settle(left)) continue;
            if (!run.IsSettled) TraceLog.W($"Flow Pills: run {run.Id} had not finished ending when the window closed; its job ends what is left");
        }
        foreach (var run in all) run.Dispose();
    }
}
