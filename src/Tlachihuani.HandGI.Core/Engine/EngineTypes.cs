// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using HandGI.Core.Vision;

namespace HandGI.Core.Engine;

public enum EngineState { Stopped, Starting, Preparing, Running, Faulted }

/// <summary>How the processing thread is scheduled. Auto decides from the machine; the others are obeyed exactly.</summary>
public enum SchedulingMode { Auto, Realtime, Normal }

public sealed record SchedulingDecision(bool UseMmcss, ThreadPriority Priority, string Reason);

/// <summary>
/// Picks the processing-thread scheduling for the machine it runs on. A small machine (4 logical
/// processors or fewer) keeps Normal priority so the window always has a core to repaint; larger
/// machines join MMCSS "Games" at above-normal priority. An explicit choice is never second-guessed.
/// The core count is a parameter so the self-test can inject hardware this computer does not have.
/// </summary>
public static class SchedulingPolicy
{
    public const int SmallMachineLogicalProcessors = 4;

    public static SchedulingDecision Decide(SchedulingMode mode, int logicalProcessors) => mode switch
    {
        SchedulingMode.Normal => new(false, ThreadPriority.Normal, "Normal priority (chosen in Setup)."),
        SchedulingMode.Realtime => new(true, ThreadPriority.AboveNormal, "MMCSS \"Games\" and above-normal priority (chosen in Setup)."),
        _ when logicalProcessors <= SmallMachineLogicalProcessors => new(false, ThreadPriority.Normal,
            $"{logicalProcessors} logical processors: normal priority, so the window always keeps a core to repaint."),
        _ => new(true, ThreadPriority.AboveNormal,
            $"{logicalProcessors} logical processors: the processing thread joins MMCSS \"Games\" at above-normal priority."),
    };

    /// <summary>Preview width: a smaller preview on small machines halves the window's drawing work.</summary>
    public static (int Width, string Reason) PreviewWidth(int requested, int logicalProcessors, bool explicitChoice)
    {
        if (explicitChoice || logicalProcessors > SmallMachineLogicalProcessors) return (requested, $"Preview {requested} px wide.");
        int width = Math.Min(requested, 640);
        return (width, $"{logicalProcessors} logical processors: preview reduced to {width} px wide (recognition is unchanged).");
    }
}

public sealed class EngineOptions
{
    /// <summary>DXGI adapter index; <see cref="Gpu.OrtFactory.CpuAdapter"/> (-1) is the opt-in CPU compatibility mode.</summary>
    public int DxgiIndex { get; set; }
    public string DeviceName { get; set; } = "";
    public bool AllowCpuFallback { get; set; }
    public YuvRange Range { get; set; } = YuvRange.Limited;
    public float PalmThreshold { get; set; } = 0.5f;
    public float PresenceThreshold { get; set; } = 0.5f;
    public bool Preview { get; set; } = true;
    public int PreviewMaxWidth { get; set; } = 960;
    public bool Mirror { get; set; } = true;
    public SchedulingMode Scheduling { get; set; } = SchedulingMode.Auto;

    public EngineOptions Clone() => (EngineOptions)MemberwiseClone();
}

/// <summary>Everything the window or the service host needs to know about one processed frame (a copy).</summary>
public sealed class FrameReport
{
    public long Sequence;
    public int Width;
    public int Height;
    public bool Mirror;
    public bool HasHand;
    public bool Detected;
    public float Presence;
    public float Handedness;
    public float PalmScore;
    public RotatedRect Roi;
    public readonly float[] Landmarks = new float[HandGraphs.LandmarkCount * 3];
    /// <summary>Google's eight answers (None, Closed_Fist, Open_Palm, Pointing_Up, Thumb_Down, Thumb_Up, Victory, ILoveYou).</summary>
    public readonly float[] Ready = new float[HandGraphs.ReadyCount];
    /// <summary>Your own gestures' head: 0 = "not one of yours", then your gestures in order.</summary>
    public readonly float[] Own = new float[HandGraphs.MaxClasses];
    /// <summary>Cosine similarity of the hand to the centre of each of your gestures.</summary>
    public readonly float[] Similarity = new float[HandGraphs.MaxOwnGestures];
    /// <summary>One probability per gesture of the profile (same order as its list), before smoothing.</summary>
    public readonly float[] Fused = new float[HandGraphs.MaxGestures];
    public readonly float[] Smoothed = new float[HandGraphs.MaxGestures];
    public float OwnShare;
    public int ActiveClass = -1;
    public int CandidateClass = -1;
    public int GpuRuns;
    public double PalmMs;
    public double HandMs;
    /// <summary>Camera timestamp → decision, in ms (NaN when the camera clock is not comparable).</summary>
    public double CameraToDecisionMs = double.NaN;
    /// <summary>Frame handed to HandGI → decision, in ms.</summary>
    public double ArrivalToDecisionMs;
    public double Fps;
    public bool Recording;
    public int RecordedSamples;
    public DateTime When;

    public double GpuMs => PalmMs + HandMs;

    public void CopyTo(FrameReport d)
    {
        d.Sequence = Sequence;
        d.Width = Width;
        d.Height = Height;
        d.Mirror = Mirror;
        d.HasHand = HasHand;
        d.Detected = Detected;
        d.Presence = Presence;
        d.Handedness = Handedness;
        d.PalmScore = PalmScore;
        d.Roi = Roi;
        Array.Copy(Landmarks, d.Landmarks, Landmarks.Length);
        Array.Copy(Ready, d.Ready, Ready.Length);
        Array.Copy(Own, d.Own, Own.Length);
        Array.Copy(Similarity, d.Similarity, Similarity.Length);
        Array.Copy(Fused, d.Fused, Fused.Length);
        Array.Copy(Smoothed, d.Smoothed, Smoothed.Length);
        d.OwnShare = OwnShare;
        d.ActiveClass = ActiveClass;
        d.CandidateClass = CandidateClass;
        d.GpuRuns = GpuRuns;
        d.PalmMs = PalmMs;
        d.HandMs = HandMs;
        d.CameraToDecisionMs = CameraToDecisionMs;
        d.ArrivalToDecisionMs = ArrivalToDecisionMs;
        d.Fps = Fps;
        d.Recording = Recording;
        d.RecordedSamples = RecordedSamples;
        d.When = When;
    }
}

public sealed record LatencySummary(
    int Samples,
    long Frames,
    long Replaced,
    double Fps,
    bool CameraClock,
    double P50,
    double P95,
    double P99,
    double Max,
    double ArrivalP50,
    double ArrivalP95,
    double GpuP50,
    double GpuP95)
{
    public static readonly LatencySummary Empty = new(0, 0, 0, 0, false, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
}

/// <summary>Fixed-size rings of the last N frames' latencies; percentiles on demand (a few times per second).</summary>
internal sealed class LatencyTracker
{
    private readonly object _gate = new();
    private readonly double[] _camera;
    private readonly double[] _arrival;
    private readonly double[] _gpu;
    private int _next;
    private int _count;
    private int _cameraCount;

    public LatencyTracker(int capacity)
    {
        _camera = new double[capacity];
        _arrival = new double[capacity];
        _gpu = new double[capacity];
    }

    public void Clear()
    {
        lock (_gate)
        {
            _next = 0;
            _count = 0;
            _cameraCount = 0;
        }
    }

    public void Add(double cameraMs, double arrivalMs, double gpuMs)
    {
        lock (_gate)
        {
            _camera[_next] = cameraMs;
            _arrival[_next] = arrivalMs;
            _gpu[_next] = gpuMs;
            _next = (_next + 1) % _camera.Length;
            if (_count < _camera.Length) _count++;
            if (!double.IsNaN(cameraMs)) _cameraCount = Math.Min(_cameraCount + 1, _camera.Length);
        }
    }

    public LatencySummary Summarize(long frames, long replaced, double fps)
    {
        double[] camera, arrival, gpu;
        lock (_gate)
        {
            if (_count == 0) return LatencySummary.Empty with { Frames = frames, Replaced = replaced, Fps = fps };
            camera = _camera.Take(_count).Where(v => !double.IsNaN(v)).ToArray();
            arrival = _arrival.Take(_count).ToArray();
            gpu = _gpu.Take(_count).ToArray();
        }
        Array.Sort(camera);
        Array.Sort(arrival);
        Array.Sort(gpu);
        bool clock = camera.Length > arrival.Length / 2;
        var total = clock ? camera : arrival;
        return new LatencySummary(
            arrival.Length, frames, replaced, fps, clock,
            Pct(total, 0.50), Pct(total, 0.95), Pct(total, 0.99), total[^1],
            Pct(arrival, 0.50), Pct(arrival, 0.95),
            Pct(gpu, 0.50), Pct(gpu, 0.95));
    }

    private static double Pct(double[] sorted, double q) =>
        sorted.Length == 0 ? double.NaN : sorted[(int)Math.Round((sorted.Length - 1) * q)];
}
