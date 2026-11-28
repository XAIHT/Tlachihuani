// Tlachihuani — Angela López Mendoza · XAIHT
using System.ComponentModel;
using System.Text.Json.Nodes;
using Tlachihuani.Client;
using Tlachihuani.Contracts;
using Tlachihuani.Core;
using Tlachihuani.FlowPills;
using Tlachihuani.Windows;

namespace Tlachihuani.Tray;

internal sealed class TrayServices(Controller primary)
{
    private readonly ScmBackend jobs = ScmBackend.ForJobs();
    private static readonly HttpClient http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });

    internal Task<RemoteControllerState> ObserveRemoteControllerAsync(CancellationToken token) =>
        ServiceProbes.RemoteAsync(() => ScmBackend.ForRemoteController().Query(), () =>
        {
            ScmBackend.ForRemoteController().ValidateRemoteControllerInstallation();
            return RcInstallation.Configuration().IncludeInStartServices;
        }, t => RcEndpoint.CallInstalledAsync(new() { ["op"] = "status" }, t), token);

    internal Task<JobsState> ObserveJobsAsync(CancellationToken token) =>
        ServiceProbes.JobsAsync(jobs.Query, JobsEndpoint.CallInstalledAsync, http, token);

    internal async Task<ServiceSnapshot> ObserveAsync(CancellationToken token)
    {
        var main = primary.ExecuteAsync("status", token);
        var provider = ObserveJobsAsync(token);
        var rc = ObserveRemoteControllerAsync(token);
        await Task.WhenAll(main, provider, rc);
        return new(await main, await provider, await rc);
    }

    internal async Task<ServiceSnapshot> StartAsync(CancellationToken token)
    {
        // A start the user asks for wins over a guard still stopping restarts after Stop Services.
        ForcedStop.CancelRestartGuards();
        await ChangePrimaryAsync("start", token);
        try
        {
            jobs.ValidateJobsInstallation();
            await new Lifecycle(jobs).ExecuteAsync("start", TimeSpan.FromSeconds(30), token);
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is not OutOfMemoryException)
        { throw new InvalidOperationException("Tlachihuani.Jobs could not start: " + ex.Message, ex); }
        using (var enable = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            enable.CancelAfter(TimeSpan.FromSeconds(40));
            // Explicit Start re-enables a provider previously stopped in Services.
            try { await JobsEndpoint.CallInstalledAsync(new() { ["op"] = "start" }, enable.Token); }
            catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
            { throw new TimeoutException("Tlachihuani.Jobs did not confirm enabling Torrenter within 40 seconds.", ex); }
        }
        try
        {
            if (ScmBackend.ForRemoteController().Query().State != "not_installed" && RcInstallation.Configuration().IncludeInStartServices)
                await RcLifecycle.ExecuteAsync("start", token);
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is not OutOfMemoryException)
        { throw new InvalidOperationException("Remote-Controller could not start: " + ex.Message, ex); }
        StartHandGIIfChosen();
        return await ServiceReadiness.WaitAsync(ObserveAsync, TimeSpan.FromSeconds(40), token);
    }

    // HandGI starts with the other services only when the user chose that in HandGI's Camera
    // settings ("Start with the other services"). It is optional, so a failure is logged, never fatal.
    private static void StartHandGIIfChosen()
    {
        if (!HandGIEndpoint.StartsWithServices()) return;
        try
        {
            if (HandGIEndpoint.IsRunning()) { TraceLog.I("Start Services: HandGI is already running"); return; }
            HandGIEndpoint.Launch("--service");
        }
        catch (Exception ex) when (ex is FileNotFoundException or Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        { TraceLog.W($"Start Services could not start HandGI: {ex.Message}"); }
    }

    private async Task ChangePrimaryAsync(string action, CancellationToken token)
    {
        var reply = await primary.ExecuteAsync(action, token);
        if (!reply.Ok) throw new ServiceReadinessException(reply.Error?.Message ?? $"Tlachihuani could not {action}.", new(reply, null, null), []);
    }

    // Stop Services ends every Tlachihuani service at once, whatever it is doing. HandGI and Flow
    // Pills (processes in this user's session) are ended immediately. Every Windows service is asked
    // to stop, and whatever still runs a few seconds later is ended through the process Windows names
    // for it (ForcedStop). A service Windows restarts by itself (Jobs' recovery setting) is stopped
    // again. Each service's outcome goes to the log; any service left running is reported.
    internal async Task StopAsync(CancellationToken token)
    {
        TraceLog.W("Stop Services: ending every Tlachihuani service");
        var main = new ScmBackend();
        var remote = ScmBackend.ForRemoteController();
        var windows = new Dictionary<string, IServiceBackend>
        {
            ["Remote-Controller"] = remote, [JobsEndpoint.ServiceName] = jobs, [Contracts.Protocol.ServiceName] = main
        };
        var outcomes = await Task.WhenAll(
            Guarded("HandGI", () => ForcedStop.UserProcessAsync("HandGI", HandGIEndpoint.RecordPath, HandGIEndpoint.ExecutableName, token)),
            Guarded("Flow Pills", () => ForcedStop.UserProcessAsync("Flow Pills", FlowPillsPaths.ProcessRecord, FlowPillsSignals.ExecutableName, token)),
            Guarded("Remote-Controller", () => ForcedStop.ServiceAsync("Remote-Controller", remote,
                () => { remote.ValidateRemoteControllerInstallation(); return RcInstallation.Image; }, token)),
            Guarded(JobsEndpoint.ServiceName, () => ForcedStop.ServiceAsync(JobsEndpoint.ServiceName, jobs,
                () => { jobs.ValidateJobsInstallation(); return JobsInstallation.Image; }, token)),
            Guarded(Contracts.Protocol.ServiceName, () => ForcedStop.ServiceAsync(Contracts.Protocol.ServiceName, main,
                () => { var install = Installation.Load(); main.ValidateInstallation(install); return install.ServicePath; }, token)));
        foreach (var outcome in outcomes)
        {
            if (outcome.Down) TraceLog.I($"Stop Services · {outcome.Service}: {outcome.Message}");
            else TraceLog.E($"Stop Services · {outcome.Service}: {outcome.Message}");
            if (outcome.Result == "ended" && windows.TryGetValue(outcome.Service, out var backend))
                ForcedStop.GuardAgainstRestart(outcome.Service, backend);
        }
        var failed = outcomes.Where(o => !o.Down).ToArray();
        if (failed.Length != 0)
            throw new AggregateException("Service shutdown failed. " + string.Join("; ", failed.Select(o => o.Message)),
                failed.Select(o => new InvalidOperationException(o.Message)));
        TraceLog.W("Stop Services: every Tlachihuani service is down");
    }

    // One service's stop never stops the others: a failure becomes that service's outcome.
    private static async Task<StopOutcome> Guarded(string name, Func<Task<StopOutcome>> stop)
    {
        try { return await stop(); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            TraceLog.E($"Stop Services could not stop {name}", ex);
            return new(name, "failed", $"{name}: {ex.Message}");
        }
    }
}
