// HandGI expert window (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (repaints and polls the engine every 15 ms; its actions are written to Tlachihuani.log through the HandGI log bridge)
using HandGI.Core.Capture;
using HandGI.Core.Engine;
using HandGI.Core.Gpu;
using HandGI.Core.SelfTest;
using HandGI.Core.Vision;

namespace HandGI.App.Stages;

/// <summary>Stage 1: camera, capture mode, GPU, models, self-test.</summary>
internal sealed class SetupStage : StageBase
{
    private sealed record FormatItem(CameraFormat Format, bool Recommended)
    {
        public override string ToString() => Format + (Recommended ? "    ★ recommended" : "");
    }

    private sealed record AdapterItem(int Index, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record Choice<T>(T Value, string Text)
    {
        public override string ToString() => Text;
    }

    private readonly ComboBox _camera = Ui.Combo(340);
    private readonly ComboBox _format = Ui.Combo(340);
    private readonly ComboBox _range = Ui.Combo(340);
    private readonly ComboBox _gpu = Ui.Combo(340);
    private readonly ComboBox _scheduling = Ui.Combo(340);
    private readonly CheckBox _mirror;
    private readonly CheckBox _preview;
    private readonly CheckBox _shared;
    private readonly CheckBox _osd;
    private readonly WrapLabel _cameraNote = new("");
    private readonly WrapLabel _gpuNote = new("");
    private readonly WrapLabel _modelNote = new("");
    private readonly WrapLabel _engineNote = new("Not started.", Theme.Text, Theme.Body);
    private readonly Button _start;
    private readonly Button _stop;
    private readonly Button _download;
    private readonly Button _selfTest;
    private readonly RichTextBox _results;
    private IReadOnlyList<CameraFormat> _formats = Array.Empty<CameraFormat>();
    private bool _loading;
    private bool _busy;
    private bool _needsRestart;

    public SetupStage(AppController app)
        : base(app, "Setup", "Pick the camera and the GPU. HandGI hands the camera's raw frames to the GPU untouched: colour conversion, cropping, both neural networks and the gesture classifier run there.")
    {
        var s = app.Settings;
        _mirror = Ui.Check("Mirror (selfie view)", s.Mirror);
        _preview = Ui.Check("Show preview", s.Preview);
        _shared = Ui.Check("Share the camera with other apps", s.SharedCamera);
        _osd = Ui.Check("On-screen volume display", s.ShowOsd);

        _range.Items.Add(new Choice<YuvRange>(YuvRange.Limited, "Limited 16-235 (almost every webcam)"));
        _range.Items.Add(new Choice<YuvRange>(YuvRange.Full, "Full 0-255"));
        _range.SelectedIndex = s.Range == YuvRange.Full ? 1 : 0;

        _scheduling.Items.Add(new Choice<SchedulingMode>(SchedulingMode.Auto, "Automatic (decided from this machine)"));
        _scheduling.Items.Add(new Choice<SchedulingMode>(SchedulingMode.Realtime, "Real-time: MMCSS + above-normal priority"));
        _scheduling.Items.Add(new Choice<SchedulingMode>(SchedulingMode.Normal, "Normal priority"));
        _scheduling.SelectedIndex = Math.Clamp((int)s.Scheduling, 0, 2);

        _start = Ui.Button("Start camera", async (_, _) => await StartAsync(), primary: true);
        _stop = Ui.Button("Stop", async (_, _) => await StopAsync());
        _download = Ui.Button("Download models", async (_, _) => await DownloadAsync());
        _selfTest = Ui.Button("Run self-test", async (_, _) => await SelfTestAsync());

        _results = new RichTextBox
        {
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            Font = Theme.Mono,
            Height = 240,
            DetectUrls = false,
            WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Margin = new Padding(0, 6, 0, 2),
            Text = "The self-test proves every operator runs on the GPU, compares the GPU sampler with a CPU reference, times the hand pipeline and trains a small model on the GPU.",
        };

        var camera = new Card("Camera");
        camera.Add(Ui.Row(Ui.Field("Camera"), _camera, Ui.Button("Refresh", async (_, _) => await RefreshCamerasAsync())));
        camera.Add(Ui.Row(Ui.Field("Mode"), _format));
        camera.Add(Ui.Row(Ui.Field("Colour range"), _range));
        camera.Add(Ui.Row(_mirror, _preview, _shared, _osd));
        camera.Add(_cameraNote, stretch: true);
        Add(camera);

        var gpu = new Card("GPU");
        gpu.Add(Ui.Row(Ui.Field("Device"), _gpu));
        gpu.Add(Ui.Row(Ui.Field("Scheduling"), _scheduling));
        gpu.Add(_gpuNote, stretch: true);
        Add(gpu);

        var models = new Card("Models");
        models.Add(_modelNote, stretch: true);
        models.Add(Ui.Row(_download));
        Add(models);

        var run = new Card("Run");
        run.Add(Ui.Row(_start, _stop, _selfTest));
        run.Add(_engineNote, stretch: true);
        run.Add(_results, stretch: true);
        Add(run);

        _camera.SelectedIndexChanged += async (_, _) =>
        {
            if (_loading) return;
            await LoadFormatsAsync();
            await RestartIfRunningAsync();
        };
        _format.SelectedIndexChanged += async (_, _) =>
        {
            if (_loading) return;
            UpdateCameraNote();
            await RestartIfRunningAsync();
        };
        _range.SelectedIndexChanged += (_, _) =>
        {
            if (_range.SelectedItem is Choice<YuvRange> c) App.Settings.Range = c.Value;
            SettingNeedsRestart();
        };
        _mirror.CheckedChanged += (_, _) =>
        {
            App.Settings.Mirror = _mirror.Checked;
            App.Engine?.SetMirror(_mirror.Checked);
            App.Settings.Save(App.Log);
        };
        _preview.CheckedChanged += (_, _) =>
        {
            App.Settings.Preview = _preview.Checked;
            SettingNeedsRestart();
        };
        _shared.CheckedChanged += (_, _) =>
        {
            App.Settings.SharedCamera = _shared.Checked;
            SettingNeedsRestart();
        };
        _osd.CheckedChanged += (_, _) =>
        {
            App.Settings.ShowOsd = _osd.Checked;
            App.Settings.Save(App.Log);
        };
        _gpu.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _gpu.SelectedItem is not AdapterItem item) return;
            App.Settings.Adapter = item.Index;
            App.Settings.AdapterName = item.Text;
            UpdateGpuNote();
            SettingNeedsRestart();
        };
        _scheduling.SelectedIndexChanged += (_, _) =>
        {
            if (_scheduling.SelectedItem is Choice<SchedulingMode> c) App.Settings.Scheduling = c.Value;
            UpdateGpuNote();
            SettingNeedsRestart();
        };
        UpdateButtons();
    }

    // ------------------------------------------------------------------ devices

    public async Task ReloadDevicesAsync()
    {
        _loading = true;
        try
        {
            _camera.Items.Clear();
            foreach (var c in App.Cameras) _camera.Items.Add(c);
            var saved = App.SavedCamera();
            if (saved is not null) _camera.SelectedItem = saved;

            _gpu.Items.Clear();
            _gpu.Items.Add(new AdapterItem(AppSettings.AutoAdapter, "Automatic (best supported NVIDIA GPU)"));
            foreach (var g in App.Gpus)
            {
                var (ok, _) = g.CheckSupport();
                _gpu.Items.Add(new AdapterItem(g.DxgiIndex, $"[{g.DxgiIndex}] {g.Name} · {g.VramText}{(ok ? "" : "   (not supported)")}"));
            }
            _gpu.Items.Add(new AdapterItem(OrtFactory.CpuAdapter, "CPU compatibility mode (slow, not GPU-exclusive)"));
            int index = 0;
            for (int i = 0; i < _gpu.Items.Count; i++)
                if (_gpu.Items[i] is AdapterItem a && a.Index == App.Settings.Adapter) index = i;
            _gpu.SelectedIndex = index;
        }
        finally
        {
            _loading = false;
        }
        UpdateGpuNote();
        UpdateModelNote();
        await LoadFormatsAsync();
        if (App.Cameras.Count == 0) _cameraNote.Text = "No camera found. Connect one and press Refresh. (Settings → Privacy & security → Camera must allow desktop apps.)";
    }

    private async Task RefreshCamerasAsync()
    {
        await App.RefreshCamerasAsync();
        App.RefreshGpus();
        await ReloadDevicesAsync();
    }

    private async Task LoadFormatsAsync()
    {
        if (_camera.SelectedItem is not CameraInfo camera)
        {
            _format.Items.Clear();
            return;
        }
        _loading = true;
        try
        {
            _formats = await CameraCatalog.ListFormatsAsync(camera);
            var recommended = CameraFormat.Recommend(_formats);
            _format.Items.Clear();
            foreach (var f in _formats) _format.Items.Add(new FormatItem(f, f == recommended));
            var wanted = camera.GroupId == App.Settings.CameraGroupId
                ? _formats.FirstOrDefault(f => f.Key == App.Settings.CameraFormatKey) ?? recommended
                : recommended;
            for (int i = 0; i < _format.Items.Count; i++)
                if (_format.Items[i] is FormatItem item && item.Format == wanted) _format.SelectedIndex = i;
            if (_format.SelectedIndex < 0 && _format.Items.Count > 0) _format.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            _cameraNote.Text = $"Could not read the camera's modes: {ex.Message}";
            App.Log.Warn(_cameraNote.Text);
        }
        finally
        {
            _loading = false;
        }
        UpdateCameraNote();
    }

    // ------------------------------------------------------------------ run

    public async Task AutoStartAsync()
    {
        if (App.Cameras.Count == 0 || App.ModelProblem is not null) return;
        if (App.ResolveAdapter().Adapter == AppController.NoAdapter) return;
        await StartAsync();
    }

    public async Task StartAsync()
    {
        if (_busy) return;
        if (_camera.SelectedItem is not CameraInfo camera)
        {
            _engineNote.Text = "No camera is selected.";
            return;
        }
        var format = (_format.SelectedItem as FormatItem)?.Format;
        _busy = true;
        UpdateButtons();
        try
        {
            _engineNote.ForeColor = Theme.Text;
            _engineNote.Text = "Starting...";
            await App.StartAsync(camera, format);
            _needsRestart = false;
            UpdateCameraNote();
        }
        catch (Exception ex)
        {
            _engineNote.ForeColor = Theme.Danger;
            _engineNote.Text = ex.Message;
            App.Log.Error("Start failed", ex);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    private async Task StopAsync()
    {
        if (_busy) return;
        _busy = true;
        UpdateButtons();
        try
        {
            await App.StopAsync();
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    private async Task RestartIfRunningAsync()
    {
        if (App.Engine?.IsRunning == true) await StartAsync();
    }

    private void SettingNeedsRestart()
    {
        App.Settings.Save(App.Log);
        if (App.Engine?.IsRunning == true) _needsRestart = true;
        UpdateCameraNote();
        UpdateButtons();
    }

    private async Task DownloadAsync()
    {
        if (_busy) return;
        _busy = true;
        UpdateButtons();
        try
        {
            foreach (var model in ModelCatalog.All)
            {
                if (ModelCatalog.Check(model).Verified) continue;
                var progress = new Progress<(long Done, long Total)>(p =>
                    _modelNote.Text = $"Downloading {model.FileName}: {p.Done / 1048576.0:0.0} of {p.Total / 1048576.0:0.0} MB...");
                await ModelCatalog.DownloadAsync(model, progress, CancellationToken.None);
                App.Log.Info($"Downloaded and verified {model.FileName}.");
            }
            App.EnsureEngine();
        }
        catch (Exception ex)
        {
            Fail($"The download failed: {ex.Message}");
        }
        finally
        {
            _busy = false;
            UpdateModelNote();
            UpdateButtons();
        }
    }

    private async Task SelfTestAsync()
    {
        if (_busy) return;
        _busy = true;
        UpdateButtons();
        _results.Clear();
        Append($"Self-test started {DateTime.Now:HH:mm:ss}\n", Theme.Muted);
        var progress = new Progress<SelfTestResult>(r =>
        {
            var color = r.Outcome switch
            {
                SelfTestOutcome.Pass => Theme.Accent,
                SelfTestOutcome.Warn => Theme.Warn,
                SelfTestOutcome.Fail => Theme.Danger,
                _ => Theme.Muted,
            };
            Append($"{r.Badge}  {r.Name}  ({r.Ms:0} ms)\n", color);
            Append($"      {r.Detail}\n", Theme.Text);
        });
        try
        {
            var options = new SelfTestOptions { Adapter = App.Settings.Adapter, AllowCpuFallback = App.Settings.AllowCpuFallback };
            var results = await SelfTests.RunAsync(options, App.Log, progress);
            int failed = results.Count(r => r.Outcome == SelfTestOutcome.Fail);
            int warned = results.Count(r => r.Outcome == SelfTestOutcome.Warn);
            int passed = results.Count(r => r.Outcome == SelfTestOutcome.Pass);
            await Task.Yield();
            Append($"\n{passed} passed · {warned} warnings · {failed} failed\n", failed == 0 ? Theme.Accent : Theme.Danger);
        }
        catch (Exception ex)
        {
            Append($"The self-test stopped: {ex.Message}\n", Theme.Danger);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    private void Append(string text, Color color)
    {
        _results.SelectionStart = _results.TextLength;
        _results.SelectionLength = 0;
        _results.SelectionColor = color;
        _results.AppendText(text);
        _results.SelectionColor = _results.ForeColor;
        _results.ScrollToCaret();
    }

    // ------------------------------------------------------------------ notes

    private void UpdateCameraNote()
    {
        var lines = new List<string>();
        if (_format.SelectedItem is FormatItem item)
        {
            lines.Add(item.Format.Uncompressed
                ? $"{item.Format.Subtype} is uncompressed: the GPU reads it directly, the CPU only copies one buffer per frame."
                : $"{item.Format.Subtype} is compressed: Windows decodes it on the CPU before HandGI sees it (more latency and CPU load). Prefer NV12 or YUY2.");
        }
        var engine = App.Engine;
        if (engine?.IsRunning == true && engine.CameraDescription.Length > 0) lines.Add($"Running: {engine.CameraDescription}");
        if (App.PreviewDecision.Length > 0) lines.Add(App.PreviewDecision);
        if (_needsRestart) lines.Add("Press Restart camera to apply the changed settings.");
        _cameraNote.Text = string.Join("\n", lines);
    }

    private void UpdateGpuNote()
    {
        var (adapter, _, note) = App.ResolveAdapter();
        var decision = SchedulingPolicy.Decide(App.Settings.Scheduling, Environment.ProcessorCount);
        string fallback = adapter < 0
            ? "Every operation runs on the CPU."
            : App.Settings.AllowCpuFallback
                ? "CPU fallback is allowed (set in settings.json)."
                : "CPU fallback is disabled: if any operation could not run on the GPU, HandGI refuses to start instead of silently using the CPU.";
        _gpuNote.ForeColor = adapter == AppController.NoAdapter ? Theme.Warn : Theme.Muted;
        _gpuNote.Text = $"{note}\nScheduling: {decision.Reason}\n{fallback}";
    }

    private void UpdateModelNote()
    {
        var lines = new List<string>();
        bool missing = false;
        foreach (var model in ModelCatalog.All)
        {
            var status = ModelCatalog.Check(model);
            missing |= !status.Verified;
            lines.Add($"{(status.Verified ? "✓" : "✗")}  {model.Title}: {status.Message}");
        }
        lines.Add("MediaPipe hand models (Apache-2.0, OpenCV Zoo ONNX exports) and Google's MediaPipe gesture recognizer (Apache-2.0). HandGI fuses its own GPU pre- and post-processing and Google's gesture networks into one GPU graph when the camera starts.");
        _modelNote.ForeColor = missing ? Theme.Warn : Theme.Muted;
        _modelNote.Text = string.Join("\n", lines);
        _download.Visible = missing;
    }

    private void UpdateButtons()
    {
        bool running = App.Engine?.IsRunning == true;
        _start.Text = running ? "Restart camera" : "Start camera";
        _start.Enabled = !_busy;
        _stop.Enabled = !_busy && running;
        _selfTest.Enabled = !_busy;
        _download.Enabled = !_busy;
    }

    public override void OnShown()
    {
        UpdateButtons();
        UpdateCameraNote();
    }

    public override void OnSlowTick() => UpdateButtons();

    public override void OnEngineState(EngineState state, string message)
    {
        _engineNote.ForeColor = state switch
        {
            EngineState.Faulted => Theme.Danger,
            EngineState.Running => Theme.Accent,
            _ => Theme.Text,
        };
        _engineNote.Text = message;
        UpdateButtons();
        UpdateCameraNote();
    }
}
