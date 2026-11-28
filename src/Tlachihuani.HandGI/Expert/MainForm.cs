// HandGI expert window (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (repaints and polls the engine every 15 ms; its actions are written to Tlachihuani.log through the HandGI log bridge)
using HandGI.App.Controls;
using HandGI.App.Stages;
using HandGI.Core.Engine;
using HandGI.Core.Gestures;
using HandGI.Core.Util;

namespace HandGI.App;

/// <summary>
/// The expert view: header with live status, three stages (Setup, Trainer, Tester), the GPU preview,
/// and a 15 ms UI timer that polls the engine (the engine never waits for the window). The HandGI
/// service owns the engine, the Ctrl+Alt+H shortcut and the volume display; closing this window
/// leaves the camera running.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly AppController _app;
    private readonly Action _toggleArmed;
    private readonly Action<LogLevel, string> _onLog;
    private readonly Action<EngineState, string> _onEngineState;
    private readonly PreviewView _preview = new() { Dock = DockStyle.Fill };
    private readonly Panel _stageHost = new() { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(14, 4, 6, 6) };
    private readonly SetupStage _setup;
    private readonly TrainerStage _trainer;
    private readonly TesterStage _tester;
    private readonly NavButton _navSetup = new("1", "Setup");
    private readonly NavButton _navTrainer = new("2", "Trainer");
    private readonly NavButton _navTester = new("3", "Tester");
    private readonly Label _gpuChip = Chip();
    private readonly Label _cameraChip = Chip();
    private readonly Label _stateChip = Chip();
    private readonly Label _volumeChip = Chip();
    private readonly Button _armButton;
    private readonly Label _status = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = Theme.Small, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Label _latency = new() { Dock = DockStyle.Right, Width = 560, ForeColor = Theme.Text, Font = Theme.Small, TextAlign = ContentAlignment.MiddleRight };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly FrameReport _report = new();
    private StageBase? _current;
    private long _reportSeq;
    private long _previewSeq;
    private int _tick;
    private volatile string _lastLog = "";

    public MainForm(AppController app, Action toggleArmed)
    {
        _app = app;
        _toggleArmed = toggleArmed;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"HandGI {AppController.Version} · expert view";
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1440, 900);
        MinimumSize = new Size(1080, 700);

        _armButton = Ui.Button("SAFE · dry run", (_, _) => _toggleArmed());
        _setup = new SetupStage(app);
        _trainer = new TrainerStage(app, _preview);
        _tester = new TesterStage(app, toggleArmed);
        BuildLayout();
        ResumeLayout(true);

        _navSetup.Click += (_, _) => ShowStage(_setup);
        _navTrainer.Click += (_, _) => ShowStage(_trainer);
        _navTester.Click += (_, _) => ShowStage(_tester);
        _timer.Tick += OnTick;
        // The controller outlives this window: these handlers are removed when it closes.
        _onLog = (_, message) => _lastLog = message;
        _onEngineState = (state, message) =>
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => OnEngineState(state, message)));
        };
        _app.Log.Logged += _onLog;
        _app.EngineStateChanged += _onEngineState;
        _app.ProfileChanged += OnProfileChanged;
        _preview.SetClassNames(app.Profile.Classes.Select(c => c.Name));
        _preview.Placeholder = "Starting the camera...";
    }

    private static Label Chip() => new()
    {
        AutoSize = true,
        BackColor = Theme.SurfaceHigh,
        ForeColor = Theme.Text,
        Font = Theme.Small,
        Padding = new Padding(10, 6, 10, 6),
        Margin = new Padding(0, 4, 8, 0),
        Text = "...",
    };

    private void BuildLayout()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Theme.Surface };
        var title = new Label { Text = "HandGI", Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true, Location = new Point(18, 6) };
        var subtitle = new Label
        {
            Text = "Hand gestures → system volume · runs on the GPU · expert view of Tlachihuani's HandGI service",
            Font = Theme.Small,
            ForeColor = Theme.Muted,
            AutoSize = true,
            Location = new Point(21, 40),
        };
        var chips = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 12, 10, 0),
            BackColor = Theme.Surface,
        };
        _armButton.Margin = new Padding(4, 3, 4, 0);
        chips.Controls.AddRange(new Control[] { _gpuChip, _cameraChip, _stateChip, _volumeChip, _armButton });
        header.Controls.Add(chips);
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var nav = new Panel { Dock = DockStyle.Left, Width = 176, BackColor = Theme.Surface, Padding = new Padding(0, 12, 0, 0) };
        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 76,
            Text = "Ctrl+Alt+H\narms or disarms\nfrom any app\n(closing this view\nkeeps HandGI on)",
            ForeColor = Theme.Muted,
            Font = Theme.Small,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        nav.Controls.Add(_navTester);
        nav.Controls.Add(_navTrainer);
        nav.Controls.Add(_navSetup);
        nav.Controls.Add(hint);

        var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Theme.Surface, Padding = new Padding(12, 0, 12, 0) };
        statusBar.Controls.Add(_status);
        statusBar.Controls.Add(_latency);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Background,
            Padding = new Padding(12, 12, 0, 8),
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var previewFrame = new Panel { Dock = DockStyle.Fill, BackColor = Theme.PreviewBack, Margin = new Padding(0, 0, 6, 0) };
        previewFrame.Controls.Add(_preview);
        body.Controls.Add(previewFrame, 0, 0);
        body.Controls.Add(_stageHost, 1, 0);

        // Every stage stays in the window (hidden when not selected) so DPI scaling reaches all of them.
        foreach (var stage in new StageBase[] { _setup, _trainer, _tester })
        {
            stage.Visible = false;
            _stageHost.Controls.Add(stage);
        }

        Controls.Add(body);
        Controls.Add(nav);
        Controls.Add(statusBar);
        Controls.Add(header);
    }

    // ------------------------------------------------------------------ lifecycle

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ShowStage(_setup);
        _app.RefreshGpus();
        await _app.RefreshCamerasAsync();
        await _setup.ReloadDevicesAsync();
        _timer.Start();
        SlowTick();
        // The service usually has the camera on already: show its state and do not restart it.
        if (_app.Engine is { } engine && engine.State != EngineState.Stopped)
        {
            OnEngineState(engine.State, "");
            return;
        }
        if (!_app.EnsureEngine())
        {
            _preview.Placeholder = _app.ModelProblem ?? "The hand models are missing.";
            _preview.Invalidate();
            return;
        }
        if (_app.Cameras.Count == 0)
        {
            _preview.Placeholder = "No camera found.";
            _preview.Invalidate();
            return;
        }
        var (adapter, _, note) = _app.ResolveAdapter();
        if (adapter == AppController.NoAdapter)
        {
            _preview.Placeholder = note;
            _preview.Invalidate();
            return;
        }
        await _setup.AutoStartAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The HandGI service owns the camera: closing this view leaves it running.
        _timer.Stop();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _app.Log.Logged -= _onLog;
        _app.EngineStateChanged -= _onEngineState;
        _app.ProfileChanged -= OnProfileChanged;
        base.OnFormClosed(e);
    }

    /// <summary>The HandGI service reads the engine's gesture events once and hands this window its copy.</summary>
    public void OnGesture(GestureEvent gestureEvent) => _tester.OnGesture(gestureEvent);

    // ------------------------------------------------------------------ stages

    private void ShowStage(StageBase stage)
    {
        if (_current == stage) return;
        _stageHost.SuspendLayout();
        foreach (Control c in _stageHost.Controls) c.Visible = c == stage;
        _stageHost.ResumeLayout(true);
        _current = stage;
        _navSetup.Selected = stage == _setup;
        _navTrainer.Selected = stage == _trainer;
        _navTester.Selected = stage == _tester;
        stage.OnShown();
    }

    private void OnProfileChanged()
    {
        _preview.SetClassNames(_app.Profile.Classes.Select(c => c.Name));
        _setup.OnProfileChanged();
        _trainer.OnProfileChanged();
        _tester.OnProfileChanged();
    }

    private void OnEngineState(EngineState state, string message)
    {
        _stateChip.Text = state switch
        {
            EngineState.Running => "● Running",
            EngineState.Preparing => "◌ Preparing GPU",
            EngineState.Starting => "◌ Starting",
            EngineState.Faulted => "✕ Error",
            _ => "○ Stopped",
        };
        _stateChip.ForeColor = state switch
        {
            EngineState.Running => Theme.Accent,
            EngineState.Faulted => Theme.Danger,
            EngineState.Stopped => Theme.Muted,
            _ => Theme.Warn,
        };
        if (state is EngineState.Stopped or EngineState.Faulted)
        {
            _preview.ClearFrame();
            _reportSeq = 0;
            _previewSeq = 0;
        }
        _preview.Placeholder = state == EngineState.Stopped ? "The camera is stopped. Start it in Setup." : message;
        _preview.Invalidate();
        _setup.OnEngineState(state, message);
        _trainer.OnEngineState(state, message);
        _tester.OnEngineState(state, message);
    }

    // ------------------------------------------------------------------ polling

    private void OnTick(object? sender, EventArgs e)
    {
        var engine = _app.Engine;
        if (engine is not null && engine.IsRunning)
        {
            // The service asks for the preview while a window shows it, and hands over the gesture events.
            engine.TryCopyPreview(ref _previewSeq, _preview.UpdateFrame);
            if (engine.TryGetReport(_report, ref _reportSeq))
            {
                _preview.UpdateReport(_report);
                _current?.OnReport(_report);
            }
        }
        if (++_tick % 16 == 0) SlowTick();
    }

    private void SlowTick()
    {
        var (adapter, name, _) = _app.ResolveAdapter();
        _gpuChip.Text = adapter == AppController.NoAdapter ? "GPU: none supported" : adapter < 0 ? "CPU compatibility mode" : $"GPU · {Shorten(name, 28)}";
        _gpuChip.ForeColor = adapter == AppController.NoAdapter ? Theme.Warn : Theme.Text;
        var engine = _app.Engine;
        _cameraChip.Text = engine?.IsRunning == true && engine.ActiveCameraText() is { Length: > 0 } cam ? cam : "Camera off";
        _volumeChip.Text = !_app.Volume.Available ? "No audio device" : _app.Volume.Muted ? "Muted" : $"Volume {_app.Volume.Level:P0}";
        UpdateArm();
        _tester.UpdateArm();

        if (engine is not null && engine.IsRunning)
        {
            var l = engine.GetLatency();
            _latency.Text = double.IsNaN(l.P50)
                ? $"{l.Fps:0.0} fps"
                : $"{l.Fps:0.0} fps   ·   {(l.CameraClock ? "capture" : "arrival")} → decision  p50 {l.P50:0.0} ms   p95 {l.P95:0.0} ms   ·   GPU {l.GpuP50:0.0} ms";
        }
        else
        {
            _latency.Text = "";
        }
        _status.Text = _lastLog;
        _current?.OnSlowTick();
    }

    private void UpdateArm()
    {
        bool armed = _app.Engine?.Armed == true;
        _armButton.Text = armed ? "ARMED" : "SAFE · dry run";
        _armButton.BackColor = armed ? Theme.Danger : Theme.SurfaceHigh;
        _armButton.ForeColor = armed ? Color.White : Theme.Text;
        _armButton.FlatAppearance.BorderColor = armed ? Theme.Danger : Theme.Border;
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

internal static class EngineText
{
    /// <summary>"Camera name · 1280×720 @ 30 fps" without the long details.</summary>
    public static string ActiveCameraText(this GestureEngine engine)
    {
        var parts = engine.CameraDescription.Split(" · ");
        return parts.Length >= 2 ? $"{Shorten(parts[0], 22)} · {parts[1]}" : engine.CameraDescription;
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
