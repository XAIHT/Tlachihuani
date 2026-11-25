// Tlachihuani — Angela López Mendoza · XAIHT
using System.Reflection;
using Tlachihuani.Client;
using Tlachihuani.Contracts;

namespace Tlachihuani.Tray;

internal sealed class TrayContext : ApplicationContext
{
    private readonly Controller controller = new();
    private readonly TrayServices services;
    private readonly SemaphoreSlim serial = new(1);
    private readonly CancellationTokenSource closing = new();
    private readonly NotifyIcon tray;
    private readonly Icon icon;
    private readonly Icon grayIcon;
    private readonly StatusForm form;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 3000 };
    private readonly TrayMenu menu;
    private readonly WindowActivation activation;
    private bool changing;
    private bool exitRequested;
    private string progress = "";
    private bool resourcesDisposed;
    private ControlReply? last;
    private JobsState? jobs;
    private RemoteControllerState? remoteController;

    internal TrayContext(bool show, EventWaitHandle showSignal)
    {
        services = new(controller);
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Tlachihuani.XT.ico")!;
        icon = new Icon(resource);
        grayIcon = TrayIcons.Grayscale(icon);
        form = new StatusForm(icon);
        activation = new WindowActivation(form, showSignal);
        form.Requested += operation => _ = RunAsync(operation);
        form.ExitRequested += RequestExit;
        menu = new TrayMenu(ShowPanel, ShowRemoteController, ShowFlowPills, RunAsync, RequestExit);
        tray = new NotifyIcon { Icon = grayIcon, Text = "Tlachihuani — Checking services", ContextMenuStrip = menu, Visible = true };
        // WinForms NotifyIcon handles Explorer's TaskbarCreated message and adds the icon again.
        tray.DoubleClick += (_, _) => ShowPanel();
        UiTrace.Attach(menu, "tray menu");
        tray.MouseClick += (_, e) => UiTrace.Log($"tray icon clicked ({e.Button})");
        tray.MouseDoubleClick += (_, e) => UiTrace.Log($"tray icon double-clicked ({e.Button})");
        tray.BalloonTipShown += (_, _) => UiTrace.Log($"tray balloon shown: {tray.BalloonTipTitle} {tray.BalloonTipText}");
        tray.BalloonTipClicked += (_, _) => UiTrace.Log("tray balloon clicked");
        logCopier.Start();
        timer.Tick += (_, _) => _ = RunAsync("status"); timer.Start();
        if (show) ShowPanel();
        _ = RunAsync("status");
        _ = Task.Run(() => new RcApplicationBroker().RunAsync(closing.Token));
        TlachihuaniLog.Info("xt", $"XT started (version {Application.ProductVersion.Split('+')[0]}, Windows user {Environment.UserName}).");
        _ = Task.Run(() => XaihtLink.Shared.RunAsync(closing.Token));
    }

    private void ShowPanel() => WindowActivation.Show(form);

    private void ShowRemoteController() => RemoteControllerLauncher.Open(form.Visible ? form : null);

    private void ShowFlowPills() => FlowPillsLauncher.Open(form.Visible ? form : null);

    private void RequestExit()
    {
        // Finish an accepted Stop Services even if Exit is selected while Jobs is draining.
        if (changing) exitRequested = true;
        else ExitThread();
    }

    private async Task RunAsync(string operation)
    {
        // The status check every 3 seconds is written only when something it sees changes;
        // a Start/Stop the user asked for is always written in full.
        using var pass = operation == "status" ? TraceLog.Pass("XT status check") : default;
        if (changing || closing.IsCancellationRequested) return;
        var token = closing.Token;
        bool mutation = operation != "status";
        if (!mutation && !serial.Wait(0)) return;
        if (mutation)
        {
            changing = true;
            progress = operation switch
            {
                "start-services" => "Starting services", "stop-services" => "Stopping services", _ => "Updating service"
            };
            Render();
            // A click during a periodic observation must run after it rather than disappear.
            try { await serial.WaitAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        }
        Exception? error = null;
        var startedAt = DateTimeOffset.Now;
        ServiceSnapshot? observation = null;
        try
        {
            if (mutation)
            {
                try
                {
                    if (operation == "start-services") observation = await Task.Run(() => services.StartAsync(token), token);
                    else if (operation == "stop-services") await Task.Run(() => services.StopAsync(token), token);
                    else
                    {
                        var result = await Task.Run(() => controller.ExecuteAsync(operation, token), token);
                        if (!result.Ok) error = new ServiceReadinessException(result.Error?.Message ?? "The service operation failed.", new(result, null, null), []);
                    }
                }
                catch (Exception ex) when (!token.IsCancellationRequested && ex is not OutOfMemoryException) { error = ex; }
            }
            // Start already returned a confirmed snapshot. Do not replace its verdict
            // with an extra, single short probe that can time out during normal work.
            observation ??= await Task.Run(() => services.ObserveAsync(token), token);
            last = observation.Main; jobs = observation.Jobs; remoteController = observation.RemoteController;
            if (operation == "stop-services" && !Stopped)
                error ??= new InvalidOperationException("Service shutdown could not be confirmed. See the observed SCM states in the report.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            jobs = null;
            remoteController = null;
            if (mutation) error = error is null ? ex : new AggregateException("The service action and its final status check failed.", error, ex);
        }
        finally
        {
            if (mutation) changing = false;
            serial.Release();
            if (!token.IsCancellationRequested) Render();
        }
        if (error is not null && !token.IsCancellationRequested)
            ServiceDialogs.Failure(form, ServiceErrorReport.Create(operation, error, observation, startedAt));
        if (mutation && exitRequested && !token.IsCancellationRequested) ExitThread();
    }

    private bool Ready => new ServiceSnapshot(last, jobs, remoteController).Ready;
    private bool Stopped => new ServiceSnapshot(last, jobs, remoteController).Stopped;

    private void Render()
    {
        menu.UpdateActions(last, Ready, Stopped, changing);
        if (last is not null) form.Render(last, jobs, changing);
        menu.SetState(StatusForm.Pretty(last?.Service?.ScmState ?? "unavailable"));
        tray.Icon = !changing && Ready ? icon : grayIcon;
        var text = changing ? progress : Ready ? remoteController?.Ready == true ? "Services ready · RC available" : "Services ready · RC off"
            : Stopped ? "Services stopped" : jobs is null || last is null ? "Service status unavailable"
            : last.Service?.ScmState != "running" ? "Main service " + StatusForm.Pretty(last.Service?.ScmState ?? "unavailable")
            : jobs.State != "running" ? "Jobs " + StatusForm.Pretty(jobs.State) : !jobs.ApiReady ? "Torrenter API unavailable"
            : "Remote-Controller " + StatusForm.Pretty(remoteController?.State ?? "unavailable");
        tray.Text = "Tlachihuani — " + text;
        var iconName = !changing && Ready ? "color" : "gray";
        if (tray.Text != lastTip || iconName != lastIcon)
        {
            UiTrace.Log($"tray state: \"{tray.Text}\", icon {iconName}; main service {last?.Service?.ScmState ?? "unknown"}, " +
                $"Jobs {jobs?.State ?? "unknown"} (API {(jobs?.ApiReady == true ? "ready" : "not ready")}), " +
                $"Remote-Controller {remoteController?.State ?? "unknown"} (ready {remoteController?.Ready.ToString() ?? "unknown"}), " +
                $"ready {Ready}, stopped {Stopped}, changing {changing}");
            lastTip = tray.Text; lastIcon = iconName;
        }
    }
    private string lastTip = "", lastIcon = "";
    private readonly ServiceLogCopier logCopier = new();

    protected override void ExitThreadCore()
    {
        closing.Cancel(); timer.Stop(); tray.Visible = false; form.AllowExit = true; form.Close();
        logCopier.Dispose();
        TlachihuaniLog.Info("xt", "XT exited.");
        TraceLog.Flush(TimeSpan.FromSeconds(3));
        base.ExitThreadCore();
    }
    protected override void Dispose(bool disposing)
    {
        // Application.Run disposes its context; the caller's using scope can dispose it again.
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            closing.Cancel(); timer.Dispose(); activation.Dispose(); tray.Dispose(); menu.Dispose(); form.Dispose(); grayIcon.Dispose(); icon.Dispose(); closing.Dispose();
        }
        base.Dispose(disposing);
    }
}
