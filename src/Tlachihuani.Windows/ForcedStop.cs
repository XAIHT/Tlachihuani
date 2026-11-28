// Tlachihuani — Angela López Mendoza · XAIHT
using System.ComponentModel;
using System.Diagnostics;
using Tlachihuani.Contracts;
using Tlachihuani.Core;

namespace Tlachihuani.Windows;

// What Stop Services did to one service.
public sealed record StopOutcome(string Service, string Result, string Message)
{
    // already_stopped, not_installed, not_running, stopped and ended all leave the service down.
    public bool Down => Result is "already_stopped" or "not_installed" or "not_running" or "stopped" or "ended";
}

// A process XT may end: the one Windows names for a service, or the one a user-session service published.
public interface IEndableProcess : IDisposable
{
    string ImagePath { get; }
    bool HasExited { get; }
    bool CanTerminate { get; }
    void Terminate(uint exitCode);
}

// Stop Services in XT ends every Tlachihuani service, whatever it is doing.
//
// A Windows service is asked to stop first. Whatever still runs after a short grace is ended through
// the process the Service Control Manager names for that service, once its executable has been
// checked against the installation; never by image name, never by a remembered PID. A service
// installed before XT could end it (OperatorProcessAccess.AllowOperatorStop) is waited for instead,
// and its outcome says so.
//
// A service in the user's session (HandGI, Flow Pills) is ended at once through the process it
// published, checked by its creation time and executable. The programs it started live in
// kill-on-close job objects and end with it.
public static class ForcedStop
{
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DeniedWait = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan GuardWindow = TimeSpan.FromSeconds(75);

    private static readonly object guards = new();
    private static readonly List<Task> running = [];
    private static CancellationTokenSource restartGuard = new();

    // expected validates the installation and returns the executable its service must be running.
    public static Task<StopOutcome> ServiceAsync(string name, IServiceBackend backend, Func<string> expected, CancellationToken token)
        => ServiceAsync(name, backend, expected, OpenServiceProcess, Grace, DeniedWait, token);

    public static async Task<StopOutcome> ServiceAsync(string name, IServiceBackend backend, Func<string> expected,
        Func<int, IEndableProcess?> open, TimeSpan grace, TimeSpan deniedWait, CancellationToken token)
    {
        var before = backend.Query();
        if (before.State == "not_installed") return new(name, "not_installed", $"{name} is not installed.");
        if (before.State == "stopped") return new(name, "already_stopped", $"{name} was already stopped.");
        string image = expected();
        using var process = open(before.Pid);
        if (process is not null && !UserProcessRecord.SamePath(process.ImagePath, image))
            return new(name, "failed", $"{name}: the running process is not this installation's executable, so nothing was ended.");

        TraceLog.I($"Stop Services: asking {name} to stop (Windows reports {before.State}, PID {before.Pid})");
        try { backend.Stop(); }
        catch (Win32Exception ex) { TraceLog.I($"{name} did not take the stop request (Windows error {ex.NativeErrorCode}: {ex.Message})"); }
        if (await GoneAsync(backend, process, grace, token)) return new(name, "stopped", $"{name} stopped.");

        if (process is null)
        {
            return await GoneAsync(backend, null, deniedWait, token)
                ? new(name, "stopped", $"{name} stopped.")
                : new(name, "failed", $"{name} did not stop, and Windows named no process to end.");
        }
        if (!process.CanTerminate)
        {
            TraceLog.W($"{name} still runs after {grace.TotalSeconds:0} seconds and was installed before XT could end it; waiting for it");
            return await GoneAsync(backend, process, deniedWait, token)
                ? new(name, "stopped", $"{name} stopped.")
                : new(name, "end_denied", $"{name} is still running. It was installed before XT could end it; installing Tlachihuani again allows that.");
        }
        TraceLog.W($"Stop Services: {name} still runs after {grace.TotalSeconds:0} seconds; ending its process {before.Pid}");
        process.Terminate(1);
        if (!await GoneAsync(backend, process, ExitWait, token))
            return new(name, "failed", $"{name} was told to end, but its process has not exited.");
        TraceLog.W($"Stop Services ended {name} (process {before.Pid})");
        return new(name, "ended", $"{name} was ended.");
    }

    // True once the process has exited (or, with no process to watch, once Windows reports Stopped).
    private static async Task<bool> GoneAsync(IServiceBackend backend, IEndableProcess? process, TimeSpan wait, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            bool gone = process is null ? backend.Query().State is "stopped" or "not_installed" : process.HasExited;
            if (gone) return true;
            if (clock.Elapsed >= wait) return false;
            await Task.Delay(100, token);
        }
    }

    // The real opener: asks for the right to end the process, and settles for observing it when the
    // service does not grant that right.
    public static IEndableProcess? OpenServiceProcess(int pid)
    {
        if (pid == 0) return null;
        try { return ProcessIdentity.Open(pid, terminate: true); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5) { }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 87) { return null; }
        try { return ProcessIdentity.Open(pid); }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 5 or 87) { return null; }
    }

    // A service of the user's own session: ended at once, then its record is removed.
    public static async Task<StopOutcome> UserProcessAsync(string name, string recordPath, string fileName, CancellationToken token)
    {
        var record = UserProcessRecord.Read(recordPath);
        using var process = record?.OpenVerified(fileName, terminate: true);
        if (record is null || process is null) return new(name, "not_running", $"{name} was not running.");
        TraceLog.W($"Stop Services: ending {name} (process {record.Pid})");
        process.Terminate(1);
        var clock = Stopwatch.StartNew();
        while (!process.HasExited)
        {
            if (clock.Elapsed >= ExitWait) return new(name, "failed", $"{name} was told to end, but its process has not exited.");
            await Task.Delay(50, token);
        }
        UserProcessRecord.Clear(recordPath, record.Pid);
        TraceLog.W($"Stop Services ended {name} (process {record.Pid})");
        return new(name, "ended", $"{name} was ended.");
    }

    // After a service was ended, Windows starts it again when its recovery options say so (Jobs is
    // installed to restart 5, 30 and 60 seconds after its process ends). For a little over a minute
    // the guard stops such a restart again, the ordinary way. Any start in XT cancels the guard.
    public static void GuardAgainstRestart(string name, IServiceBackend backend)
    {
        CancellationToken token;
        lock (guards) token = restartGuard.Token;
        var guard = Task.Run(() => GuardAsync(name, backend, GuardWindow, token), token);
        lock (guards) { running.RemoveAll(task => task.IsCompleted); running.Add(guard); }
    }

    // XT closing while a guard still watches finishes that guard first, so a restart Windows makes
    // after Stop Services never outlives the tray. Returns at once when no guard is watching.
    public static void FinishGuards(TimeSpan limit)
    {
        Task[] pending;
        lock (guards) pending = running.Where(task => !task.IsCompleted).ToArray();
        if (pending.Length == 0) return;
        TraceLog.I($"XT finishes {pending.Length} restart guard(s) before it closes");
        try { Task.WaitAll(pending, limit); }
        catch (AggregateException) { }
    }

    public static void CancelRestartGuards()
    {
        CancellationTokenSource old;
        lock (guards) { old = restartGuard; restartGuard = new(); }
        old.Cancel();
        old.Dispose();
    }

    public static async Task GuardAsync(string name, IServiceBackend backend, TimeSpan window, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            while (clock.Elapsed < window)
            {
                await Task.Delay(1000, token);
                NativeStatus state;
                try { state = backend.Query(); }
                catch (Exception ex) when (ex is Win32Exception or LifecycleException) { continue; }
                if (state.State is not ("start_pending" or "running")) continue;
                TraceLog.W($"Windows started {name} again after Stop Services ended it (its recovery setting); stopping it again");
                try
                {
                    await new Lifecycle(backend).ExecuteAsync("stop", TimeSpan.FromSeconds(40), token);
                    TraceLog.I($"{name} is stopped again");
                    return;   // an ordinary stop is not a failure, so Windows does not start it again
                }
                catch (Exception ex) when (ex is LifecycleException or Win32Exception)
                {
                    TraceLog.E($"{name} could not be stopped again: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
