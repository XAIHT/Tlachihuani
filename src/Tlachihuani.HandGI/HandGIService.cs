// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (its pump runs 20 times a second; every decision is written explicitly, and HandGI's own messages arrive through LogBridge)
using System.ComponentModel;
using System.Text.Json.Nodes;
using HandGI.Core.Capture;
using HandGI.Core.Engine;
using HandGI.Core.Gestures;
using HandGI.Core.Util;
using Tlachihuani.Contracts;
using Tlachihuani.Core;
using Tlachihuani.Windows;

namespace HandGI.App;

// The HandGI service: one per signed-in user. It owns the camera and the gesture engine, the
// Ctrl+Alt+H shortcut, the volume display and the pipe XT talks to. Its windows come and go on top
// of it: closing a window never turns the camera off. Only a stop (XT, Stop Services, signing out)
// ends it.
internal sealed class HandGIService : ApplicationContext, IHandGIHost
{
    private readonly AppController app;
    private readonly SynchronizationContext ui;
    private readonly HotkeyWindow shortcut = new();
    private readonly VolumeOsd osd = new();
    private readonly System.Windows.Forms.Timer pump = new() { Interval = 50 };
    private readonly ControlPipe pipe;
    private SettingsForm? settings;
    private MainForm? expert;
    private string problem = "";
    private bool starting;
    private bool exiting;

    internal static int Run(string? page, bool cameraOff = false)
    {
        using var singleton = new Mutex(true, HandGIEndpoint.SingletonName, out bool first);
        if (!first)
        {
            if (page is null) TraceLog.I("HandGI is already running for this user; nothing to start");
            else ForwardShow(page);
            return 0;
        }
        TraceLog.I($"HandGI starts (version {AppController.Version}, process {Environment.ProcessId}{(page is null ? "" : ", window " + page)})");
        // Before anything creates a window handle (the synchronization context below does), or
        // Windows Forms refuses to change how errors in windows are handled.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => TraceLog.E("HandGI met an error in a window and keeps running", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) TraceLog.E("HandGI was stopped by an unexpected error", ex);
        };
        Native.DisableProcessPowerThrottling();
        LegacyImport.Run();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var log = new HandGiLog(null) { MinimumLevel = LogLevel.Debug };
        LogBridge.Attach(log);
        var app = new AppController(log);
        HandGIService service;
        try { service = new HandGIService(app, page, cameraOff); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            TraceLog.E("HandGI could not start", ex);
            app.Shutdown();
            return 1;
        }
        using (service) Application.Run(service);
        singleton.ReleaseMutex();
        return 0;
    }

    // A second launch asks the running HandGI to open the window, then leaves.
    private static void ForwardShow(string page)
    {
        try
        {
            HandGIEndpoint.CallAsync("show", CancellationToken.None, page).GetAwaiter().GetResult();
            TraceLog.I($"HandGI is already running; asked it to open its {page} window");
        }
        catch (Exception ex) when (ex is LifecycleException or TimeoutException or IOException or Win32Exception or ProtocolException or InvalidOperationException)
        {
            TraceLog.W($"HandGI is already running but did not open its {page} window: {ex.Message}");
        }
    }

    private HandGIService(AppController app, string? page, bool cameraOff)
    {
        this.app = app;
        ui = SynchronizationContext.Current ?? throw new InvalidOperationException("HandGI needs a window message loop.");
        shortcut.Pressed += () => SetHandControl(app.Engine?.Armed != true, "Ctrl+Alt+H");
        if (!shortcut.Registered)
            TraceLog.W("Ctrl+Alt+H is already used by another program; hand control can still be paused and resumed in Tlachihuani");
        app.EngineStateChanged += (state, message) => ui.Post(_ => OnEngineState(state, message), null);
        pump.Tick += (_, _) => Pump();
        pump.Start();
        pipe = new ControlPipe(Answer, ui);
        UserProcessRecord.ForThisProcess(AppController.Version).Publish(HandGIEndpoint.RecordPath);
        Microsoft.Win32.SystemEvents.SessionEnded += OnSessionEnded;
        TraceLog.I($"HandGI is running in the background (pipe {HandGIEndpoint.PipeName()}); XT can reach it");
        if (cameraOff) TraceLog.I("HandGI started with the camera off (--camera-off); Camera settings turn it on");
        else ui.Post(async _ => await StartCameraAsync(), null);
        if (page is not null) ui.Post(_ => Show(page), null);
    }

    public AppController App => app;

    // Turns the camera on with the saved camera (or the first one), in the mode it remembers.
    public async Task StartCameraAsync()
    {
        if (starting || exiting) return;
        starting = true;
        try
        {
            problem = "";
            app.RefreshGpus();
            await app.RefreshCamerasAsync();
            if (!app.EnsureEngine()) { Problem("HandGI's files are incomplete: " + app.ModelProblem); return; }
            var camera = app.SavedCamera();
            if (camera is null)
            {
                Problem("No camera was found. Connect a camera, then start HandGI again (Windows must allow desktop apps to use the camera).");
                return;
            }
            var (adapter, _, note) = app.ResolveAdapter();
            if (adapter == AppController.NoAdapter) { Problem(note); return; }
            var formats = await CameraCatalog.ListFormatsAsync(camera);
            var format = formats.FirstOrDefault(f => f.Key == app.Settings.CameraFormatKey) ?? CameraFormat.Recommend(formats);
            app.Engine!.Armed = app.Settings.StartArmed;
            TraceLog.I($"HandGI turns the camera on: {camera.DisplayName}; hand control {(app.Engine.Armed ? "on" : "paused")}");
            await app.StartAsync(camera, format);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Problem("The camera could not be turned on: " + ex.Message);
            TraceLog.E("HandGI could not turn the camera on", ex);
        }
        finally { starting = false; settings?.RefreshState(); }
    }

    public async Task StopCameraAsync()
    {
        TraceLog.I("HandGI turns the camera off (HandGI keeps running)");
        try { await app.StopAsync(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { TraceLog.E("HandGI could not turn the camera off", ex); }
        settings?.RefreshState();
    }

    private void Problem(string text)
    {
        problem = text;
        TraceLog.W("HandGI: " + text);
    }

    public bool HandControlOn => app.Engine?.Armed == true;

    // Hand control on: recognised signs act on Windows. Paused: HandGI watches and shows what it
    // sees, and changes nothing.
    public void SetHandControl(bool on, string source)
    {
        var engine = app.Engine;
        if (engine is null)
        {
            TraceLog.W($"Hand control cannot be turned {(on ? "on" : "off")} ({source}): HandGI is not ready");
            return;
        }
        engine.Armed = on;
        TraceLog.I($"HandGI hand control {(on ? "on" : "paused")} ({source})");
        if (app.Settings.ShowOsd) osd.ShowLevel(app.Volume.Level, app.Volume.Muted, on ? "Hand control on" : "Hand control paused");
        settings?.RefreshState();
    }

    internal void Show(string page)
    {
        if (exiting) return;
        if (!HandGIEndpoint.Pages.Contains(page)) page = "signs";
        if (page == "expert") { ShowExpert(); return; }
        if (settings is null || settings.IsDisposed)
        {
            settings = new SettingsForm(this);
            settings.FormClosed += (_, _) => { settings = null; TraceLog.I("HandGI's window closed; HandGI keeps running"); };
        }
        settings.ShowPage(page);
        Bring(settings);
        TraceLog.I($"HandGI shows its {page} window");
    }

    public void ShowExpert()
    {
        if (expert is null || expert.IsDisposed)
        {
            expert = new MainForm(app, () => SetHandControl(!HandControlOn, "expert view"));
            expert.FormClosed += (_, _) => { expert = null; TraceLog.I("HandGI's expert view closed; HandGI keeps running"); };
        }
        Bring(expert);
        TraceLog.I("HandGI shows its expert view");
    }

    private static void Bring(Form form)
    {
        if (!form.Visible) form.Show();
        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        form.Activate();
        form.BringToFront();
    }

    // XT's requests, answered on the window thread.
    private JsonObject Answer(JsonObject request)
    {
        string op = (string?)request["op"] ?? "";
        if (op != "status") TraceLog.I($"XT asks HandGI to {op}{(request["page"] is null ? "" : " " + (string?)request["page"])}");
        switch (op)
        {
            case "status": return Status();
            case "arm": SetHandControl(true, "Tlachihuani"); return Status();
            case "disarm": SetHandControl(false, "Tlachihuani"); return Status();
            case "show": Show((string?)request["page"] ?? "signs"); return Status();
            case "stop":
                ui.Post(_ => ExitThread(), null);
                return Status(stopping: true);
            default:
                return new JsonObject { ["ok"] = false, ["error"] = "HandGI does not know that request." };
        }
    }

    internal JsonObject Status(bool stopping = false)
    {
        var engine = app.Engine;
        var state = engine?.State ?? EngineState.Stopped;
        string word = stopping ? "stopping"
            : state == EngineState.Running ? "watching"
            : state is EngineState.Starting or EngineState.Preparing || starting ? "starting"
            : state == EngineState.Faulted || problem.Length > 0 ? "problem"
            : "camera_off";
        var profile = app.Profile;
        int inUse = profile.Classes.Count(c => c.Enabled && c.Action != GestureAction.None && (c.IsReadyMade || profile.IsTrained));
        return new JsonObject
        {
            ["ok"] = true,
            ["state"] = word,
            ["message"] = word == "problem" ? (problem.Length > 0 ? problem : "The camera stopped unexpectedly.") : "",
            ["hand_control"] = engine?.Armed == true ? "on" : "paused",
            ["camera"] = engine?.IsRunning == true ? engine.ActiveCameraText() : "",
            ["signs_in_use"] = inUse,
            ["own_signs"] = profile.MineCount,
            ["needs_learning"] = profile.NeedsTraining,
            ["shortcut"] = shortcut.Registered,
            ["start_with_services"] = app.Settings.StartWithServices,
            ["version"] = AppController.Version,
        };
    }

    public string ProblemText => problem;

    public bool Starting => starting;

    private void OnEngineState(EngineState state, string message)
    {
        if (state == EngineState.Running) problem = "";
        if (state == EngineState.Faulted) problem = message;
        settings?.OnEngineState(state, message);
    }

    // One consumer for the engine's gesture events: the volume display and every open window.
    private void Pump()
    {
        var engine = app.Engine;
        if (engine is null || !engine.IsRunning) return;
        engine.PreviewWanted = Watching(settings) || Watching(expert);
        while (engine.TryDequeueEvent(out var ev))
        {
            if (ev.Kind == GestureEventKind.Action && !ev.DryRun && app.Settings.ShowOsd)
            {
                bool muted = ev.Action == GestureAction.MuteToggle ? ev.Description == "Mute" : app.Volume.Muted;
                osd.ShowLevel(ev.Volume, muted, $"{ev.ClassName} · {ev.Description}");
            }
            settings?.OnGesture(ev);
            expert?.OnGesture(ev);
        }
    }

    private static bool Watching(Form? form) => form is { IsDisposed: false, Visible: true } && form.WindowState != FormWindowState.Minimized;

    private void OnSessionEnded(object? sender, Microsoft.Win32.SessionEndedEventArgs e)
    {
        TraceLog.I($"Windows is ending the session ({e.Reason}); HandGI stops");
        ui.Post(_ => ExitThread(), null);
    }

    protected override void ExitThreadCore()
    {
        if (!exiting)
        {
            exiting = true;
            TraceLog.I("HandGI is stopping");
            Microsoft.Win32.SystemEvents.SessionEnded -= OnSessionEnded;
            pump.Stop();
            pipe.Dispose();
            shortcut.Dispose();
            settings?.Close();
            expert?.Close();
            osd.Close();
            app.Shutdown();
            UserProcessRecord.Clear(HandGIEndpoint.RecordPath, Environment.ProcessId);
            TraceLog.I("HandGI stopped");
        }
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pump.Dispose();
            osd.Dispose();
        }
        base.Dispose(disposing);
    }
}
