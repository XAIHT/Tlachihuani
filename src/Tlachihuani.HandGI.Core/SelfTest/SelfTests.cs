// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using HandGI.Core.Audio;
using HandGI.Core.Engine;
using HandGI.Core.Gestures;
using HandGI.Core.Gpu;
using HandGI.Core.Onnx;
using HandGI.Core.Training;
using HandGI.Core.Util;
using HandGI.Core.Vision;
using Microsoft.ML.OnnxRuntime;

namespace HandGI.Core.SelfTest;

public enum SelfTestOutcome { Pass, Warn, Fail, Skip }

public sealed record SelfTestResult(string Name, SelfTestOutcome Outcome, string Detail, double Ms)
{
    public string Badge => Outcome switch
    {
        SelfTestOutcome.Pass => "PASS",
        SelfTestOutcome.Warn => "WARN",
        SelfTestOutcome.Fail => "FAIL",
        _ => "SKIP",
    };
}

public sealed class SelfTestOptions
{
    /// <summary>-2 = best supported GPU, -1 = CPU compatibility mode, 0.. = DXGI adapter index.</summary>
    public int Adapter { get; set; } = AppSettings.AutoAdapter;
    public bool AllowCpuFallback { get; set; }
    public bool IncludeModels { get; set; } = true;
    public bool IncludeTraining { get; set; } = true;
}

/// <summary>
/// End-to-end checks that prove the GPU path is real: the generated ONNX operators run on DirectML with
/// CPU fallback disabled and match CPU math, the raw-frame sampler matches a CPU reference for every
/// pixel layout, the fused MediaPipe graphs run and are timed, training converges on the GPU and agrees
/// with a CPU forward pass, and the decision rules behave. Nothing here changes the system volume.
/// </summary>
public static class SelfTests
{
    private const int NoDevice = int.MinValue;

    public static Task<IReadOnlyList<SelfTestResult>> RunAsync(SelfTestOptions options, ILog log, IProgress<SelfTestResult>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Run(options, log, progress, cancellationToken), cancellationToken);

    public static IReadOnlyList<SelfTestResult> Run(SelfTestOptions options, ILog log, IProgress<SelfTestResult>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = new List<SelfTestResult>();
        void Add(SelfTestResult r)
        {
            results.Add(r);
            var level = r.Outcome switch
            {
                SelfTestOutcome.Fail => LogLevel.Error,
                SelfTestOutcome.Warn => LogLevel.Warn,
                _ => LogLevel.Info,
            };
            log.Write(level, $"Self-test {r.Badge} {r.Name}: {r.Detail} ({r.Ms:0} ms)");
            progress?.Report(r);
        }

        var (deviceResult, adapter) = SelectDevice(options);
        Add(deviceResult);
        bool device = adapter != NoDevice;
        bool fallback = options.AllowCpuFallback;

        Add(Check("Scheduling policy", ScheduleCheck));
        Add(Check("Palm anchors", AnchorCheck));
        Add(Check("Hand geometry", GeometryCheck));
        Add(Check("Gesture decisions", DeciderCheck));
        Add(Check("Gesture lists: Google's, yours, replaced", ProfileCheck));
        cancellationToken.ThrowIfCancellationRequested();

        if (device)
        {
            Add(Check("ONNX writer + DirectML operators", () => OperatorCheck(adapter, fallback)));
            cancellationToken.ThrowIfCancellationRequested();
            Add(Check("GPU sampler vs CPU reference", () => SamplerCheck(adapter, fallback)));
            cancellationToken.ThrowIfCancellationRequested();
        }
        else
        {
            Add(Skip("ONNX writer + DirectML operators"));
            Add(Skip("GPU sampler vs CPU reference"));
        }

        MediaPipeGestureModel? gestures = null;
        string gestureProblem = "";
        if (options.IncludeModels || options.IncludeTraining)
        {
            try
            {
                gestures = ModelCatalog.LoadGestures();
            }
            catch (Exception ex)
            {
                gestureProblem = ex.Message;
            }
        }
        (SelfTestOutcome, string) NoGestures() => (SelfTestOutcome.Fail, $"Google's gesture model could not be read: {gestureProblem}");

        if (options.IncludeModels)
        {
            Add(Check("Models", ModelCheck));
            cancellationToken.ThrowIfCancellationRequested();
            Add(Check("Google's gestures: HandGI vs Google", () => gestures is null ? NoGestures() : GestureReferenceCheck(gestures)));
            Add(device ? Check("Google's gestures on the GPU", () => gestures is null ? NoGestures() : GestureGpuCheck(gestures, adapter, fallback)) : Skip("Google's gestures on the GPU"));
            cancellationToken.ThrowIfCancellationRequested();
            Add(Check("Your gestures beside Google's", () => gestures is null ? NoGestures() : FusionCheck(gestures)));
            Add(device ? Check("Hand pipeline timing", () => gestures is null ? NoGestures() : PipelineCheck(adapter, fallback, log, gestures)) : Skip("Hand pipeline timing"));
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (options.IncludeTraining)
        {
            Add(device ? Check("Learning your own gestures", () => gestures is null ? NoGestures() : TrainingCheck(adapter, fallback, gestures, cancellationToken)) : Skip("Learning your own gestures"));
            cancellationToken.ThrowIfCancellationRequested();
            Add(device ? Check("Your gestures with either hand", () => gestures is null ? NoGestures() : HandsCheck(adapter, fallback, gestures, cancellationToken)) : Skip("Your gestures with either hand"));
            cancellationToken.ThrowIfCancellationRequested();
        }

        Add(Check("Audio endpoint", () => AudioCheck(log)));
        return results;
    }

    private static SelfTestResult Check(string name, Func<(SelfTestOutcome Outcome, string Detail)> body)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var (outcome, detail) = body();
            return new SelfTestResult(name, outcome, detail, sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SelfTestResult(name, SelfTestOutcome.Fail, $"{ex.GetType().Name}: {ex.Message}", sw.Elapsed.TotalMilliseconds);
        }
    }

    private static SelfTestResult Skip(string name) =>
        new(name, SelfTestOutcome.Skip, "Skipped: no device to run on (see the Device result).", 0);

    // ------------------------------------------------------------------ device

    private static (SelfTestResult Result, int Adapter) SelectDevice(SelfTestOptions options)
    {
        var sw = Stopwatch.StartNew();
        SelfTestResult R(SelfTestOutcome o, string d) => new("Device", o, d, sw.Elapsed.TotalMilliseconds);
        try
        {
            if (options.Adapter == OrtFactory.CpuAdapter)
                return (R(SelfTestOutcome.Warn, "CPU compatibility mode was chosen: these checks run on the CPU, not on a GPU."), OrtFactory.CpuAdapter);

            var gpus = GpuCatalog.Enumerate();
            string list = gpus.Count == 0
                ? "no DXGI adapters"
                : string.Join("; ", gpus.Select(g =>
                {
                    var (ok, reason) = g.CheckSupport();
                    return $"[{g.DxgiIndex}] {g.Summary}: {(ok ? "supported" : reason)}";
                }));

            if (options.Adapter >= 0)
            {
                var chosen = gpus.FirstOrDefault(g => g.DxgiIndex == options.Adapter);
                if (chosen is null) return (R(SelfTestOutcome.Fail, $"Adapter {options.Adapter} does not exist. Adapters: {list}"), NoDevice);
                var (ok, reason) = chosen.CheckSupport();
                return (R(ok ? SelfTestOutcome.Pass : SelfTestOutcome.Warn, $"Using {chosen.Summary} ({reason}). Adapters: {list}"), chosen.DxgiIndex);
            }

            var best = GpuCatalog.PickBest(gpus);
            if (best is null)
            {
                return (R(SelfTestOutcome.Fail,
                    $"No supported NVIDIA GPU (GTX 1060 or newer) was found. Adapters: {list}. HandGI runs on the GPU; CPU compatibility mode can be chosen in Setup."), NoDevice);
            }
            return (R(SelfTestOutcome.Pass, $"Using {best.Summary} (DXGI adapter {best.DxgiIndex}). Adapters: {list}"), best.DxgiIndex);
        }
        catch (Exception ex)
        {
            return (R(SelfTestOutcome.Fail, $"Adapter enumeration failed: {ex.Message}"), NoDevice);
        }
    }

    // ------------------------------------------------------------------ CPU-only logic checks

    private static (SelfTestOutcome, string) ScheduleCheck()
    {
        var errors = new List<string>();
        var small = SchedulingPolicy.Decide(SchedulingMode.Auto, 4);
        var large = SchedulingPolicy.Decide(SchedulingMode.Auto, 22);
        var forcedRealtime = SchedulingPolicy.Decide(SchedulingMode.Realtime, 4);
        var forcedNormal = SchedulingPolicy.Decide(SchedulingMode.Normal, 22);
        if (small.UseMmcss || small.Priority != ThreadPriority.Normal) errors.Add("an injected 4-core machine must stay at normal priority");
        if (!large.UseMmcss || large.Priority != ThreadPriority.AboveNormal) errors.Add("an injected 22-core machine should use MMCSS");
        if (!forcedRealtime.UseMmcss) errors.Add("an explicit real-time choice must be obeyed");
        if (forcedNormal.UseMmcss || forcedNormal.Priority != ThreadPriority.Normal) errors.Add("an explicit normal choice must be obeyed");
        if (SchedulingPolicy.PreviewWidth(960, 4, explicitChoice: false).Width != 640) errors.Add("a 4-core machine should get the 640 px preview");
        if (SchedulingPolicy.PreviewWidth(960, 4, explicitChoice: true).Width != 960) errors.Add("an explicit preview width must be obeyed");
        var here = SchedulingPolicy.Decide(SchedulingMode.Auto, Environment.ProcessorCount);
        return errors.Count == 0
            ? (SelfTestOutcome.Pass, $"Injected 4-core and 22-core machines decide correctly. This machine: {here.Reason}")
            : (SelfTestOutcome.Fail, string.Join("; ", errors));
    }

    private static (SelfTestOutcome, string) AnchorCheck()
    {
        var a = PalmAnchors.Centers192();
        var errors = new List<string>();
        void Expect(int anchor, float x, float y)
        {
            float ax = a[anchor * 2], ay = a[anchor * 2 + 1];
            if (MathF.Abs(ax - x) > 1e-3f || MathF.Abs(ay - y) > 1e-3f) errors.Add($"anchor {anchor} is ({ax},{ay}), expected ({x},{y})");
        }
        if (a.Length != PalmAnchors.Count * 2) errors.Add($"{a.Length / 2} anchors, expected {PalmAnchors.Count}");
        else
        {
            Expect(0, 4, 4);
            Expect(1, 4, 4);
            Expect(2, 12, 4);
            Expect(48, 4, 12);
            Expect(1152, 8, 8);
            Expect(1157, 8, 8);
            Expect(1158, 24, 8);
            Expect(2015, 184, 184);
        }
        return errors.Count == 0
            ? (SelfTestOutcome.Pass, "2016 SSD anchors: 24x24x2 (stride 8) + 12x12x6 (stride 16), centres verified.")
            : (SelfTestOutcome.Fail, string.Join("; ", errors));
    }

    private static (SelfTestOutcome, string) GeometryCheck()
    {
        var errors = new List<string>();
        static (float X, float Y) Apply(float[] m, float u, float v) => (m[0] * u + m[1] * v + m[2], m[3] * u + m[4] * v + m[5]);
        void Near(string what, float actual, float expected, float tolerance = 0.05f)
        {
            if (!(MathF.Abs(actual - expected) <= tolerance)) errors.Add($"{what} = {actual:0.###}, expected {expected:0.###}");
        }

        var letterbox = HandGeometry.Letterbox(1280, 720, 192);
        var (lx, ly) = Apply(letterbox, 96, 96);
        Near("letterbox centre x", lx, 640);
        Near("letterbox centre y", ly, 360);
        var (tx, ty) = Apply(letterbox, 0, 0);
        Near("letterbox corner x", tx, 0);
        Near("letterbox corner y", ty, -280);

        var rect = new RotatedRect(300, 200, 120, 120, 0.4f);
        var (crop, scale) = HandGeometry.CropAffine(rect, 224);
        var (cx, cy) = Apply(crop, 112, 112);
        Near("crop centre x", cx, 300);
        Near("crop centre y", cy, 200);
        Near("crop scale", scale, 120f / 224f, 1e-4f);
        var corner = rect.Corners()[0];
        var (kx, ky) = Apply(crop, 0, 0);
        Near("crop corner x", kx, corner.X);
        Near("crop corner y", ky, corner.Y);

        var candidate = new float[HandGraphs.CandidateStride];
        candidate[0] = 0.9f;
        candidate[1] = 600; candidate[2] = 300; candidate[3] = 700; candidate[4] = 420;
        candidate[5] = 650; candidate[6] = 420; // wrist
        candidate[9] = 650; candidate[10] = 330; // middle-finger MCP
        var roi = HandGeometry.FromPalm(candidate);
        Near("palm ROI rotation", roi.Rotation, 0f, 1e-3f);
        Near("palm ROI size", roi.Width, 312f, 0.01f);
        Near("palm ROI centre x", roi.Cx, 650f);
        Near("palm ROI centre y", roi.Cy, 300f);

        var upright = UprightHand();
        var next = HandGeometry.FromLandmarks(upright);
        Near("landmark ROI rotation (upright)", next.Rotation, 0f, 0.05f);
        if (!HandGeometry.IsUsable(next, 1280, 720)) errors.Add("the upright hand ROI is not usable");

        var right = RotateAboutWrist(upright, MathF.PI / 2);
        var turned = HandGeometry.FromLandmarks(right);
        Near("landmark ROI rotation (pointing right)", turned.Rotation, MathF.PI / 2, 0.05f);

        return errors.Count == 0
            ? (SelfTestOutcome.Pass, "Letterbox, rotated crop, palm-to-ROI (scale 2.6, shift -0.5) and landmarks-to-ROI (scale 2.0, shift -0.1) match MediaPipe's geometry.")
            : (SelfTestOutcome.Fail, string.Join("; ", errors));
    }

    private static float[] UprightHand()
    {
        var points = new (float X, float Y)[]
        {
            (640, 520),
            (600, 500), (575, 470), (560, 440), (550, 415),
            (600, 420), (595, 370), (592, 335), (590, 305),
            (640, 410), (640, 355), (640, 318), (640, 288),
            (680, 420), (683, 370), (685, 338), (686, 312),
            (715, 440), (720, 400), (723, 375), (725, 352),
        };
        var lm = new float[HandGraphs.LandmarkCount * 3];
        for (int i = 0; i < points.Length; i++)
        {
            lm[i * 3] = points[i].X;
            lm[i * 3 + 1] = points[i].Y;
        }
        return lm;
    }

    private static float[] RotateAboutWrist(float[] lm, float angle)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        float wx = lm[0], wy = lm[1];
        var r = (float[])lm.Clone();
        for (int i = 0; i < HandGraphs.LandmarkCount; i++)
        {
            float dx = lm[i * 3] - wx, dy = lm[i * 3 + 1] - wy;
            r[i * 3] = wx + dx * c - dy * s;
            r[i * 3 + 1] = wy + dx * s + dy * c;
        }
        return r;
    }

    private sealed class FakeVolume : IVolumeSink
    {
        public float Level { get; private set; } = 0.5f;
        public bool Muted { get; private set; }
        public event Action<float, bool>? Changed;

        public void SetLevel(float level)
        {
            Level = level;
            Changed?.Invoke(Level, Muted);
        }

        public void ToggleMute()
        {
            Muted = !Muted;
            Changed?.Invoke(Level, Muted);
        }
    }

    private static (SelfTestOutcome, string) DeciderCheck()
    {
        var profile = GestureProfile.CreateDefault(); // Google's ready-made gestures: usable without any training
        if (!profile.IsUsable) return (SelfTestOutcome.Fail, "A new profile (Google's ready-made gestures) cannot be used without training.");

        var volume = new FakeVolume();
        bool armed = true;
        var decider = new GestureDecider(profile, volume, () => armed);
        var events = new List<GestureEvent>();
        var probs = new float[HandGraphs.MaxGestures];
        double t = 1000;
        void Frames(int cls, int count, bool hand = true)
        {
            for (int n = 0; n < count; n++)
            {
                Array.Clear(probs);
                for (int i = 0; i < profile.Classes.Count; i++) probs[i] = cls == i ? 0.94f : 0.02f;
                decider.Update(hand, probs, 0.3f, t, 25, events);
                t += 1000.0 / 30.0;
            }
        }

        var errors = new List<string>();
        int up = profile.Classes.FindIndex(c => c.Action == GestureAction.VolumeUp);
        int down = profile.Classes.FindIndex(c => c.Action == GestureAction.VolumeDown);
        if (up < 0 || down < 0) return (SelfTestOutcome.Fail, "The default profile has no 'Volume up' or 'Volume down' gesture.");

        Frames(up, 30, hand: false); // no hand in the camera: nothing may happen
        if (events.Count > 0) errors.Add($"{events.Count} event(s) without any hand in the camera");

        Frames(up, 36); // a hand enters the view showing "Volume up" for 1.2 s
        int started = events.Count(e => e.Kind == GestureEventKind.Started && e.ClassIndex == up);
        var ups = events.Where(e => e.Kind == GestureEventKind.Action && e.ClassIndex == up).ToList();
        if (started != 1) errors.Add($"'Volume up' started {started} times (expected once)");
        if (ups.Count < 4) errors.Add($"only {ups.Count} volume-up steps while held for 1.2 s (expected at least 4)");
        if (volume.Level < 0.5f + 3 * profile.Decision.Step - 1e-4f) errors.Add($"the volume only reached {volume.Level:P0}");
        float risen = volume.Level;

        Frames(up, 15, hand: false); // the hand leaves the camera: the gesture ends and nothing more may change
        if (!events.Any(e => e.Kind == GestureEventKind.Ended && e.ClassIndex == up)) errors.Add("'Volume up' never ended after the hand left the camera");
        if (MathF.Abs(volume.Level - risen) > 1e-6f) errors.Add("the volume changed after the hand left the camera");
        if (decider.ActiveClass != -1) errors.Add("a gesture stayed active after the hand left the camera");

        armed = false; // safe mode: decisions are reported, never applied
        int before = events.Count;
        Frames(down, 30);
        var dry = events.Skip(before).Where(e => e.Kind == GestureEventKind.Action).ToList();
        if (dry.Count == 0) errors.Add("no dry-run events while disarmed");
        if (dry.Any(e => !e.DryRun)) errors.Add("an action was not marked as a dry run while disarmed");
        if (MathF.Abs(volume.Level - risen) > 1e-6f) errors.Add("the volume changed while disarmed");

        Frames(down, 1, hand: false);
        if (decider.ActiveClass != -1) errors.Add("a gesture stayed active after the hand left the frame");

        return errors.Count == 0
            ? (SelfTestOutcome.Pass, $"No hand in the camera: nothing happens. A hand showing 'Volume up' for 1.2 s: {ups.Count} steps, 50% to {risen:P0} (accelerating); the action stops the moment the hand leaves; dry run when disarmed.")
            : (SelfTestOutcome.Fail, string.Join("; ", errors));
    }

    /// <summary>The gesture list itself (no camera, no GPU, no model file): Google's, yours, replacing, saving, upgrading.</summary>
    private static (SelfTestOutcome, string) ProfileCheck()
    {
        var errors = new List<string>();
        var p = GestureProfile.CreateDefault();
        if (p.Classes.Count != ReadyGestures.All.Count || p.MineCount != 0)
            errors.Add($"a new profile has {p.Classes.Count} gestures ({p.MineCount} of your own); expected Google's {ReadyGestures.All.Count}");
        if (!p.IsUsable || p.NeedsTraining) errors.Add("a new profile cannot be used without training");
        GestureAction ActionOf(string id) => p.Classes.First(c => c.IsReadyMade && c.ReadyMadeId == id).Action;
        if (ActionOf("Thumb_Up") != GestureAction.VolumeUp || ActionOf("Thumb_Down") != GestureAction.VolumeDown || ActionOf("Closed_Fist") != GestureAction.MuteToggle)
            errors.Add("the starting actions are not thumb up = volume up, thumb down = volume down, fist = mute");
        int up = p.Classes.FindIndex(c => c.IsReadyMade && c.ReadyMadeId == "Thumb_Up");

        int pinky = p.AddMine("Little finger up", GestureAction.None);
        int myUp = p.ReplaceWithMine(up);
        if (!p.NeedsTraining || p.IsTrained) errors.Add("new gestures of your own are not marked as needing to be learned");
        if (p.MineOrdinal(pinky) != 0 || p.MineOrdinal(myUp) != 1 || p.MineOrdinal(up) != -1) errors.Add("your gestures are not numbered in order");
        if (p.Classes[myUp].Action != GestureAction.VolumeUp || p.Classes[myUp].ReadyMadeId != "Thumb_Up") errors.Add("a replacement does not take over the replaced gesture's action");
        if (p.Classes[up].Enabled) errors.Add("a replaced ready-made gesture stayed switched on");
        if (!p.IsUsable) errors.Add("the profile stopped working while your gestures wait to be learned");

        var rng = new Random(3);
        for (int i = 0; i < 5; i++) p.Samples.Add(RandomRow(rng), 0);
        for (int i = 0; i < 7; i++) p.Samples.Add(RandomRow(rng), 1);

        string path = Path.Combine(Path.GetTempPath(), $"handgi-selftest-{Guid.NewGuid():N}.json");
        try
        {
            p.Save(path);
            var q = GestureProfile.Load(path);
            bool same = q.Classes.Count == p.Classes.Count && q.Classes.Zip(p.Classes).All(x =>
                x.First.Name == x.Second.Name && x.First.Source == x.Second.Source && x.First.Action == x.Second.Action &&
                x.First.Enabled == x.Second.Enabled && x.First.ReadyMadeId == x.Second.ReadyMadeId);
            if (!same) errors.Add("the gestures came back changed after saving and loading");
            if (q.Samples.Count != 12 || q.Samples.CountOf(1) != 7 || MaxDiff(q.Samples.Features[11], p.Samples.Features[11]) != 0)
                errors.Add("the recorded examples came back changed after saving and loading");
        }
        finally
        {
            TryDelete(path);
        }

        p.RemoveMine(myUp);
        if (!p.Classes[up].Enabled) errors.Add("removing your replacement did not bring Google's thumb up back");
        if (p.Samples.Count != 5 || p.Samples.CountOf(0) != 5) errors.Add("removing one of your gestures lost another one's examples");

        // A version 1 file (66 numbers per example, from before the ready-made gestures existed).
        var hand = SyntheticHands.Render(SyntheticHands.OpenPalm, new Random(5), 1280, 720, jitter: false);
        float palm = MathF.Sqrt(MathF.Pow(hand[27] - hand[0], 2) + MathF.Pow(hand[28] - hand[1], 2)); // wrist → middle-finger base
        var v1 = new float[66];
        for (int i = 0; i < HandGraphs.LandmarkCount; i++)
            for (int k = 0; k < 3; k++) v1[i * 3 + k] = (hand[i * 3 + k] - hand[k]) / palm;
        var data = new float[3 * 66];
        for (int r = 0; r < 3; r++) v1.CopyTo(data, r * 66);
        string json = JsonSerializer.Serialize(new
        {
            Version = 1,
            Name = "Old profile",
            Classes = new[] { new { Name = "Up", Action = "VolumeUp", Hint = "" }, new { Name = "Down", Action = "VolumeDown", Hint = "" } },
            Samples = new { Labels = new[] { 0, 0, 1 }, Data = Convert.ToBase64String(MemoryMarshal.AsBytes(data.AsSpan())) },
            Decision = new { EnterThreshold = 0.8f, ExitThreshold = 0.5f },
        });
        string oldPath = Path.Combine(Path.GetTempPath(), $"handgi-selftest-v1-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(oldPath, json);
            var old = GestureProfile.Load(oldPath);
            if (old.Classes.Count(c => c.IsReadyMade) != ReadyGestures.All.Count || old.MineCount != 2 || old.Samples.Count != 3)
            {
                errors.Add($"an old profile was not upgraded to Google's gestures + its own ({old.Classes.Count} gestures, {old.Samples.Count} examples)");
            }
            else
            {
                var expected = MediaPipeGestureModel.Normalize(hand, 1280, 720);
                double dxy = 0;
                for (int i = 0; i < HandGraphs.LandmarkCount; i++)
                {
                    dxy = Math.Max(dxy, Math.Abs(old.Samples.Features[0][i * 3] - expected[i * 3]));
                    dxy = Math.Max(dxy, Math.Abs(old.Samples.Features[0][i * 3 + 1] - expected[i * 3 + 1]));
                }
                if (dxy > 1e-3) errors.Add($"old examples were converted wrongly (off by {dxy:0.0000})");
            }
            if (old.Weights is not null || old.MigrationNote is null) errors.Add("an upgraded profile did not ask for its gestures to be learned again");
            if (MathF.Abs(old.Decision.EnterThreshold - 0.65f) > 1e-4f) errors.Add("an upgraded profile kept the old start threshold");
        }
        finally
        {
            TryDelete(oldPath);
        }

        return errors.Count == 0
            ? (SelfTestOutcome.Pass, "A new list starts with Google's 7 gestures (thumb up = volume up, thumb down = volume down, fist = mute) and works without training. " +
                                     "Your own gestures are added and numbered; a replacement takes over its gesture's action and switches Google's off; removing it brings Google's back. " +
                                     "Saving and loading keeps everything; an old profile is upgraded and keeps its examples.")
            : (SelfTestOutcome.Fail, string.Join("; ", errors));
    }

    private static float[] RandomRow(Random rng)
    {
        var row = new float[SampleSet.Dimension];
        for (int i = 0; i < row.Length; i++) row[i] = (float)Gaussian(rng);
        return row;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ------------------------------------------------------------------ GPU checks

    private static (SelfTestOutcome, string) OperatorCheck(int adapter, bool fallback)
    {
        var g = new OnnxGraph("op_");
        var bytes = g.Input("bytes", OnnxType.UInt8, 16);
        var x = g.Input("x", OnnxType.Float, 2, 4);
        var outputs = new List<string>();
        void Out(string tensor, string name, OnnxType type, params long[] shape)
        {
            g.IdentityTo(tensor, name);
            g.Output(name, type, shape);
            outputs.Add(name);
        }

        float[] w = { 1, 2, 0, -1, 0.5f, 1, 2, 0, -2, 1, 1, 1 };
        Out(g.Cast(g.Gather(bytes, g.ConstI32(new[] { 0, 3, 7, 15, 9 }, 5), 0), OnnxType.Float), "gather", OnnxType.Float, 5);
        Out(g.MatMul(x, g.Const(w, 4, 3)), "matmul", OnnxType.Float, 2, 3);
        Out(g.Add(g.Mul(g.Sub(x, g.Scalar(1f)), g.Scalar(2f)), g.Div(x, g.Scalar(4f))), "arith", OnnxType.Float, 2, 4);
        Out(g.Max(g.Min(x, g.Scalar(3f)), g.Scalar(-1f)), "clamp", OnnxType.Float, 2, 4);
        Out(g.Floor(x), "floor", OnnxType.Float, 2, 4);
        Out(g.Sqrt(g.Relu(x)), "sqrt", OnnxType.Float, 2, 4);
        Out(g.Log(g.Add(g.Relu(x), g.Scalar(1f))), "log", OnnxType.Float, 2, 4);
        Out(g.Sigmoid(x), "sigmoid", OnnxType.Float, 2, 4);
        Out(g.Softmax(x, 1), "softmax", OnnxType.Float, 2, 4);
        Out(g.Cast(g.And(g.Greater(x, g.Scalar(0f)), g.Not(g.Less(x, g.Scalar(2f)))), OnnxType.Float), "logic", OnnxType.Float, 2, 4);
        Out(g.Slice(g.Concat(1, x, g.Mul(x, g.Scalar(-1f))), 2, 6, 1), "slice", OnnxType.Float, 2, 4);
        Out(g.Transpose(x, 1, 0), "transpose", OnnxType.Float, 4, 2);
        Out(g.Expand(g.Scalar(7f), 2, 3), "expand", OnnxType.Float, 2, 3);
        var (topValues, topIndices) = g.TopK(g.Reshape(x, 8), 3, 0);
        Out(topValues, "topk_values", OnnxType.Float, 3);
        Out(g.Cast(topIndices, OnnxType.Float), "topk_indices", OnnxType.Float, 3);
        Out(g.ReduceMax(x, 1, keepDims: true), "reducemax", OnnxType.Float, 2, 1);
        Out(g.Cast(g.Cast(g.Floor(x), OnnxType.Int32), OnnxType.Float), "int32", OnnxType.Float, 2, 4);
        Out(g.Cast(g.Min(g.Floor(g.Add(g.Mul(g.Sigmoid(x), g.Scalar(255f)), g.Scalar(0.5f))), g.Scalar(255f)), OnnxType.UInt8), "uint8", OnnxType.UInt8, 2, 4);

        var model = g.ToModel("handgi_operator_probe");
        using var session = OrtFactory.CreateSession(model, adapter, fallback, "selftest-ops");

        var byteData = new byte[16];
        for (int i = 0; i < 16; i++) byteData[i] = (byte)(i * 17);
        float[] xs = { -1.5f, 0f, 0.75f, 2f, 3.5f, -0.25f, 1f, 2.5f };
        using var bytesValue = OrtValue.CreateTensorValueFromMemory(byteData, new long[] { 16 });
        using var xValue = OrtValue.CreateTensorValueFromMemory(xs, new long[] { 2, 4 });
        using var run = new RunOptions();
        using var results = session.Run(run, new[] { "bytes", "x" }, new[] { bytesValue, xValue }, outputs.ToArray());
        var values = results.ToArray();

        static float Sig(float v) => 1f / (1f + MathF.Exp(-v));
        var expected = new Dictionary<string, float[]>
        {
            ["gather"] = new float[] { 0, 51, 119, 255, 153 },
            ["matmul"] = MatMul(xs, 2, 4, w, 3),
            ["arith"] = xs.Select(v => (v - 1) * 2 + v / 4).ToArray(),
            ["clamp"] = xs.Select(v => MathF.Max(MathF.Min(v, 3), -1)).ToArray(),
            ["floor"] = xs.Select(MathF.Floor).ToArray(),
            ["sqrt"] = xs.Select(v => MathF.Sqrt(MathF.Max(v, 0))).ToArray(),
            ["log"] = xs.Select(v => MathF.Log(MathF.Max(v, 0) + 1)).ToArray(),
            ["sigmoid"] = xs.Select(Sig).ToArray(),
            ["softmax"] = SoftmaxRows(xs, 2, 4),
            ["logic"] = xs.Select(v => v > 0 && !(v < 2) ? 1f : 0f).ToArray(),
            ["slice"] = new[] { xs[2], xs[3], -xs[0], -xs[1], xs[6], xs[7], -xs[4], -xs[5] },
            ["transpose"] = new[] { xs[0], xs[4], xs[1], xs[5], xs[2], xs[6], xs[3], xs[7] },
            ["expand"] = Enumerable.Repeat(7f, 6).ToArray(),
            ["topk_values"] = new[] { 3.5f, 2.5f, 2f },
            ["topk_indices"] = new[] { 4f, 7f, 3f },
            ["reducemax"] = new[] { 2f, 3.5f },
            ["int32"] = xs.Select(MathF.Floor).ToArray(),
        };

        double worst = 0;
        var errors = new List<string>();
        for (int i = 0; i < outputs.Count; i++)
        {
            string name = outputs[i];
            if (name == "uint8")
            {
                var got = values[i].GetTensorDataAsSpan<byte>().ToArray();
                for (int j = 0; j < xs.Length; j++)
                {
                    int want = (int)MathF.Min(MathF.Floor(Sig(xs[j]) * 255f + 0.5f), 255f);
                    if (Math.Abs(got[j] - want) > 1) errors.Add($"uint8[{j}] = {got[j]}, expected {want}");
                }
                continue;
            }
            var actual = values[i].GetTensorDataAsSpan<float>().ToArray();
            var want2 = expected[name];
            if (actual.Length != want2.Length)
            {
                errors.Add($"{name}: {actual.Length} values, expected {want2.Length}");
                continue;
            }
            for (int j = 0; j < actual.Length; j++)
            {
                double d = Math.Abs(actual[j] - want2[j]);
                worst = Math.Max(worst, d);
                if (d > 1e-4 + 1e-4 * Math.Abs(want2[j])) errors.Add($"{name}[{j}] = {actual[j]}, expected {want2[j]}");
            }
        }

        // ONNX Runtime copies a CPU input to the device at the moment it is bound. Every HandGI session
        // relies on one rule: an input that changes is bound again before the next run. Guard that rule.
        string bindNote = "";
        var probe = new OnnxGraph("bind_");
        var pv = probe.Input("v", OnnxType.Float, 2);
        probe.IdentityTo(probe.Mul(pv, probe.Scalar(2f)), "twice");
        probe.Output("twice", OnnxType.Float, 2);
        using (var bindSession = OrtFactory.CreateSession(probe.ToModel("handgi_binding_probe"), adapter, fallback, "selftest-bind"))
        {
            static bool Near(float a, float b) => Math.Abs(a - b) <= 1e-5f;
            float[] vData = { 1f, 2f };
            float[] twice = new float[2];
            using var vValue = OrtValue.CreateTensorValueFromMemory(vData, new long[] { 2 });
            using var outValue = OrtValue.CreateTensorValueFromMemory(twice, new long[] { 2 });
            using var bindRun = new RunOptions();
            using var binding = bindSession.CreateIoBinding();
            binding.BindInput("v", vValue);
            binding.BindOutput("twice", outValue);
            bindSession.RunWithBinding(bindRun, binding);
            if (!Near(twice[0], 2f) || !Near(twice[1], 4f)) errors.Add($"binding: first run gave [{twice[0]}, {twice[1]}], expected [2, 4]");
            vData[0] = 5f;
            vData[1] = 6f;                       // changed, but not bound again
            bindSession.RunWithBinding(bindRun, binding);
            bool stale = Near(twice[0], 2f) && Near(twice[1], 4f);
            binding.BindInput("v", vValue);      // bound again: the new values must reach the device
            bindSession.RunWithBinding(bindRun, binding);
            if (!Near(twice[0], 10f) || !Near(twice[1], 12f)) errors.Add($"binding: re-bound run gave [{twice[0]}, {twice[1]}], expected [10, 12]");
            bindNote = stale
                ? "an input changed without binding it again stays stale here, and binding it again refreshes it"
                : "binding an input again refreshes it";
        }

        string where = adapter < 0 ? "the CPU" : $"GPU adapter {adapter}";
        string fallbackNote = adapter >= 0 && !fallback ? "CPU fallback disabled, so every node ran on the GPU" : "CPU fallback allowed";
        return errors.Count == 0
            ? (SelfTestOutcome.Pass, $"{outputs.Count} operator outputs (Gather/Cast/MatMul/TopK/ReduceMax/Softmax/logic...) match CPU math on {where} (max error {worst:0.0e0}); {fallbackNote}; {bindNote}.")
            : (SelfTestOutcome.Fail, string.Join("; ", errors.Take(8)));
    }

    private static float[] MatMul(float[] a, int rows, int inner, float[] b, int cols)
    {
        var r = new float[rows * cols];
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
            {
                float s = 0;
                for (int k = 0; k < inner; k++) s += a[i * inner + k] * b[k * cols + j];
                r[i * cols + j] = s;
            }
        return r;
    }

    private static float[] SoftmaxRows(float[] a, int rows, int cols)
    {
        var r = new float[a.Length];
        for (int i = 0; i < rows; i++)
        {
            float max = float.MinValue;
            for (int j = 0; j < cols; j++) max = MathF.Max(max, a[i * cols + j]);
            float sum = 0;
            for (int j = 0; j < cols; j++) sum += r[i * cols + j] = MathF.Exp(a[i * cols + j] - max);
            for (int j = 0; j < cols; j++) r[i * cols + j] /= sum;
        }
        return r;
    }

    private static (SelfTestOutcome, string) SamplerCheck(int adapter, bool fallback)
    {
        const int W = 96, H = 64, OutW = 40, OutH = 30;
        var rng = new Random(20261125);
        int nvStride = 128;
        int nvUv = nvStride * H + 256; // a gap between the planes, as some drivers deliver
        var nv12 = new FrameLayout(PixelLayoutKind.Nv12, W, H, nvUv + nvStride * (H / 2), 0, nvStride, nvUv, nvStride);
        var cases = new (string Name, FrameLayout Layout, YuvRange Range, bool Bilinear)[]
        {
            ("NV12 limited bilinear", nv12, YuvRange.Limited, true),
            ("NV12 full nearest", nv12, YuvRange.Full, false),
            ("YUY2 limited bilinear", new FrameLayout(PixelLayoutKind.Yuy2, W, H, (W * 2 + 64) * H, 0, W * 2 + 64, 0, 0), YuvRange.Limited, true),
            ("YUY2 limited nearest", FrameLayout.Packed(PixelLayoutKind.Yuy2, W, H), YuvRange.Limited, false),
            ("BGRA8 bilinear", new FrameLayout(PixelLayoutKind.Bgra8, W, H, (W * 4 + 32) * H, 0, W * 4 + 32, 0, 0), YuvRange.Limited, true),
            ("RGBA8 nearest", FrameLayout.Packed(PixelLayoutKind.Rgba8, W, H), YuvRange.Limited, false),
            ("Gray8 bilinear", FrameLayout.Packed(PixelLayoutKind.Gray8, W, H), YuvRange.Limited, true),
        };

        float theta = 25f * MathF.PI / 180f, k = 1.35f;
        float c = MathF.Cos(theta) * k, s = MathF.Sin(theta) * k;
        float cx = W * 0.72f, cy = H * 0.45f;
        var rotated = new[] { c, -s, cx - (c * OutW / 2f - s * OutH / 2f), s, c, cy - (s * OutW / 2f + c * OutH / 2f) };
        var mirrored = new[] { -W / (float)OutW, 0f, W, 0f, H / (float)OutH, 0f };

        double worst = 0;
        int compared = 0, skipped = 0;
        var failures = new List<string>();
        foreach (var (name, layout, range, bilinear) in cases)
        {
            var frame = new byte[layout.ByteLength];
            rng.NextBytes(frame);
            using var session = OrtFactory.CreateSession(HandGraphs.BuildSamplerProbe(layout, OutW, OutH, bilinear, range), adapter, fallback, "selftest-sampler");
            using var frameValue = OrtValue.CreateTensorValueFromMemory(frame, new long[] { frame.Length });
            foreach (var affine in new[] { rotated, mirrored })
            {
                var a = (float[])affine.Clone();
                using var affineValue = OrtValue.CreateTensorValueFromMemory(a, new long[] { 2, 3 });
                using var run = new RunOptions();
                using var result = session.Run(run, new[] { HandGraphs.Frame, HandGraphs.RoiAffine }, new[] { frameValue, affineValue }, new[] { "rgb" });
                var gpu = result.First().GetTensorDataAsSpan<float>().ToArray();
                var ambiguous = new bool[OutW * OutH];
                var cpu = ReferenceRgb(frame, layout, a, OutW, OutH, bilinear, range, ambiguous);

                double caseWorst = 0;
                int worstAt = 0;
                for (int p = 0; p < OutW * OutH; p++)
                {
                    if (ambiguous[p])
                    {
                        skipped++;
                        continue;
                    }
                    compared++;
                    for (int ch = 0; ch < 3; ch++)
                    {
                        double d = Math.Abs(gpu[p * 3 + ch] - cpu[p * 3 + ch]);
                        if (d > caseWorst)
                        {
                            caseWorst = d;
                            worstAt = p;
                        }
                    }
                }
                worst = Math.Max(worst, caseWorst);
                if (caseWorst > 2e-3)
                    failures.Add($"{name}: max error {caseWorst:0.0000} at output pixel ({worstAt % OutW},{worstAt / OutW})");
            }
        }

        return failures.Count == 0
            ? (SelfTestOutcome.Pass, $"{cases.Length} layouts x 2 transforms: {compared:N0} pixels match the CPU reference (max error {worst:0.0e0}); {skipped} pixels on exact rounding boundaries skipped.")
            : (SelfTestOutcome.Fail, string.Join("; ", failures));
    }

    /// <summary>The sampler's math, written plainly on the CPU in double precision.</summary>
    private static float[] ReferenceRgb(byte[] f, FrameLayout l, float[] a, int outW, int outH, bool bilinear, YuvRange range, bool[] ambiguous)
    {
        var rgb = new float[outW * outH * 3];
        int w = l.Width, h = l.Height;
        double Valid(double x, double y) => x > -0.5 && x < w - 0.5 && y > -0.5 && y < h - 0.5 ? 1 : 0;
        int Cx(double x) => (int)Math.Clamp(x, 0, w - 1);
        int Cy(double y) => (int)Math.Clamp(y, 0, h - 1);
        double Luma(int x, int y) => l.Kind == PixelLayoutKind.Yuy2 ? f[l.Offset0 + y * l.Stride0 + 2 * x] : f[l.Offset0 + y * l.Stride0 + x];
        double Px(int x, int y, int ch) => f[l.Offset0 + y * l.Stride0 + 4 * x + ch];
        double Bil(double sx, double sy, Func<int, int, double> fetch)
        {
            double x0 = Math.Floor(sx), y0 = Math.Floor(sy);
            double fx = sx - x0, fy = sy - y0;
            double x1 = x0 + 1, y1 = y0 + 1;
            return fetch(Cx(x0), Cy(y0)) * (1 - fx) * (1 - fy) * Valid(x0, y0)
                 + fetch(Cx(x1), Cy(y0)) * fx * (1 - fy) * Valid(x1, y0)
                 + fetch(Cx(x0), Cy(y1)) * (1 - fx) * fy * Valid(x0, y1)
                 + fetch(Cx(x1), Cy(y1)) * fx * fy * Valid(x1, y1);
        }
        static bool NearBoundary(double t)
        {
            double frac = t - Math.Floor(t);
            return frac < 2e-3 || frac > 1 - 2e-3;
        }

        for (int v = 0; v < outH; v++)
        {
            for (int u = 0; u < outW; u++)
            {
                int p = v * outW + u;
                double xc = u + 0.5, yc = v + 0.5;
                double sx = a[0] * xc + a[1] * yc + a[2] - 0.5;
                double sy = a[3] * xc + a[4] * yc + a[5] - 0.5;
                ambiguous[p] = NearBoundary(sx + 0.5) || NearBoundary(sy + 0.5);
                double xr = Math.Floor(sx + 0.5), yr = Math.Floor(sy + 0.5);
                double inside = Valid(xr, yr);
                int xrc = Cx(xr), yrc = Cy(yr);
                double r, g, b;
                switch (l.Kind)
                {
                    case PixelLayoutKind.Bgra8:
                    case PixelLayoutKind.Rgba8:
                    {
                        int rOff = l.Kind == PixelLayoutKind.Bgra8 ? 2 : 0;
                        int bOff = l.Kind == PixelLayoutKind.Bgra8 ? 0 : 2;
                        r = (bilinear ? Bil(sx, sy, (x, y) => Px(x, y, rOff)) : Px(xrc, yrc, rOff)) / 255.0;
                        g = (bilinear ? Bil(sx, sy, (x, y) => Px(x, y, 1)) : Px(xrc, yrc, 1)) / 255.0;
                        b = (bilinear ? Bil(sx, sy, (x, y) => Px(x, y, bOff)) : Px(xrc, yrc, bOff)) / 255.0;
                        break;
                    }
                    case PixelLayoutKind.Gray8:
                        r = g = b = (bilinear ? Bil(sx, sy, Luma) : Luma(xrc, yrc)) / 255.0;
                        break;
                    default:
                    {
                        double y = bilinear ? Bil(sx, sy, Luma) : Luma(xrc, yrc);
                        int hx = xrc / 2;
                        double cu, cv;
                        if (l.Kind == PixelLayoutKind.Nv12)
                        {
                            int hy = yrc / 2;
                            cu = f[l.Offset1 + hy * l.Stride1 + 2 * hx];
                            cv = f[l.Offset1 + hy * l.Stride1 + 2 * hx + 1];
                        }
                        else
                        {
                            cu = f[l.Offset0 + yrc * l.Stride0 + 4 * hx + 1];
                            cv = f[l.Offset0 + yrc * l.Stride0 + 4 * hx + 3];
                        }
                        double uu = cu - 128, vv = cv - 128, yy;
                        double cr, cgu, cgv, cb;
                        if (range == YuvRange.Limited)
                        {
                            yy = (y - 16) * 1.164383;
                            cr = 1.596027; cgu = 0.391762; cgv = 0.812968; cb = 2.017232;
                        }
                        else
                        {
                            yy = y;
                            cr = 1.402; cgu = 0.344136; cgv = 0.714136; cb = 1.772;
                        }
                        r = (yy + vv * cr) / 255.0;
                        g = (yy - uu * cgu - vv * cgv) / 255.0;
                        b = (yy + uu * cb) / 255.0;
                        break;
                    }
                }
                rgb[p * 3] = (float)Math.Clamp(r * inside, 0, 1);
                rgb[p * 3 + 1] = (float)Math.Clamp(g * inside, 0, 1);
                rgb[p * 3 + 2] = (float)Math.Clamp(b * inside, 0, 1);
            }
        }
        return rgb;
    }

    private static (SelfTestOutcome, string) ModelCheck()
    {
        var lines = new List<string>();
        bool ok = true;
        foreach (var model in ModelCatalog.All)
        {
            var status = ModelCatalog.Check(model);
            if (!status.Verified || status.Path is null)
            {
                ok = false;
                lines.Add($"{model.FileName}: {status.Message}");
                continue;
            }
            if (model.Kind == ModelKind.MediaPipeTask)
            {
                var gestures = MediaPipeGestureModel.FromTask(File.ReadAllBytes(status.Path));
                lines.Add($"{model.FileName}: SHA-256 verified, {gestures.Describe()}");
                continue;
            }
            var info = OnnxModelInfo.Parse(File.ReadAllBytes(status.Path));
            string inputs = string.Join(", ", info.Inputs.Select(i => $"{i.Name}[{string.Join("x", i.Dims)}]"));
            string outputs = string.Join(", ", info.Outputs.Select(o => $"{o.Name}[{string.Join("x", o.Dims)}]"));
            lines.Add($"{model.FileName}: SHA-256 verified, opset {info.DefaultOpset}, in {inputs}, out {outputs}");
        }
        return (ok ? SelfTestOutcome.Pass : SelfTestOutcome.Fail, string.Join(" | ", lines));
    }

    private static (SelfTestOutcome, string) PipelineCheck(int adapter, bool fallback, ILog log, MediaPipeGestureModel gestures)
    {
        var palm = ModelCatalog.LoadVerified(ModelCatalog.Palm);
        var hand = ModelCatalog.LoadVerified(ModelCatalog.Hand);
        var layout = FrameLayout.Packed(PixelLayoutKind.Nv12, 1280, 720);
        var options = new HandTrackerOptions { DxgiIndex = adapter, AllowCpuFallback = fallback || adapter < 0 };
        var owned = new List<IDisposable>();
        (float[] B, OrtValue V) F(params long[] shape)
        {
            var t = OrtFactory.PinnedFloat(shape);
            owned.Add(t.Value);
            return t;
        }

        try
        {
            var (frameBytes, frameValue) = OrtFactory.PinnedBytes(layout.ByteLength);
            owned.Add(frameValue);
            FillNv12(frameBytes, layout);

            using var tracker = new HandTracker(palm, hand, gestures, options, log);
            tracker.Prepare(layout);
            double build = tracker.LastBuildMs;
            var observation = new HandObservation();
            var palmMs = new List<double>();
            for (int i = 0; i < 45; i++)
            {
                tracker.ResetTracking();
                tracker.Process(frameValue, observation);
                if (i >= 5) palmMs.Add(observation.PalmMs);
            }

            using var session = OrtFactory.CreateSession(HandGraphs.BuildHand(hand, layout, YuvRange.Limited, gestures), adapter, options.AllowCpuFallback, "selftest-hand");
            using var binding = session.CreateIoBinding();
            var roiAffine = F(2, 3);
            var roiScale = F(1);
            var mean = F(1, HandGraphs.EmbeddingSize);
            var invStd = F(1, HandGraphs.EmbeddingSize);
            Array.Fill(invStd.B, 1f);
            var w1 = F(HandGraphs.EmbeddingSize, HandGraphs.Hidden1);
            var b1 = F(1, HandGraphs.Hidden1);
            var w2 = F(HandGraphs.Hidden1, HandGraphs.Hidden2);
            var b2 = F(1, HandGraphs.Hidden2);
            var w3 = F(HandGraphs.Hidden2, HandGraphs.MaxClasses);
            var b3 = F(1, HandGraphs.MaxClasses);
            var mask = F(1, HandGraphs.MaxClasses);
            for (int i = 4; i < HandGraphs.MaxClasses; i++) mask.B[i] = -1e4f;
            var landmarks = F(HandGraphs.LandmarkCount, 3);
            var presence = F(1, 1);
            var handedness = F(1, 1);
            var gestureInput = F(1, HandGraphs.GestureInputCount);
            var embedding = F(1, HandGraphs.EmbeddingSize);
            var ready = F(1, HandGraphs.ReadyCount);
            var probs = F(1, HandGraphs.MaxClasses);
            var (affine, scale) = HandGeometry.CropAffine(new RotatedRect(640, 360, 420, 420, 0.3f), HandGraphs.HandSize);
            affine.CopyTo(roiAffine.B, 0);
            roiScale.B[0] = scale;

            binding.BindInput(HandGraphs.Frame, frameValue);
            binding.BindInput(HandGraphs.RoiAffine, roiAffine.V);
            binding.BindInput(HandGraphs.RoiScale, roiScale.V);
            binding.BindInput(HandGraphs.ClfMean, mean.V);
            binding.BindInput(HandGraphs.ClfInvStd, invStd.V);
            binding.BindInput(HandGraphs.ClfW1, w1.V);
            binding.BindInput(HandGraphs.ClfB1, b1.V);
            binding.BindInput(HandGraphs.ClfW2, w2.V);
            binding.BindInput(HandGraphs.ClfB2, b2.V);
            binding.BindInput(HandGraphs.ClfW3, w3.V);
            binding.BindInput(HandGraphs.ClfB3, b3.V);
            binding.BindInput(HandGraphs.ClfMask, mask.V);
            binding.BindOutput(HandGraphs.OutLandmarks, landmarks.V);
            binding.BindOutput(HandGraphs.OutPresence, presence.V);
            binding.BindOutput(HandGraphs.OutHandedness, handedness.V);
            binding.BindOutput(HandGraphs.OutGestureInput, gestureInput.V);
            binding.BindOutput(HandGraphs.OutEmbedding, embedding.V);
            binding.BindOutput(HandGraphs.OutReady, ready.V);
            binding.BindOutput(HandGraphs.OutProbs, probs.V);

            using var run = new RunOptions();
            var handMs = new List<double>();
            for (int i = 0; i < 45; i++)
            {
                long t0 = QpcClock.Now100ns();
                session.RunWithBinding(run, binding);
                if (i >= 5) handMs.Add(QpcClock.MsSince(t0));
            }

            using var preview = new PreviewRenderer(layout, 960, true, options);
            var previewMs = new List<double>();
            for (int i = 0; i < 25; i++)
            {
                long t0 = QpcClock.Now100ns();
                preview.Render(frameValue);
                if (i >= 5) previewMs.Add(QpcClock.MsSince(t0));
            }

            bool finite = landmarks.B.All(float.IsFinite) && float.IsFinite(presence.B[0]) && probs.B.All(float.IsFinite) &&
                          gestureInput.B.All(float.IsFinite) && embedding.B.All(float.IsFinite) && ready.B.All(float.IsFinite);
            float probSum = probs.B.Take(4).Sum();
            float readySum = ready.B.Sum();
            double palm50 = Pct(palmMs, 0.5), palm95 = Pct(palmMs, 0.95), hand50 = Pct(handMs, 0.5), hand95 = Pct(handMs, 0.95);
            string where = adapter < 0 ? "the CPU" : $"GPU adapter {adapter}";
            string detail = $"1280x720 NV12 on {where}: graphs built in {build:0} ms; palm detector p50 {palm50:0.00} ms (p95 {palm95:0.00}); " +
                            $"landmarks + Google's gesture networks + your gestures p50 {hand50:0.00} ms (p95 {hand95:0.00}); preview {Pct(previewMs, 0.5):0.00} ms; " +
                            $"presence on an empty frame {presence.B[0]:0.00}";
            if (!finite) return (SelfTestOutcome.Fail, "Non-finite outputs. " + detail);
            if (MathF.Abs(probSum - 1f) > 1e-3f) return (SelfTestOutcome.Fail, $"Your-gesture probabilities sum to {probSum:0.0000}. " + detail);
            if (MathF.Abs(readySum - 1f) > 1e-3f) return (SelfTestOutcome.Fail, $"Google's gesture probabilities sum to {readySum:0.0000}. " + detail);
            bool slow = palm50 + hand50 > 12;
            return (slow ? SelfTestOutcome.Warn : SelfTestOutcome.Pass, detail + (slow ? ". Slower than a 30-60 fps budget needs." : "."));
        }
        finally
        {
            foreach (var d in owned) d.Dispose();
        }
    }

    private static void FillNv12(byte[] data, FrameLayout l)
    {
        for (int y = 0; y < l.Height; y++)
            for (int x = 0; x < l.Width; x++)
                data[l.Offset0 + y * l.Stride0 + x] = (byte)(40 + (x * 3 + y * 2) % 160);
        for (int y = 0; y < l.Height / 2; y++)
            for (int x = 0; x < l.Width; x++)
                data[l.Offset1 + y * l.Stride1 + x] = 128;
    }

    private static double Pct(List<double> values, double q)
    {
        if (values.Count == 0) return double.NaN;
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted[(int)Math.Round((sorted.Length - 1) * q)];
    }

    /// <summary>
    /// Learns two gestures Google does not know ("little finger up", "three fingers") from drawn hands,
    /// on the GPU, then checks the learned weights through the whole decision path: fresh hands of
    /// yours are recognised as yours, Google's gestures stay Google's, and the GPU and the CPU agree.
    /// </summary>
    private static (SelfTestOutcome, string) TrainingCheck(int adapter, bool fallback, MediaPipeGestureModel model, CancellationToken cancellationToken)
    {
        var poses = new[] { SyntheticHands.PinkyUp, SyntheticHands.ThreeFingers };
        var rng = new Random(7);
        float[] Shot(SyntheticHands.Pose pose) => MediaPipeGestureModel.Normalize(SyntheticHands.Render(pose, rng, 1280, 720), 1280, 720);
        var set = new SampleSet();
        for (int c = 0; c < poses.Length; c++)
            for (int i = 0; i < 120; i++) set.Add(Shot(poses[c]), c);

        var options = new TrainingOptions { Steps = 300, Seed = 99, ValidationFraction = 0.2f };
        var result = GpuTrainer.Train(set, poses.Length, model, options, adapter, fallback, null, cancellationToken);
        var w = result.Weights;

        var profile = GestureProfile.CreateDefault();
        int[] mine = { profile.AddMine(poses[0].Name, GestureAction.None), profile.AddMine(poses[1].Name, GestureAction.None) };
        profile.Weights = w;
        if (!profile.IsTrained) return (SelfTestOutcome.Fail, "The learned weights were not accepted for the two gestures.");

        // The GPU's own evaluation graph and the CPU (Google's embedder + the head) on the same rows.
        double gpuCpu = GpuVersusCpu(model, w, set, adapter, fallback);

        // Fresh hands HandGI never saw, through the same decision path as the live camera.
        var fusion = new GestureFusion();
        var fused = new float[HandGraphs.MaxGestures];
        int Decide(float[] normalized, out float probability)
        {
            var e = model.Embed(normalized);
            fusion.Fuse(profile, model, model.Classify(e), CpuForward(w, e), e, fused);
            int best = ArgMax(fused.AsSpan(0, profile.Classes.Count));
            probability = fused[best];
            return best;
        }
        int ownHits = 0, ownTotal = 0, stolen = 0, readyTotal = 0;
        for (int c = 0; c < poses.Length; c++)
        {
            for (int i = 0; i < 60; i++, ownTotal++)
                if (Decide(Shot(poses[c]), out float p) == mine[c] && p >= profile.Decision.EnterThreshold) ownHits++;
        }
        foreach (var pose in SyntheticHands.ReadyMade.Values)
        {
            for (int i = 0; i < 25; i++, readyTotal++)
                if (mine.Contains(Decide(Shot(pose), out _))) stolen++;
        }

        double ownRate = ownHits / (double)ownTotal;
        double stolenRate = stolen / (double)readyTotal;
        string where = adapter < 0 ? "the CPU" : $"GPU adapter {adapter}";
        string detail = $"2 gestures of your own from {result.Samples} examples + {result.Negatives:N0} generated \"not yours\" hands ({result.NegativesDropped} too close to yours were left out); " +
                        $"{result.Steps} AdamW steps on {where} in {result.TotalMs:0} ms ({result.MsPerStep:0.00} ms/step); held-out accuracy {result.ValidationAccuracy:P1}. " +
                        $"Fresh hands: yours recognised {ownHits}/{ownTotal}, Google's 7 gestures taken for yours {stolen}/{readyTotal}; GPU and CPU agree to {gpuCpu:0.0e0}.";
        if (gpuCpu > 2e-3) return (SelfTestOutcome.Fail, "The GPU and the CPU disagree about the learned weights. " + detail);
        if (result.ValidationAccuracy < 0.95f || ownRate < 0.9) return (SelfTestOutcome.Fail, "Your gestures were not learned well enough. " + detail);
        if (stolenRate > 0.10) return (SelfTestOutcome.Fail, "Google's gestures are taken for yours too often. " + detail);
        if (stolenRate > 0.02) return (SelfTestOutcome.Warn, "Google's gestures are sometimes taken for yours. " + detail);
        return (SelfTestOutcome.Pass, detail);
    }

    /// <summary>
    /// Your gestures with either hand: learned from one hand, they work with the other one (its mirror
    /// image) without Google's gestures, shown with either hand, being taken for yours; examples recorded
    /// with both hands fold into one gesture as tight as with one hand; and two of your gestures that are
    /// mirror images of each other keep to the hand each was recorded with. Every comparison with the
    /// way HandGI learned before uses the very same examples and the very same test hands.
    /// </summary>
    private static (SelfTestOutcome, string) HandsCheck(int adapter, bool fallback, MediaPipeGestureModel model, CancellationToken cancellationToken)
    {
        var rng = new Random(11);
        List<float[]> Shots(SyntheticHands.Pose pose, int count, bool otherHand) => Enumerable.Range(0, count)
            .Select(_ =>
            {
                var shot = MediaPipeGestureModel.Normalize(SyntheticHands.Render(pose, rng, 1280, 720), 1280, 720);
                return otherHand ? MediaPipeGestureModel.Mirrored(shot) : shot;
            })
            .ToList();

        (GestureProfile Profile, TrainingResult Result) Learn(string[] gestureNames, List<float[]>[] examples, bool eitherHand)
        {
            var set = new SampleSet();
            for (int c = 0; c < examples.Length; c++)
                foreach (var row in examples[c]) set.Add(row, c);
            var options = new TrainingOptions { Steps = 300, Seed = 99, ValidationFraction = 0.2f, EitherHand = eitherHand };
            var learned = GpuTrainer.Train(set, examples.Length, model, options, adapter, fallback, null, cancellationToken);
            var learner = GestureProfile.CreateDefault();
            foreach (var name in gestureNames) learner.AddMine(name, GestureAction.None);
            learner.Weights = learned.Weights;
            cancellationToken.ThrowIfCancellationRequested();
            return (learner, learned);
        }

        var fusion = new GestureFusion();
        var fused = new float[HandGraphs.MaxGestures];
        int Decide(GestureProfile p, float[] normalized, out float probability)
        {
            var e = model.Embed(normalized);
            fusion.Fuse(p, model, model.Classify(e), CpuForward(p.Weights!, e), e, fused);
            int best = ArgMax(fused.AsSpan(0, p.Classes.Count));
            probability = fused[best];
            return best;
        }
        double Rate(GestureProfile p, int ordinal, List<float[]> shots)
        {
            int target = p.MineIndices[ordinal];
            return shots.Count(s => Decide(p, s, out float q) == target && q >= p.Decision.EnterThreshold) / (double)shots.Count;
        }

        // Google's seven gestures as fresh hands: as drawn, and made with the other hand
        var google = SyntheticHands.ReadyMade.Values.Select(pose => (pose.Name, Hand: Shots(pose, 25, false), Other: Shots(pose, 25, true))).ToList();
        (double Share, string Which) Stolen(GestureProfile p)
        {
            var mine = p.MineIndices;
            int taken = 0, total = 0;
            var which = new List<string>();
            foreach (var (gestureName, asDrawn, otherHand) in google)
            {
                int a = asDrawn.Count(s => mine.Contains(Decide(p, s, out _)));
                int b = otherHand.Count(s => mine.Contains(Decide(p, s, out _)));
                taken += a + b;
                total += asDrawn.Count + otherHand.Count;
                if (a > 0) which.Add($"{gestureName} {a}/{asDrawn.Count}");
                if (b > 0) which.Add($"{gestureName} with the other hand {b}/{otherHand.Count}");
            }
            return (taken / (double)total, which.Count == 0 ? "none" : string.Join(", ", which));
        }

        var errors = new List<string>();
        var pinky = SyntheticHands.PinkyUp;
        var three = SyntheticHands.ThreeFingers;
        var names = new[] { pinky.Name, three.Name };
        var pinkyTest = Shots(pinky, 60, false);
        var pinkyOtherTest = Shots(pinky, 60, true);
        var threeTest = Shots(three, 60, false);
        var threeOtherTest = Shots(three, 60, true);

        // 1. recorded with one hand only
        var oneHand = new[] { Shots(pinky, 120, false), Shots(three, 120, false) };
        var (before, _) = Learn(names, oneHand, eitherHand: false);
        var (profile, result) = Learn(names, oneHand, eitherHand: true);
        double beforeSame = (Rate(before, 0, pinkyTest) + Rate(before, 1, threeTest)) / 2;
        double beforeOther = (Rate(before, 0, pinkyOtherTest) + Rate(before, 1, threeOtherTest)) / 2;
        double same = (Rate(profile, 0, pinkyTest) + Rate(profile, 1, threeTest)) / 2;
        double other = (Rate(profile, 0, pinkyOtherTest) + Rate(profile, 1, threeOtherTest)) / 2;
        var (stolenBefore, whichBefore) = Stolen(before);
        var (stolen, which) = Stolen(profile);
        if (!result.BothHands.All(b => b)) errors.Add("a gesture recorded with one hand was not learned for both hands");
        if (same < 0.9 || same < beforeSame - 0.05) errors.Add($"the hand the gestures were recorded with is recognised {same:P0} (before {beforeSame:P0})");
        if (other < 0.9) errors.Add($"the other hand is recognised only {other:P0}");

        // 2. recorded with both hands: every second example of the little finger up is the other hand
        var mixedPinky = Shots(pinky, 60, false).Zip(Shots(pinky, 60, true)).SelectMany(t => new[] { t.First, t.Second }).ToList();
        var (mixedProfile, mixedResult) = Learn(names, new[] { mixedPinky, oneHand[1] }, eitherHand: true);
        double mixedSame = Rate(mixedProfile, 0, pinkyTest), mixedOther = Rate(mixedProfile, 0, pinkyOtherTest);
        float floorOne = result.Weights.Floors[0], floorMixed = mixedResult.Weights.Floors[0];
        if (mixedSame < 0.9 || mixedOther < 0.9) errors.Add($"examples from both hands: recognised {mixedSame:P0} with one hand, {mixedOther:P0} with the other");
        if (floorMixed < floorOne - 0.05f) errors.Add($"examples from both hands made the check looser (similarity floor {floorMixed:0.00} instead of {floorOne:0.00})");

        // 3. two gestures that are mirror images of each other: the thumb out to one side, and to the other
        var thumbOut = SyntheticHands.ThumbUp with { Name = "Thumb out", Roll = 0 };
        var twinNames = new[] { "Thumb out", "Thumb out, mirrored" };
        var twins = new[] { Shots(thumbOut, 120, false), Shots(thumbOut, 120, true) };
        var thumbTest = Shots(thumbOut, 60, false);
        var thumbOtherTest = Shots(thumbOut, 60, true);
        var (twinBefore, _) = Learn(twinNames, twins, eitherHand: false);
        var (twinProfile, twinResult) = Learn(twinNames, twins, eitherHand: true);
        bool kept = twinResult.MirrorTwins.Contains((0, 1)) && !twinResult.BothHands.Any(b => b);
        double twinA = Rate(twinProfile, 0, thumbTest), twinB = Rate(twinProfile, 1, thumbOtherTest);
        double twinBeforeA = Rate(twinBefore, 0, thumbTest), twinBeforeB = Rate(twinBefore, 1, thumbOtherTest);
        if (!kept) errors.Add("two gestures that are mirror images of each other were not kept to their hands");
        if (twinA < twinBeforeA - 0.05 || twinB < twinBeforeB - 0.05)
            errors.Add($"mirror-image gestures are recognised worse than before ({twinA:P0} / {twinB:P0} instead of {twinBeforeA:P0} / {twinBeforeB:P0})");

        string detail = $"Learned with one hand: that hand {same:P0} (before {beforeSame:P0}), the other hand {other:P0} (before {beforeOther:P0}). " +
                        $"Google's 7 gestures, with either hand, taken for yours: {stolen:P1} (before {stolenBefore:P1}; now: {which}; before: {whichBefore}). " +
                        $"Half the examples made with the other hand: {mixedResult.OtherHandExamples} folded in, recognised {mixedSame:P0} / {mixedOther:P0}, " +
                        $"similarity floor {floorMixed:0.00} (one hand {floorOne:0.00}). " +
                        $"Two mirror-image gestures: kept to their hands {(kept ? "yes" : "no")}, recognised {twinA:P0} / {twinB:P0} (before {twinBeforeA:P0} / {twinBeforeB:P0}).";
        if (errors.Count > 0) return (SelfTestOutcome.Fail, string.Join("; ", errors) + ". " + detail);
        if (stolen > 0.10) return (SelfTestOutcome.Fail, "Google's gestures are taken for yours too often. " + detail);
        if (stolen > 0.02) return (SelfTestOutcome.Warn, "Google's gestures are sometimes taken for yours. " + detail);
        return (SelfTestOutcome.Pass, detail);
    }

    /// <summary>Largest probability difference between the GPU evaluation graph and the CPU for the same learned weights.</summary>
    private static double GpuVersusCpu(MediaPipeGestureModel model, ClassifierWeights w, SampleSet set, int adapter, bool fallback)
    {
        const int Batch = 256, X = HandGraphs.GestureInputCount, K = HandGraphs.MaxClasses;
        int rows = Math.Min(Batch, set.Count);
        var owned = new List<IDisposable>();
        (float[] B, OrtValue V) F(params long[] shape)
        {
            var t = OrtFactory.PinnedFloat(shape);
            owned.Add(t.Value);
            return t;
        }
        try
        {
            using var session = OrtFactory.CreateSession(TrainingGraphs.BuildEval(Batch, model), adapter, fallback || adapter < 0, "selftest-eval");
            using var binding = session.CreateIoBinding();
            using var run = new RunOptions();
            var x = F(Batch, X);
            var mean = F(1, HandGraphs.EmbeddingSize);
            var invStd = F(1, HandGraphs.EmbeddingSize);
            var mask = F(1, K);
            var probs = F(Batch, K);
            for (int r = 0; r < rows; r++) set.Features[r].CopyTo(x.B, r * X);
            w.Mean.CopyTo(mean.B, 0);
            w.InvStd.CopyTo(invStd.B, 0);
            for (int c = 0; c < K; c++) mask.B[c] = c < w.ClassCount ? 0f : -1e4f;
            var weights = new Dictionary<string, float[]> { ["w1"] = w.W1, ["b1"] = w.B1, ["w2"] = w.W2, ["b2"] = w.B2, ["w3"] = w.W3, ["b3"] = w.B3 };
            binding.BindInput("x", x.V);
            binding.BindInput("mean", mean.V);
            binding.BindInput("invstd", invStd.V);
            binding.BindInput("mask", mask.V);
            foreach (var name in TrainingGraphs.ParamNames)
            {
                var t = F(TrainingGraphs.ParamShape(name));
                weights[name].CopyTo(t.B, 0);
                binding.BindInput(name, t.V);
            }
            binding.BindOutput("probs", probs.V);
            session.RunWithBinding(run, binding);

            double worst = 0;
            for (int r = 0; r < rows; r++)
            {
                var cpu = CpuForward(w, model.Embed(set.Features[r]));
                worst = Math.Max(worst, MaxDiff(probs.B.AsSpan(r * K, K), cpu));
            }
            return worst;
        }
        finally
        {
            foreach (var d in owned) d.Dispose();
        }
    }

    /// <summary>Your-gesture head on the CPU: Google's 128 numbers → probabilities (0 = not one of yours).</summary>
    private static float[] CpuForward(ClassifierWeights w, float[] embedding)
    {
        const int E = HandGraphs.EmbeddingSize, H1 = HandGraphs.Hidden1, H2 = HandGraphs.Hidden2, K = HandGraphs.MaxClasses;
        var xn = new double[E];
        for (int i = 0; i < E; i++) xn[i] = (embedding[i] - w.Mean[i]) * w.InvStd[i];
        var h1 = new double[H1];
        for (int j = 0; j < H1; j++)
        {
            double s = w.B1[j];
            for (int i = 0; i < E; i++) s += xn[i] * w.W1[i * H1 + j];
            h1[j] = Math.Max(0, s);
        }
        var h2 = new double[H2];
        for (int j = 0; j < H2; j++)
        {
            double s = w.B2[j];
            for (int i = 0; i < H1; i++) s += h1[i] * w.W2[i * H2 + j];
            h2[j] = Math.Max(0, s);
        }
        var z = new double[K];
        for (int c = 0; c < K; c++)
        {
            double s = w.B3[c] + (c < w.ClassCount ? 0 : -1e4);
            for (int i = 0; i < H2; i++) s += h2[i] * w.W3[i * K + c];
            z[c] = s;
        }
        double max = z.Max(), sum = 0;
        var p = new float[K];
        for (int c = 0; c < K; c++)
        {
            double e = Math.Exp(z[c] - max);
            sum += e;
            p[c] = (float)e;
        }
        for (int c = 0; c < K; c++) p[c] = (float)(p[c] / sum);
        return p;
    }

    // ------------------------------------------------------------------ Google's gesture networks

    private sealed record GestureCase(string Name, int Width, int Height, float[] Pixels, float[] Normalized, float[] Embedding, float[] Probs);

    /// <summary>Google's own answers (TensorFlow Lite) for 18 hands, built into HandGI (see tools\gesture_reference.py).</summary>
    private static List<GestureCase> LoadGestureCases()
    {
        using var stream = typeof(SelfTests).Assembly.GetManifestResourceStream("HandGI.Core.SelfTest.gesture_vectors.json")
            ?? throw new InvalidDataException("The gesture test hands are missing from HandGI.Core.");
        using var doc = JsonDocument.Parse(stream);
        var cases = new List<GestureCase>();
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            float[] Floats(string name) => c.GetProperty(name).EnumerateArray().Select(v => (float)v.GetDouble()).ToArray();
            cases.Add(new GestureCase(c.GetProperty("name").GetString() ?? "?", c.GetProperty("width").GetInt32(), c.GetProperty("height").GetInt32(),
                Floats("landmarks_px"), Floats("normalized"), Floats("embedding"), Floats("probs")));
        }
        return cases;
    }

    private static string LabelOf(ReadOnlySpan<float> probs) => MediaPipeGestureModel.Labels[ArgMax(probs)];

    /// <summary>HandGI's copy of Google's networks (CPU) against Google's TensorFlow Lite answers.</summary>
    private static (SelfTestOutcome, string) GestureReferenceCheck(MediaPipeGestureModel model)
    {
        var cases = LoadGestureCases();
        double dn = 0, de = 0, dp = 0;
        var errors = new List<string>();
        foreach (var c in cases)
        {
            var n = MediaPipeGestureModel.Normalize(c.Pixels, c.Width, c.Height);
            var e = model.Embed(n);
            var p = model.Classify(e);
            dn = Math.Max(dn, MaxDiff(n, c.Normalized));
            de = Math.Max(de, MaxDiff(e, c.Embedding));
            dp = Math.Max(dp, MaxDiff(p, c.Probs));
            if (ArgMax(p) != ArgMax(c.Probs)) errors.Add($"{c.Name}: HandGI says {LabelOf(p)}, Google says {LabelOf(c.Probs)}");
        }
        int shapes = cases.Select(c => (c.Width, c.Height)).Distinct().Count();
        string detail = $"{cases.Count} hands in {shapes} picture shapes: the same answer as Google for {cases.Count - errors.Count}/{cases.Count}; " +
                        $"largest differences: hand position {dn:0.0e0}, Google's 128 numbers {de:0.0e0}, gesture probabilities {dp:0.0e0}. {model.Describe()}.";
        bool close = dn <= 1e-4 && de <= 2e-3 && dp <= 1e-3;
        return errors.Count == 0 && close
            ? (SelfTestOutcome.Pass, detail)
            : (SelfTestOutcome.Fail, string.Join("; ", errors.Prepend(detail)));
    }

    /// <summary>The same 18 hands through HandGI's GPU graph (DirectML), compared with Google's answers.</summary>
    private static (SelfTestOutcome, string) GestureGpuCheck(MediaPipeGestureModel model, int adapter, bool fallback)
    {
        var cases = LoadGestureCases();
        double dn = 0, de = 0, dp = 0, ms = 0;
        var errors = new List<string>();
        foreach (var group in cases.GroupBy(c => (c.Width, c.Height)))
        {
            var list = group.ToList();
            int n = list.Count;
            var owned = new List<IDisposable>();
            (float[] B, OrtValue V) F(params long[] shape)
            {
                var t = OrtFactory.PinnedFloat(shape);
                owned.Add(t.Value);
                return t;
            }
            try
            {
                using var session = OrtFactory.CreateSession(GestureGraphs.BuildProbe(model, n, group.Key.Width, group.Key.Height), adapter, fallback || adapter < 0, "selftest-gestures");
                using var binding = session.CreateIoBinding();
                using var run = new RunOptions();
                var pixels = F(n, MediaPipeGestureModel.Landmarks, 3);
                var normalized = F(n, MediaPipeGestureModel.InputSize);
                var embedding = F(n, MediaPipeGestureModel.EmbeddingSize);
                var ready = F(n, MediaPipeGestureModel.ClassCount);
                for (int i = 0; i < n; i++) list[i].Pixels.CopyTo(pixels.B, i * MediaPipeGestureModel.InputSize);
                binding.BindInput("landmarks_px", pixels.V); // CPU inputs reach the GPU when bound
                binding.BindOutput("normalized", normalized.V);
                binding.BindOutput("embedding", embedding.V);
                binding.BindOutput("ready", ready.V);
                session.RunWithBinding(run, binding);
                long t0 = QpcClock.Now100ns();
                session.RunWithBinding(run, binding);
                ms = Math.Max(ms, QpcClock.MsSince(t0));
                for (int i = 0; i < n; i++)
                {
                    var c = list[i];
                    var p = ready.B.AsSpan(i * MediaPipeGestureModel.ClassCount, MediaPipeGestureModel.ClassCount);
                    dn = Math.Max(dn, MaxDiff(normalized.B.AsSpan(i * MediaPipeGestureModel.InputSize, MediaPipeGestureModel.InputSize), c.Normalized));
                    de = Math.Max(de, MaxDiff(embedding.B.AsSpan(i * MediaPipeGestureModel.EmbeddingSize, MediaPipeGestureModel.EmbeddingSize), c.Embedding));
                    dp = Math.Max(dp, MaxDiff(p, c.Probs));
                    if (ArgMax(p) != ArgMax(c.Probs)) errors.Add($"{c.Name}: the GPU says {LabelOf(p)}, Google says {LabelOf(c.Probs)}");
                }
            }
            finally
            {
                foreach (var d in owned) d.Dispose();
            }
        }
        string where = adapter < 0 ? "the CPU" : $"GPU adapter {adapter}";
        string detail = $"{cases.Count} hands on {where}: the same answer as Google for {cases.Count - errors.Count}/{cases.Count}; " +
                        $"largest differences: hand position {dn:0.0e0}, Google's 128 numbers {de:0.0e0}, gesture probabilities {dp:0.0e0}; one batch {ms:0.00} ms.";
        bool close = dn <= 1e-4 && de <= 5e-3 && dp <= 2e-3;
        return errors.Count == 0 && close
            ? (SelfTestOutcome.Pass, detail)
            : (SelfTestOutcome.Fail, string.Join("; ", errors.Prepend(detail)));
    }

    /// <summary>
    /// Google's gestures and yours together, with Google's own numbers: a ready-made gesture comes
    /// through, a switched-off one does nothing, your replacement wins when shown, a shape you never
    /// recorded is not taken for yours, and removing your replacement brings Google's back.
    /// </summary>
    private static (SelfTestOutcome, string) FusionCheck(MediaPipeGestureModel model)
    {
        var errors = new List<string>();
        var cases = LoadGestureCases();
        var upCase = cases.First(c => LabelOf(c.Probs) == "Thumb_Up");
        var palmCase = cases.First(c => LabelOf(c.Probs) == "Open_Palm");
        var pinkyCase = cases.First(c => c.Name.StartsWith("Pinky", StringComparison.OrdinalIgnoreCase));

        var profile = GestureProfile.CreateDefault();
        var fusion = new GestureFusion();
        var fused = new float[HandGraphs.MaxGestures];
        var own = new float[HandGraphs.MaxClasses];
        own[0] = 1f;
        int Fuse(GestureCase c)
        {
            fusion.Fuse(profile, model, c.Probs, own, c.Embedding, fused);
            return ArgMax(fused.AsSpan(0, profile.Classes.Count));
        }
        int up = profile.Classes.FindIndex(c => c.ReadyMadeId == "Thumb_Up");
        int palm = profile.Classes.FindIndex(c => c.ReadyMadeId == "Open_Palm");
        int thumbUpLabel = Array.IndexOf(MediaPipeGestureModel.Labels, "Thumb_Up");

        if (Fuse(upCase) != up || MathF.Abs(fused[up] - upCase.Probs[thumbUpLabel]) > 1e-6f) errors.Add("Google's thumb up did not come through unchanged");
        float google = fused[up];
        profile.Classes[up].Enabled = false;
        Fuse(upCase);
        if (fused[up] != 0) errors.Add("a switched-off gesture was still recognised");
        profile.Classes[up].Enabled = true;

        // Replace Google's thumb up with "your own" (here: the little finger up), as if it had been learned.
        int mine = profile.ReplaceWithMine(up);
        var w = new ClassifierWeights { ClassCount = 2 };
        var white = new float[HandGraphs.EmbeddingSize];
        model.Whiten(pinkyCase.Embedding, white);
        double norm = Math.Sqrt(white.Sum(v => (double)v * v));
        for (int k = 0; k < white.Length; k++) w.Centroids[k] = (float)(white[k] / norm);
        w.Floors[0] = 0.8f;
        profile.Weights = w;
        if (!profile.IsTrained) errors.Add("hand-made learned weights were not accepted");
        own[0] = 0.1f;
        own[1] = 0.9f; // the head is very sure the hand is yours, every time
        if (Fuse(pinkyCase) != mine || MathF.Abs(fused[mine] - 0.9f) > 1e-3f) errors.Add("your gesture did not win when it was shown");
        float yours = fused[mine];
        if (profile.Classes[mine].Action != GestureAction.VolumeUp) errors.Add("your replacement does not turn the volume up");
        int other = Fuse(palmCase);
        float palmSimilarity = fusion.Similarity[0];
        if (fused[mine] != 0 || other != palm) errors.Add($"an open palm was taken for your gesture (similarity {palmSimilarity:0.00})");
        Fuse(upCase);
        float upSimilarity = fusion.Similarity[0];
        if (fused[up] != 0 || fused[mine] != 0) errors.Add("the replaced thumb up still did something");

        profile.RemoveMine(mine);
        own[0] = 1f;
        own[1] = 0f;
        if (!profile.Classes[up].Enabled || profile.IsTrained) errors.Add("removing your replacement did not restore Google's thumb up");
        else if (Fuse(upCase) != up) errors.Add("Google's thumb up did not work again after removing your replacement");

        return errors.Count == 0
            ? (SelfTestOutcome.Pass, $"Thumb up → volume up (Google {google:P0}); switched off → nothing; replaced by your own gesture → yours wins ({yours:P0}) and Google's thumb up does nothing; " +
                                     $"an open palm is not taken for yours (similarity {palmSimilarity:0.00}, thumb up {upSimilarity:0.00}, needed 0.80); removing yours brings Google's back.")
            : (SelfTestOutcome.Fail, string.Join("; ", errors));
    }

    private static double MaxDiff(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length) return double.PositiveInfinity;
        double worst = 0;
        for (int i = 0; i < a.Length; i++) worst = Math.Max(worst, Math.Abs((double)a[i] - b[i]));
        return worst;
    }

    private static int ArgMax(ReadOnlySpan<float> values)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
            if (values[i] > values[best]) best = i;
        return best;
    }

    private static double Gaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static (SelfTestOutcome, string) AudioCheck(ILog log)
    {
        using var volume = new VolumeController(log);
        var sw = Stopwatch.StartNew();
        while (!volume.Available && sw.ElapsedMilliseconds < 1500) Thread.Sleep(20);
        Thread.Sleep(50);
        return volume.Available
            ? (SelfTestOutcome.Pass, $"Default playback device answers: volume {volume.Level:P0}{(volume.Muted ? ", muted" : "")} (read only; the self-test never changes it).")
            : (SelfTestOutcome.Warn, "No default playback device answered; gestures will be recognised but cannot change the volume.");
    }
}
