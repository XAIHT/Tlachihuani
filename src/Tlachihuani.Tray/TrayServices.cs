// Tlachihuani — Angela López Mendoza · XAIHT
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Tlachihuani.Client;
using Tlachihuani.Core;
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
        return await ServiceReadiness.WaitAsync(ObserveAsync, TimeSpan.FromSeconds(40), token);
    }

    private async Task ChangePrimaryAsync(string action, CancellationToken token)
    {
        var reply = await primary.ExecuteAsync(action, token);
        if (!reply.Ok) throw new ServiceReadinessException(reply.Error?.Message ?? $"Tlachihuani could not {action}.", new(reply, null, null), []);
    }

    internal async Task StopAsync(CancellationToken token)
    {
        // Jobs checkpoints active transfers, then closes the kill-on-close Windows Job Object
        // containing its worker and web facade. Always also try the other independent service.
        var errors = new List<Exception>();
        try
        {
            if (ScmBackend.ForRemoteController().Query().State is not ("stopped" or "not_installed"))
                await RcLifecycle.ExecuteAsync("stop", token);
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is not OutOfMemoryException)
        { errors.Add(new InvalidOperationException("Remote-Controller: " + ex.Message, ex)); }
        foreach (var name in new[] { JobsEndpoint.ServiceName, Contracts.Protocol.ServiceName })
        {
            token.ThrowIfCancellationRequested();
            try { await StopHostAsync(name == JobsEndpoint.ServiceName, token); }
            catch (Exception ex) when (!token.IsCancellationRequested && ex is not OutOfMemoryException)
            { errors.Add(new InvalidOperationException($"{name}: {ex.Message}", ex)); }
        }
        if (errors.Count != 0) throw new AggregateException("Service shutdown failed. " + string.Join("; ", errors.Select(e => e.Message)), errors);
    }

    private async Task StopHostAsync(bool stopJobs, CancellationToken token)
    {
        var scm = stopJobs ? jobs : new ScmBackend();
        var before = scm.Query();
        if (before.State is "stopped" or "not_installed") return;
        string expected;
        if (stopJobs)
        {
            scm.ValidateJobsInstallation();
            expected = JobsInstallation.Image;
        }
        else
        {
            var install = Installation.Load();
            scm.ValidateInstallation(install);
            expected = install.ServicePath;
        }
        using var host = ObserveHost(before.Pid, expected);
        if (stopJobs) await new Lifecycle(scm).ExecuteAsync("stop", TimeSpan.FromSeconds(40), token);
        else await ChangePrimaryAsync("stop", token);
        // SCM can report Stopped just before the executable exits. Keep the original identity
        // for observation only. Neither a process name nor a recycled PID is a kill target.
        using var exit = CancellationTokenSource.CreateLinkedTokenSource(token);
        exit.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            while (host is not null ? !host.HasExited : PidPresent(before.Pid))
                await Task.Delay(100, exit.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("Windows reported Stopped, but process exit could not be confirmed."); }
        if (scm.Query().State != "stopped") throw new InvalidOperationException("The service state changed during shutdown.");
    }

    private static ProcessIdentity? ObserveHost(int pid, string expected)
    {
        if (pid == 0) return null;
        ProcessIdentity identity;
        try { identity = ProcessIdentity.Open(pid); }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 5 or 87) { return null; }
        if (string.Equals(identity.ImagePath, expected, StringComparison.OrdinalIgnoreCase)) return identity;
        identity.Dispose();
        throw new InvalidOperationException("The service process no longer matches the installation.");
    }

    private static bool PidPresent(int pid)
    {
        if (pid == 0) return false;
        try { using var process = Process.GetProcessById(pid); return true; }
        catch (ArgumentException) { return false; }
    }
}
