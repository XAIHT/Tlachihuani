// HandGI expert window (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (repaints and polls the engine every 15 ms; its actions are written to Tlachihuani.log through the HandGI log bridge)
using HandGI.App.Controls;
using HandGI.Core.Engine;
using HandGI.Core.Gestures;

namespace HandGI.App.Stages;

/// <summary>Stage 3: try the gestures live, in safe (dry-run) mode or armed, with latency numbers and tunable rules.</summary>
internal sealed class TesterStage : StageBase
{
    private const int LogLimit = 300;

    private readonly Action _toggleArmed;
    private readonly Button _arm;
    private readonly WrapLabel _armNote = new("");
    private readonly Label _gesture = new() { Font = Theme.Big, AutoSize = true, ForeColor = Theme.Muted, Text = "No hand", Margin = new Padding(0, 2, 0, 2) };
    private readonly WrapLabel _detail = new("");
    private readonly ProbabilityBars _bars = new();
    private readonly Label _latency = new() { Font = Theme.Mono, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 4, 0, 4) };
    private readonly WrapLabel _latencyNote = new("Capture → decision is measured from the camera's own timestamp to the moment HandGI decided. The volume change itself follows within about a millisecond.");
    private readonly NumericUpDown _smoothing = Ui.Number(0.05m, 1m, 0.55m, 0.05m, 2);
    private readonly NumericUpDown _enter = Ui.Number(0.30m, 0.99m, 0.80m, 0.05m, 2);
    private readonly NumericUpDown _exit = Ui.Number(0.10m, 0.95m, 0.50m, 0.05m, 2);
    private readonly NumericUpDown _frames = Ui.Number(1, 10, 2, 1);
    private readonly NumericUpDown _repeat = Ui.Number(80, 2000, 250, 10);
    private readonly NumericUpDown _step = Ui.Number(1, 20, 4, 1);
    private readonly NumericUpDown _accelStep = Ui.Number(1, 30, 6, 1);
    private readonly NumericUpDown _cooldown = Ui.Number(0, 2000, 150, 10);
    private readonly ListBox _log;
    private bool _updating;

    public TesterStage(AppController app, Action toggleArmed)
        : base(app, "Tester", "Try your gestures. In SAFE mode HandGI only reports what it would do; ARMED, it changes the Windows volume. Ctrl+Alt+H switches between them from any application.")
    {
        _toggleArmed = toggleArmed;
        _arm = Ui.Button("SAFE · dry run", (_, _) => _toggleArmed(), primary: false);
        _arm.Font = Theme.Heading;
        _arm.Padding = new Padding(16, 6, 16, 6);

        _log = new ListBox
        {
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            Font = Theme.Mono,
            Height = 200,
            IntegralHeight = false,
            HorizontalScrollbar = true,
            Margin = new Padding(0, 4, 0, 4),
        };

        var live = new Card("Live");
        live.Add(Ui.Row(_arm));
        live.Add(_armNote, stretch: true);
        live.Add(_gesture);
        live.Add(_detail, stretch: true);
        live.Add(_bars, stretch: true);
        Add(live);

        var latency = new Card("Latency");
        latency.Add(_latency, stretch: true);
        latency.Add(_latencyNote, stretch: true);
        Add(latency);

        var rules = new Card("Decision rules");
        rules.Add(Ui.Row(Ui.Field("Smoothing", 110), _smoothing, Ui.Field("Enter frames", 110), _frames));
        rules.Add(Ui.Row(Ui.Field("Enter above", 110), _enter, Ui.Field("Exit below", 110), _exit));
        rules.Add(Ui.Row(Ui.Field("Repeat (ms)", 110), _repeat, Ui.Field("Cool-down (ms)", 110), _cooldown));
        rules.Add(Ui.Row(Ui.Field("Step (%)", 110), _step, Ui.Field("Fast step (%)", 110), _accelStep));
        rules.Add(new WrapLabel("Smoothing: weight of the newest frame. Enter above / for N frames: how sure and how long before a gesture counts. Exit below: when it ends (lower = steadier). Repeat: step interval while the gesture is held; after one second the fast step applies."), stretch: true);
        rules.Add(Ui.Row(Ui.Button("Save rules to the profile", (_, _) => SaveRules())));
        Add(rules);

        var actions = new Card("Actions");
        actions.Add(_log, stretch: true);
        actions.Add(Ui.Row(Ui.Button("Clear", (_, _) => _log.Items.Clear())));
        Add(actions);

        foreach (var n in new[] { _smoothing, _enter, _exit, _frames, _repeat, _step, _accelStep, _cooldown })
            n.ValueChanged += (_, _) => ApplyRules();

        LoadRules();
        UpdateArm();
    }

    private void LoadRules()
    {
        _updating = true;
        try
        {
            var d = App.Profile.Decision;
            _smoothing.Value = Clamp(_smoothing, (decimal)d.Smoothing);
            _enter.Value = Clamp(_enter, (decimal)d.EnterThreshold);
            _exit.Value = Clamp(_exit, (decimal)d.ExitThreshold);
            _frames.Value = Clamp(_frames, d.EnterFrames);
            _repeat.Value = Clamp(_repeat, d.RepeatMs);
            _cooldown.Value = Clamp(_cooldown, d.CooldownMs);
            _step.Value = Clamp(_step, (decimal)Math.Round(d.Step * 100));
            _accelStep.Value = Clamp(_accelStep, (decimal)Math.Round(d.AcceleratedStep * 100));
            _bars.EnterThreshold = d.EnterThreshold;
            _bars.ExitThreshold = d.ExitThreshold;
        }
        finally
        {
            _updating = false;
        }
    }

    private static decimal Clamp(NumericUpDown n, decimal v) => Math.Clamp(v, n.Minimum, n.Maximum);

    private void ApplyRules()
    {
        if (_updating) return;
        var d = App.Profile.Decision;
        d.Smoothing = (float)_smoothing.Value;
        d.EnterThreshold = (float)_enter.Value;
        d.ExitThreshold = (float)Math.Min(_exit.Value, _enter.Value - 0.05m);
        d.EnterFrames = (int)_frames.Value;
        d.RepeatMs = (int)_repeat.Value;
        d.CooldownMs = (int)_cooldown.Value;
        d.Step = (float)_step.Value / 100f;
        d.AcceleratedStep = (float)_accelStep.Value / 100f;
        _bars.EnterThreshold = d.EnterThreshold;
        _bars.ExitThreshold = d.ExitThreshold;
        _bars.Invalidate();
    }

    private void SaveRules()
    {
        try
        {
            string path = App.SaveProfile();
            AddLine($"{DateTime.Now:HH:mm:ss}  rules saved to {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Fail($"The profile could not be saved: {ex.Message}");
        }
    }

    public void UpdateArm()
    {
        bool armed = App.Engine?.Armed == true;
        _arm.Text = armed ? "ARMED · gestures change the volume" : "SAFE · dry run";
        _arm.BackColor = armed ? Theme.Danger : Theme.SurfaceHigh;
        _arm.ForeColor = armed ? Color.White : Theme.Text;
        _arm.FlatAppearance.BorderColor = armed ? Theme.Danger : Theme.Border;
        var p = App.Profile;
        string state = armed ? "Armed: recognised gestures change the system volume now." : "Safe: every action below is a dry run. Arm when the results look right.";
        _armNote.Text = !p.IsUsable
            ? "Every gesture is switched off: hands are tracked but nothing is recognised. Switch gestures on in the Trainer."
            : p.NeedsTraining
                ? $"{state} Your own gestures are not learned yet (Trainer → Learn); Google's gestures already work."
                : state;
        _armNote.ForeColor = !p.IsUsable || p.NeedsTraining ? Theme.Warn : armed ? Theme.Danger : Theme.Muted;
    }

    public override void OnReport(FrameReport r)
    {
        var classes = App.Profile.Classes;
        if (!r.HasHand)
        {
            _gesture.Text = "No hand";
            _gesture.ForeColor = Theme.Muted;
            _detail.Text = r.Detected ? $"Palm detector best score {r.PalmScore:0.00} (needs {App.Settings.PalmThreshold:0.00})." : "";
            _bars.Clear();
            return;
        }
        if (r.ActiveClass >= 0 && r.ActiveClass < classes.Count)
        {
            _gesture.Text = classes[r.ActiveClass].Name;
            _gesture.ForeColor = Theme.ClassColor(r.ActiveClass);
        }
        else
        {
            _gesture.Text = App.Profile.IsUsable ? "Hand (no gesture)" : "Hand tracked";
            _gesture.ForeColor = Theme.Text;
        }
        string google = MediaPipeGestureModel.Labels[ArgMax(r.Ready)];
        _detail.Text = $"Hand presence {r.Presence:0.00} · {(r.Handedness >= 0.5f ? "right" : "left")} hand ({Math.Max(r.Handedness, 1 - r.Handedness):P0}) · " +
                       $"Google sees {(google == "None" ? "no gesture" : google.Replace('_', ' '))} ({r.Ready.Max():P0})" +
                       (App.Profile.IsTrained ? $" · yours {r.OwnShare:P0}" : "") +
                       $" · {r.GpuRuns} GPU run{(r.GpuRuns == 1 ? "" : "s")} this frame{(r.Detected ? " (palm detector + landmarks)" : " (tracking)")}";
        if (App.Profile.IsUsable) _bars.SetValues(r.Smoothed, r.ActiveClass, r.CandidateClass);
    }

    private static int ArgMax(float[] values)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
            if (values[i] > values[best]) best = i;
        return best;
    }

    public override void OnGesture(GestureEvent e)
    {
        if (e.Kind == GestureEventKind.Ended) return;
        string text = e.Kind == GestureEventKind.Started
            ? $"{e.When:HH:mm:ss.fff}  ▶ {e.ClassName}"
            : $"{e.When:HH:mm:ss.fff}    {e.ClassName}: {e.Description}{(e.DryRun ? "  (dry run)" : "")}  · {e.FrameToDecisionMs:0} ms";
        AddLine(text);
    }

    private void AddLine(string text)
    {
        _log.BeginUpdate();
        _log.Items.Insert(0, text);
        while (_log.Items.Count > LogLimit) _log.Items.RemoveAt(_log.Items.Count - 1);
        _log.EndUpdate();
    }

    public override void OnSlowTick()
    {
        UpdateArm();
        var engine = App.Engine;
        if (engine is null || !engine.IsRunning)
        {
            _latency.Text = "The camera is not running.";
            return;
        }
        var l = engine.GetLatency();
        string Ms(double v) => double.IsNaN(v) ? "  -  " : $"{v,5:0.0}";
        string first = l.CameraClock ? "Capture → decision" : "Arrival → decision";
        _latency.Text =
            $"{first,-20} p50 {Ms(l.P50)} ms   p95 {Ms(l.P95)} ms   p99 {Ms(l.P99)} ms\n" +
            $"{"Arrival → decision",-20} p50 {Ms(l.ArrivalP50)} ms   p95 {Ms(l.ArrivalP95)} ms\n" +
            $"{"GPU per frame",-20} p50 {Ms(l.GpuP50)} ms   p95 {Ms(l.GpuP95)} ms\n" +
            $"{"Frames",-20} {l.Frames} at {l.Fps:0.0} fps · {l.Replaced} skipped for a newer frame" +
            (engine.CpuDecode ? "\nThis camera mode is decoded on the CPU (MJPEG): choose NV12 or YUY2 in Setup for less latency." : "");
    }

    public override void OnProfileChanged()
    {
        _bars.SetNames(App.Profile.Classes.Select(c => c.Name));
        LoadRules();
        UpdateArm();
    }

    public override void OnShown()
    {
        _bars.SetNames(App.Profile.Classes.Select(c => c.Name));
        LoadRules();
        UpdateArm();
        OnSlowTick();
    }
}
