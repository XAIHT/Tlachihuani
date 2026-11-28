// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using System.Collections.Concurrent;
using HandGI.Core.Audio;
using HandGI.Core.Capture;
using HandGI.Core.Gestures;
using HandGI.Core.Util;
using HandGI.Core.Vision;

namespace HandGI.Core.Engine;

public delegate void PreviewSink(ReadOnlySpan<byte> bgra, int width, int height);

/// <summary>
/// The real-time pipeline, independent of any window:
/// camera → latest-frame slot → GPU (palm detector, hand landmarks + gesture classifier: two DirectML
/// runs, or one while tracking) → decision → volume. The desktop app and a future Windows service host
/// the same engine; nothing in here touches UI.
/// </summary>
public sealed class GestureEngine : IAsyncDisposable
{
    /// <summary>float32 holds every integer below 2^24 exactly; the GPU sampler computes byte offsets in float.</summary>
    public const int MaxFrameBytes = 1 << 24;

    private const int EventQueueLimit = 512;

    private readonly byte[] _palmModel;
    private readonly byte[] _handModel;
    private readonly MediaPipeGestureModel _gestures;
    private readonly IVolumeSink _volume;
    private readonly ILog _log;
    private readonly object _stateGate = new();
    private readonly object _reportGate = new();
    private readonly object _previewGate = new();
    private readonly object _recordGate = new();
    private readonly ConcurrentQueue<GestureEvent> _eventQueue = new();
    private readonly List<GestureEvent> _events = new();
    private readonly HandObservation _obs = new();
    private readonly LatencyTracker _latency = new(600);
    private readonly List<float[]> _recorded = new();
    private readonly GestureFusion _fusion = new();
    private readonly float[] _fused = new float[HandGraphs.MaxGestures];

    private EngineOptions _options = new();
    private HandTrackerOptions _trackerOptions = new();
    private GestureProfile _profile;
    private GestureProfile? _pendingProfile;
    /// <summary>The processing thread's own copy of the profile (see <see cref="GestureProfile.RuntimeCopy"/>).</summary>
    private GestureProfile _active;
    private GestureDecider? _decider;
    private HandTracker? _tracker;
    private PreviewRenderer? _preview;
    private LatestFrameSlot? _slot;
    private CameraCapture? _camera;
    private Thread? _thread;
    private volatile bool _stop;
    private volatile bool _faulted;
    private volatile bool _armed;
    private volatile bool _mirror = true;
    private volatile bool _mirrorChanged;
    private volatile bool _previewWanted = true;
    private int _recordLabel = -1;

    private FrameReport _reportBack = new();
    private FrameReport _reportFront = new();
    private long _reportSeq;
    private byte[] _previewFront = Array.Empty<byte>();
    private int _previewWidth;
    private int _previewHeight;
    private long _previewSeq;

    private long _frames;
    private double _intervalEma;
    private double _lastFrameMs = double.NaN;
    private const long LiveLogEvery100ns = 15 * 10_000_000L;
    private long _liveAt100ns;
    private long _liveArrived;
    private long _liveFrames;
    private int _consecutiveErrors;
    private long _lastErrorLog;
    private bool _cameraClockWarned;
    private EngineState _state = EngineState.Stopped;
    private string _stateMessage = "Stopped.";

    public GestureEngine(byte[] palmModel, byte[] handModel, MediaPipeGestureModel gestures, IVolumeSink volume, ILog log, GestureProfile? profile = null)
    {
        _palmModel = palmModel;
        _handModel = handModel;
        _gestures = gestures;
        _volume = volume;
        _log = log;
        _profile = profile ?? GestureProfile.CreateDefault();
        _active = _profile.RuntimeCopy();
    }

    /// <summary>Classes of the own-gesture head the GPU must score: "not one of yours" + your learned gestures.</summary>
    private static int HeadClasses(GestureProfile profile) => profile.IsTrained ? profile.MineCount + 1 : 1;

    /// <summary>Raised on whichever thread changed the state (UI code must marshal).</summary>
    public event Action<EngineState, string>? StateChanged;

    /// <summary>Raised on the processing thread; keep handlers short. Windows drain <see cref="TryDequeueEvent"/> instead.</summary>
    public event Action<GestureEvent>? GestureRaised;

    public EngineState State
    {
        get { lock (_stateGate) return _state; }
    }

    public string StateMessage
    {
        get { lock (_stateGate) return _stateMessage; }
    }

    public string? LastError { get; private set; }
    public string CameraDescription { get; private set; } = "";
    public bool CpuDecode { get; private set; }
    public SchedulingDecision? Scheduling { get; private set; }
    public EngineOptions Options => _options;
    public GestureProfile Profile => _profile;
    public bool IsRunning => _thread is not null;
    public long FramesProcessed => Interlocked.Read(ref _frames);
    public double Fps => _intervalEma > 0 ? 1000.0 / _intervalEma : 0;

    /// <summary>When false every decision is a dry run: events are reported, the volume never changes.</summary>
    public bool Armed
    {
        get => _armed;
        set => _armed = value;
    }

    /// <summary>The window sets this to false while minimised: no preview work at all.</summary>
    public bool PreviewWanted
    {
        get => _previewWanted;
        set => _previewWanted = value;
    }

    public bool IsRecording => Volatile.Read(ref _recordLabel) >= 0;

    public int RecordedCount
    {
        get { lock (_recordGate) return _recorded.Count; }
    }

    // ------------------------------------------------------------------ lifecycle

    public async Task StartAsync(CameraInfo camera, CameraFormat? format, bool shared, EngineOptions options)
    {
        await StopAsync().ConfigureAwait(false);

        _options = options.Clone();
        _faulted = false;
        LastError = null;
        _mirror = options.Mirror;
        _mirrorChanged = false;
        _previewWanted = true;
        Interlocked.Exchange(ref _frames, 0);
        _intervalEma = 0;
        _lastFrameMs = double.NaN;
        _consecutiveErrors = 0;
        _cameraClockWarned = false;
        _liveAt100ns = 0;
        _latency.Clear();
        lock (_reportGate) _reportSeq = 0;
        lock (_previewGate) _previewSeq = 0;
        while (_eventQueue.TryDequeue(out _)) { }

        Scheduling = SchedulingPolicy.Decide(options.Scheduling, Environment.ProcessorCount);
        _log.Info($"Scheduling: {Scheduling.Reason}");

        _trackerOptions = new HandTrackerOptions
        {
            DxgiIndex = options.DxgiIndex,
            AllowCpuFallback = options.AllowCpuFallback || options.DxgiIndex < 0,
            Range = options.Range,
            PalmThreshold = options.PalmThreshold,
            PresenceThreshold = options.PresenceThreshold,
        };
        _tracker = new HandTracker(_palmModel, _handModel, _gestures, _trackerOptions, _log);
        var profile = _profile.RuntimeCopy();
        _active = profile;
        _tracker.SetClassifier(profile.IsTrained ? profile.Weights : null, HeadClasses(profile));
        _decider = new GestureDecider(profile, _volume, ActionsArmed);
        Volatile.Write(ref _pendingProfile, null);
        _slot = new LatestFrameSlot();
        _stop = false;

        SetState(EngineState.Starting, $"Opening {camera.DisplayName}...");
        _thread = new Thread(Loop) { IsBackground = true, Name = "HandGI engine" };
        _thread.Start();

        try
        {
            var capture = await CameraCapture.OpenAsync(camera, format, shared, _log).ConfigureAwait(false);
            capture.FrameArrived += OnFrame;
            capture.Failed += OnCameraFailed;
            _camera = capture;
            CameraDescription = capture.Description;
            CpuDecode = capture.CpuDecode;
            if (capture.CpuDecode)
                _log.Warn("This camera mode is compressed (MJPEG): Windows decodes it on the CPU before HandGI sees it. Pick an NV12 or YUY2 mode for the lowest latency.");
            SetStateIf(EngineState.Starting, EngineState.Preparing, "Waiting for the first camera frame...");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            await StopAsync().ConfigureAwait(false);
            SetState(EngineState.Faulted, ex.Message);
            throw;
        }
    }

    public async Task StopAsync()
    {
        var capture = Interlocked.Exchange(ref _camera, null);
        if (capture is not null)
        {
            capture.FrameArrived -= OnFrame;
            capture.Failed -= OnCameraFailed;
            try
            {
                await capture.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warn($"Camera close: {ex.Message}");
            }
        }

        var thread = _thread;
        bool joined = true;
        if (thread is not null)
        {
            _stop = true;
            _slot?.Wake();
            joined = await Task.Run(() => thread.Join(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            if (!joined) _log.Warn("The processing thread did not stop within 5 s; its GPU resources are left to the process exit.");
            _thread = null;
        }

        if (joined)
        {
            _preview?.Dispose();
            _tracker?.Dispose();
            _slot?.Dispose();
        }
        _preview = null;
        _tracker = null;
        _slot = null;
        if (State != EngineState.Faulted && State != EngineState.Stopped) SetState(EngineState.Stopped, "Stopped.");
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    // ------------------------------------------------------------------ live controls

    /// <summary>Hot-swaps the gesture profile (classes, trained weights, decision rules) at the next frame.</summary>
    public void SetProfile(GestureProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profile = profile;
        var runtime = profile.RuntimeCopy();
        Volatile.Write(ref _pendingProfile, runtime);
        _tracker?.SetClassifier(runtime.IsTrained ? runtime.Weights : null, HeadClasses(runtime));
    }

    public void SetMirror(bool mirror)
    {
        _mirror = mirror;
        _mirrorChanged = true;
    }

    /// <summary>Starts collecting the 63 hand-shape numbers of every frame with a hand, for your gesture number <paramref name="label"/> (0, 1, 2…). Actions are suppressed meanwhile.</summary>
    public void BeginRecording(int label)
    {
        lock (_recordGate)
        {
            _recorded.Clear();
            Volatile.Write(ref _recordLabel, label);
        }
    }

    public List<float[]> EndRecording()
    {
        lock (_recordGate)
        {
            Volatile.Write(ref _recordLabel, -1);
            var result = new List<float[]>(_recorded);
            _recorded.Clear();
            return result;
        }
    }

    public bool TryGetReport(FrameReport into, ref long lastSequence)
    {
        lock (_reportGate)
        {
            if (_reportSeq == 0 || _reportSeq == lastSequence) return false;
            _reportFront.CopyTo(into);
            lastSequence = _reportSeq;
            return true;
        }
    }

    /// <summary>Hands the newest GPU-rendered BGRA preview to <paramref name="sink"/> (under a lock: copy, do not keep).</summary>
    public bool TryCopyPreview(ref long lastSequence, PreviewSink sink)
    {
        lock (_previewGate)
        {
            if (_previewSeq == 0 || _previewSeq == lastSequence || _previewFront.Length == 0) return false;
            lastSequence = _previewSeq;
            sink(_previewFront, _previewWidth, _previewHeight);
            return true;
        }
    }

    public bool TryDequeueEvent(out GestureEvent gestureEvent) => _eventQueue.TryDequeue(out gestureEvent!);

    public LatencySummary GetLatency() => _latency.Summarize(FramesProcessed, _slot?.Replaced ?? 0, Fps);

    // ------------------------------------------------------------------ processing thread

    private bool ActionsArmed() => _armed && Volatile.Read(ref _recordLabel) < 0;

    private void OnFrame(ReadOnlySpan<byte> data, FrameLayout layout, long timestamp100ns) =>
        _slot?.Publish(data, layout, timestamp100ns);

    private void OnCameraFailed(string message)
    {
        LastError = message;
        _log.Error($"Camera failed: {message}");
        SetState(EngineState.Faulted, $"The camera stopped: {message}");
    }

    private void Loop()
    {
        var decision = Scheduling ?? SchedulingPolicy.Decide(SchedulingMode.Normal, Environment.ProcessorCount);
        IntPtr mmcss = IntPtr.Zero;
        try
        {
            Thread.CurrentThread.Priority = decision.Priority;
            if (decision.UseMmcss)
            {
                mmcss = Native.EnterMmcss("Games");
                if (mmcss == IntPtr.Zero) _log.Warn("MMCSS registration was refused; running at thread priority only.");
            }
            Native.DisableThreadPowerThrottling();
        }
        catch (Exception ex)
        {
            _log.Warn($"Real-time scheduling unavailable: {ex.Message}");
        }

        try
        {
            var slot = _slot!;
            while (!_stop)
            {
                var frame = slot.Take(250);
                if (frame is null) continue;
                try
                {
                    if (!_faulted)
                    {
                        ProcessFrame(frame);
                        _consecutiveErrors = 0;
                    }
                }
                catch (Exception ex)
                {
                    HandleError(ex, frame.Layout);
                }
                finally
                {
                    slot.Return(frame);
                }
            }
        }
        finally
        {
            if (mmcss != IntPtr.Zero) Native.LeaveMmcss(mmcss);
        }
    }

    private void ProcessFrame(FrameBuffer frame)
    {
        var tracker = _tracker!;
        var layout = frame.Layout;
        if (tracker.Layout != layout) Prepare(tracker, layout);
        if (_mirrorChanged)
        {
            _mirrorChanged = false;
            _preview?.SetMirror(_mirror);
        }
        ApplyPendingProfile();

        long start = QpcClock.Now100ns();
        double startMs = QpcClock.ToMs(start);
        if (!double.IsNaN(_lastFrameMs))
        {
            double interval = startMs - _lastFrameMs;
            if (interval > 0 && interval < 1000) _intervalEma = _intervalEma <= 0 ? interval : _intervalEma * 0.9 + interval * 0.1;
        }
        _lastFrameMs = startMs;

        // GPU: palm detection only when not tracking, then landmarks + Google's gesture embedder + both classifiers.
        tracker.Process(frame.Value, _obs);
        long afterGpu = QpcClock.Now100ns();

        bool recording;
        int recorded;
        lock (_recordGate)
        {
            recording = _recordLabel >= 0;
            if (recording && _obs.HasHand) _recorded.Add((float[])_obs.GestureInput.Clone());
            recorded = _recorded.Count;
        }

        // Your gestures first, then Google's ready-made ones with whatever share yours leave.
        var active = _active;
        if (_obs.HasHand) _fusion.Fuse(active, _gestures, _obs.Ready, _obs.Own, _obs.Embedding, _fused);
        else Array.Clear(_fused);

        double cameraMs = CameraLatency(frame.CameraTime100ns, afterGpu);
        double sinceFrame = double.IsNaN(cameraMs) ? QpcClock.ToMs(afterGpu - frame.ArrivedTime100ns) : cameraMs;
        float wristY = _obs.HasHand ? Math.Clamp(_obs.Landmarks[1] / Math.Max(1, layout.Height), 0f, 1f) : 0.5f;
        _events.Clear();
        var snapshot = _decider!.Update(_obs.HasHand, _fused, wristY, QpcClock.ToMs(afterGpu), sinceFrame, _events);
        long decided = QpcClock.Now100ns();

        foreach (var e in _events) Publish(e);

        double cameraToDecision = double.IsNaN(cameraMs) ? double.NaN : QpcClock.ToMs(decided - frame.CameraTime100ns);
        double arrivalToDecision = QpcClock.ToMs(decided - frame.ArrivedTime100ns);
        _latency.Add(cameraToDecision, arrivalToDecision, _obs.PalmMs + _obs.HandMs);
        long frames = Interlocked.Increment(ref _frames);
        if (decided - _liveAt100ns >= LiveLogEvery100ns) LogLive(decided, frames);

        var r = _reportBack;
        r.Sequence = frame.Sequence;
        r.Width = layout.Width;
        r.Height = layout.Height;
        r.Mirror = _mirror;
        r.HasHand = _obs.HasHand;
        r.Detected = _obs.Detected;
        r.Presence = _obs.Presence;
        r.Handedness = _obs.Handedness;
        r.PalmScore = _obs.PalmScore;
        r.Roi = _obs.Roi;
        Array.Copy(_obs.Landmarks, r.Landmarks, r.Landmarks.Length);
        Array.Copy(_obs.Ready, r.Ready, r.Ready.Length);
        Array.Copy(_obs.Own, r.Own, r.Own.Length);
        Array.Copy(_fusion.Similarity, r.Similarity, r.Similarity.Length);
        Array.Copy(_fused, r.Fused, r.Fused.Length);
        r.OwnShare = _obs.HasHand ? _fusion.OwnShare : 0;
        Array.Clear(r.Smoothed);
        Array.Copy(snapshot.Smoothed, r.Smoothed, Math.Min(snapshot.Smoothed.Length, r.Smoothed.Length));
        r.ActiveClass = snapshot.ActiveClass;
        r.CandidateClass = snapshot.CandidateClass;
        r.GpuRuns = _obs.GpuRuns;
        r.PalmMs = _obs.PalmMs;
        r.HandMs = _obs.HandMs;
        r.CameraToDecisionMs = cameraToDecision;
        r.ArrivalToDecisionMs = arrivalToDecision;
        r.Fps = Fps;
        r.Recording = recording;
        r.RecordedSamples = recorded;
        r.When = DateTime.Now;
        lock (_reportGate)
        {
            (_reportBack, _reportFront) = (_reportFront, _reportBack);
            _reportSeq = frame.Sequence;
        }

        // The preview is rendered after the decision, so it never delays an action.
        if (_preview is not null && _previewWanted) RenderPreview(frame);

        if (frames == 1)
            _log.Info($"First frame processed: GPU {_obs.PalmMs + _obs.HandMs:0.0} ms (first run, includes warm-up); it waited {arrivalToDecision:0} ms for the one-time GPU graph build. Live figures follow every 15 s.");
    }

    /// <summary>Every 15 s while running: what the camera delivers versus what HandGI processes, and where the time goes.</summary>
    private void LogLive(long now100ns, long frames)
    {
        var camera = _camera;
        long arrived = camera?.FramesArrived ?? 0;
        if (_liveAt100ns == 0)
        {
            _liveAt100ns = now100ns;
            _liveArrived = arrived;
            _liveFrames = frames;
            return;
        }
        double seconds = (now100ns - _liveAt100ns) / 1e7;
        double cameraFps = (arrived - _liveArrived) / seconds;
        double processedFps = (frames - _liveFrames) / seconds;
        _liveAt100ns = now100ns;
        _liveArrived = arrived;
        _liveFrames = frames;

        var l = GetLatency();
        long busy = camera?.FramesSkippedBusy ?? 0;
        string total = l.CameraClock
            ? $"camera → decision p50 {l.P50:0.0} ms (p95 {l.P95:0.0}), of which inside HandGI p50 {l.ArrivalP50:0.0} ms (p95 {l.ArrivalP95:0.0})"
            : $"arrival → decision p50 {l.ArrivalP50:0.0} ms (p95 {l.ArrivalP95:0.0})";
        _log.Info($"Live: the camera delivers {cameraFps:0.0} fps, HandGI processes {processedFps:0.0} fps " +
                  $"({l.Replaced} skipped for a newer frame{(busy > 0 ? $", {busy} while copying" : "")}); {total}; GPU p50 {l.GpuP50:0.0} ms (p95 {l.GpuP95:0.0}).");
    }

    private void Prepare(HandTracker tracker, FrameLayout layout)
    {
        if (layout.ByteLength > MaxFrameBytes)
        {
            throw new NotSupportedException(
                $"The camera mode {layout} needs {layout.ByteLength / 1048576.0:0.0} MB per frame; the GPU sampler reads up to 16 MB. Choose 1920x1080 or smaller.");
        }
        SetState(EngineState.Preparing, $"Building the GPU graphs for {layout}...");
        _preview?.Dispose();
        _preview = null;
        tracker.Prepare(layout);
        if (_options.Preview)
        {
            try
            {
                _preview = new PreviewRenderer(layout, _options.PreviewMaxWidth, _mirror, _trackerOptions);
            }
            catch (Exception ex)
            {
                _log.Warn($"Preview disabled: {ex.Message}");
            }
        }
        _intervalEma = 0;
        _lastFrameMs = double.NaN;
        string device = _options.DxgiIndex < 0
            ? "the CPU (compatibility mode)"
            : string.IsNullOrEmpty(_options.DeviceName) ? $"GPU adapter {_options.DxgiIndex}" : _options.DeviceName;
        SetState(EngineState.Running, $"Running on {device} · {layout} · GPU graphs built in {tracker.LastBuildMs:0} ms");
    }

    private void ApplyPendingProfile()
    {
        var profile = Interlocked.Exchange(ref _pendingProfile, null);
        if (profile is null) return;
        _active = profile;
        _decider = new GestureDecider(profile, _volume, ActionsArmed);
        int readyOn = profile.Classes.Count(c => c.IsReadyMade && c.Enabled);
        string mine = profile.MineCount == 0 ? "none of your own"
            : $"{profile.MineCount} of your own ({(profile.IsTrained ? "learned" : "not learned yet")})";
        _log.Info($"Profile '{profile.Name}' active: {readyOn} ready-made gestures on, {mine}.");
    }

    private void RenderPreview(FrameBuffer frame)
    {
        var preview = _preview!;
        try
        {
            preview.Render(frame.Value);
        }
        catch (Exception ex)
        {
            _log.Warn($"Preview stopped: {ex.Message}");
            preview.Dispose();
            _preview = null;
            return;
        }

        var pixels = preview.Pixels;
        lock (_previewGate)
        {
            if (_previewFront.Length != pixels.Length) _previewFront = new byte[pixels.Length];
            Buffer.BlockCopy(pixels, 0, _previewFront, 0, pixels.Length);
            _previewWidth = preview.Width;
            _previewHeight = preview.Height;
            _previewSeq = frame.Sequence;
        }
    }

    private double CameraLatency(long cameraTime100ns, long now100ns)
    {
        double ms = QpcClock.ToMs(now100ns - cameraTime100ns);
        if (ms >= 0 && ms < 5000) return ms;
        if (!_cameraClockWarned)
        {
            _cameraClockWarned = true;
            _log.Warn($"Camera timestamps are not on the QPC clock (offset {ms:0} ms); latency is measured from frame arrival instead.");
        }
        return double.NaN;
    }

    private void Publish(GestureEvent e)
    {
        _eventQueue.Enqueue(e);
        while (_eventQueue.Count > EventQueueLimit && _eventQueue.TryDequeue(out _)) { }
        if (e.Kind == GestureEventKind.Action)
            _log.Info($"Gesture {e.ClassName}: {e.Description}{(e.DryRun ? " (dry run)" : "")} · {e.FrameToDecisionMs:0} ms after capture");
        try
        {
            GestureRaised?.Invoke(e);
        }
        catch (Exception ex)
        {
            _log.Warn($"Gesture handler failed: {ex.Message}");
        }
    }

    private void HandleError(Exception ex, FrameLayout layout)
    {
        if (_tracker is null || _tracker.Layout != layout)
        {
            _faulted = true;
            LastError = ex.Message;
            _log.Error($"Could not prepare the GPU pipeline for {layout}", ex);
            _log.Debug(ex.ToString());
            SetState(EngineState.Faulted, ex.Message);
            return;
        }

        _consecutiveErrors++;
        long now = Environment.TickCount64;
        if (now - _lastErrorLog > 3000)
        {
            _lastErrorLog = now;
            _log.Error("Frame processing failed", ex);
            _log.Debug(ex.ToString());
        }
        if (_consecutiveErrors >= 30)
        {
            _faulted = true;
            LastError = ex.Message;
            SetState(EngineState.Faulted, $"Processing keeps failing: {ex.Message}");
        }
    }

    private void SetState(EngineState state, string message)
    {
        lock (_stateGate)
        {
            _state = state;
            _stateMessage = message;
        }
        RaiseState(state, message);
    }

    private void SetStateIf(EngineState expected, EngineState state, string message)
    {
        lock (_stateGate)
        {
            if (_state != expected) return;
            _state = state;
            _stateMessage = message;
        }
        RaiseState(state, message);
    }

    private void RaiseState(EngineState state, string message)
    {
        _log.Info($"Engine {state}: {message}");
        try
        {
            StateChanged?.Invoke(state, message);
        }
        catch (Exception ex)
        {
            _log.Warn($"State handler failed: {ex.Message}");
        }
    }
}
