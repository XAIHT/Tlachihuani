// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using HandGI.Core.Gestures;
using HandGI.Core.Gpu;
using HandGI.Core.Util;
using Microsoft.ML.OnnxRuntime;

namespace HandGI.Core.Vision;

public sealed class HandTrackerOptions
{
    public int DxgiIndex { get; init; }
    public bool AllowCpuFallback { get; init; }
    public YuvRange Range { get; init; } = YuvRange.Limited;
    public float PalmThreshold { get; init; } = 0.5f;
    public float PresenceThreshold { get; init; } = 0.5f;
}

/// <summary>What the tracker saw in one frame. Owned by the caller and reused frame after frame.</summary>
public sealed class HandObservation
{
    public bool HasHand;
    public bool Detected;
    public float Presence;
    public float Handedness;
    public float PalmScore;
    public RotatedRect Roi;
    public double PalmMs;
    public double HandMs;
    public int GpuRuns;
    public readonly float[] Landmarks = new float[HandGraphs.LandmarkCount * 3];
    /// <summary>The landmarks in MediaPipe's object-normalised form: what a recorded example stores.</summary>
    public readonly float[] GestureInput = new float[HandGraphs.GestureInputCount];
    /// <summary>Google's 128 numbers describing the hand shape.</summary>
    public readonly float[] Embedding = new float[HandGraphs.EmbeddingSize];
    /// <summary>Google's ready-made classifier: None + 7 gestures.</summary>
    public readonly float[] Ready = new float[HandGraphs.ReadyCount];
    /// <summary>Your own gestures' head: index 0 = not one of yours, then your gestures in order.</summary>
    public readonly float[] Own = new float[HandGraphs.MaxClasses];

    public void Clear()
    {
        HasHand = false;
        Detected = false;
        Presence = 0;
        Handedness = 0;
        PalmScore = 0;
        Roi = default;
        PalmMs = 0;
        HandMs = 0;
        GpuRuns = 0;
    }
}

/// <summary>
/// MediaPipe-style hand tracking on the GPU: the palm detector runs only when no hand is being
/// tracked; otherwise the next crop comes from this frame's landmarks, so steady state is a single
/// GPU dispatch per frame (crop + landmarks + features + gesture classifier).
/// If tracking is lost, detection re-runs on the same frame, so a lost hand costs no extra frame.
/// </summary>
public sealed class HandTracker : IDisposable
{
    private readonly byte[] _palmModel;
    private readonly byte[] _handModel;
    private readonly MediaPipeGestureModel _gestures;
    private readonly HandTrackerOptions _options;
    private readonly ILog _log;
    private readonly RunOptions _run = new();
    private readonly object _pendingGate = new();

    private readonly (float[] B, OrtValue V) _palmAffine = OrtFactory.PinnedFloat(2, 3);
    private readonly (float[] B, OrtValue V) _palmOut = OrtFactory.PinnedFloat(HandGraphs.Candidates, HandGraphs.CandidateStride);
    private readonly (float[] B, OrtValue V) _roiAffine = OrtFactory.PinnedFloat(2, 3);
    private readonly (float[] B, OrtValue V) _roiScale = OrtFactory.PinnedFloat(1);
    private readonly (float[] B, OrtValue V) _landmarks = OrtFactory.PinnedFloat(HandGraphs.LandmarkCount, 3);
    private readonly (float[] B, OrtValue V) _presence = OrtFactory.PinnedFloat(1, 1);
    private readonly (float[] B, OrtValue V) _handedness = OrtFactory.PinnedFloat(1, 1);
    private readonly (float[] B, OrtValue V) _gestureInput = OrtFactory.PinnedFloat(1, HandGraphs.GestureInputCount);
    private readonly (float[] B, OrtValue V) _embedding = OrtFactory.PinnedFloat(1, HandGraphs.EmbeddingSize);
    private readonly (float[] B, OrtValue V) _ready = OrtFactory.PinnedFloat(1, HandGraphs.ReadyCount);
    private readonly (float[] B, OrtValue V) _probs = OrtFactory.PinnedFloat(1, HandGraphs.MaxClasses);
    private readonly (float[] B, OrtValue V) _mean = OrtFactory.PinnedFloat(1, HandGraphs.EmbeddingSize);
    private readonly (float[] B, OrtValue V) _invStd = OrtFactory.PinnedFloat(1, HandGraphs.EmbeddingSize);
    private readonly (float[] B, OrtValue V) _w1 = OrtFactory.PinnedFloat(HandGraphs.EmbeddingSize, HandGraphs.Hidden1);
    private readonly (float[] B, OrtValue V) _b1 = OrtFactory.PinnedFloat(1, HandGraphs.Hidden1);
    private readonly (float[] B, OrtValue V) _w2 = OrtFactory.PinnedFloat(HandGraphs.Hidden1, HandGraphs.Hidden2);
    private readonly (float[] B, OrtValue V) _b2 = OrtFactory.PinnedFloat(1, HandGraphs.Hidden2);
    private readonly (float[] B, OrtValue V) _w3 = OrtFactory.PinnedFloat(HandGraphs.Hidden2, HandGraphs.MaxClasses);
    private readonly (float[] B, OrtValue V) _b3 = OrtFactory.PinnedFloat(1, HandGraphs.MaxClasses);
    private readonly (float[] B, OrtValue V) _mask = OrtFactory.PinnedFloat(1, HandGraphs.MaxClasses);

    private InferenceSession? _palm;
    private InferenceSession? _hand;
    private OrtIoBinding? _palmBinding;
    private OrtIoBinding? _handBinding;
    private FrameLayout? _layout;
    private bool _tracking;
    private RotatedRect _roi;

    private ClassifierWeights? _pendingWeights;
    private int _pendingClassCount;
    private bool _hasPending = true;

    public HandTracker(byte[] palmModel, byte[] handModel, MediaPipeGestureModel gestures, HandTrackerOptions options, ILog log)
    {
        _palmModel = palmModel;
        _handModel = handModel;
        _gestures = gestures;
        _options = options;
        _log = log;
    }

    public FrameLayout? Layout => _layout;
    public bool IsTracking => _tracking;
    public double LastBuildMs { get; private set; }

    /// <summary>Builds (or rebuilds) the GPU sessions for a frame layout. Takes ~0.5–2 s the first time.</summary>
    public void Prepare(FrameLayout layout)
    {
        if (_layout == layout && _palm is not null) return;
        DisposeSessions();
        var t0 = QpcClock.Now100ns();

        _palm = OrtFactory.CreateSession(HandGraphs.BuildPalm(_palmModel, layout, _options.Range), _options.DxgiIndex, _options.AllowCpuFallback, "palm");
        _hand = OrtFactory.CreateSession(HandGraphs.BuildHand(_handModel, layout, _options.Range, _gestures), _options.DxgiIndex, _options.AllowCpuFallback, "hand");

        // ONNX Runtime copies a CPU input to the GPU at the moment it is bound, so every input is filled
        // before it is bound and bound again whenever it changes (see RunHand and ApplyPendingClassifier).
        HandGeometry.Letterbox(layout.Width, layout.Height, HandGraphs.PalmSize).CopyTo(_palmAffine.B, 0);
        _palmBinding = _palm.CreateIoBinding();
        _palmBinding.BindInput(HandGraphs.PalmAffine, _palmAffine.V);
        _palmBinding.BindOutput(HandGraphs.PalmOut, _palmOut.V);

        _handBinding = _hand.CreateIoBinding();
        _handBinding.BindInput(HandGraphs.RoiAffine, _roiAffine.V);
        _handBinding.BindInput(HandGraphs.RoiScale, _roiScale.V);
        _handBinding.BindInput(HandGraphs.ClfMean, _mean.V);
        _handBinding.BindInput(HandGraphs.ClfInvStd, _invStd.V);
        _handBinding.BindInput(HandGraphs.ClfW1, _w1.V);
        _handBinding.BindInput(HandGraphs.ClfB1, _b1.V);
        _handBinding.BindInput(HandGraphs.ClfW2, _w2.V);
        _handBinding.BindInput(HandGraphs.ClfB2, _b2.V);
        _handBinding.BindInput(HandGraphs.ClfW3, _w3.V);
        _handBinding.BindInput(HandGraphs.ClfB3, _b3.V);
        _handBinding.BindInput(HandGraphs.ClfMask, _mask.V);
        _handBinding.BindOutput(HandGraphs.OutLandmarks, _landmarks.V);
        _handBinding.BindOutput(HandGraphs.OutPresence, _presence.V);
        _handBinding.BindOutput(HandGraphs.OutHandedness, _handedness.V);
        _handBinding.BindOutput(HandGraphs.OutGestureInput, _gestureInput.V);
        _handBinding.BindOutput(HandGraphs.OutEmbedding, _embedding.V);
        _handBinding.BindOutput(HandGraphs.OutReady, _ready.V);
        _handBinding.BindOutput(HandGraphs.OutProbs, _probs.V);

        _layout = layout;
        _tracking = false;
        lock (_pendingGate) _hasPending = true; // bind the classifier weights to the new session on the next frame
        LastBuildMs = QpcClock.MsSince(t0);
        _log.Info($"GPU graphs ready for {layout} in {LastBuildMs:0} ms (palm + hand, DirectML adapter {_options.DxgiIndex}).");
    }

    /// <summary>Queues new classifier weights; applied at the start of the next frame (thread-safe).</summary>
    public void SetClassifier(ClassifierWeights? weights, int classCount)
    {
        lock (_pendingGate)
        {
            _pendingWeights = weights;
            _pendingClassCount = classCount;
            _hasPending = true;
        }
    }

    public void ResetTracking() => _tracking = false;

    /// <summary>Runs one frame. <paramref name="frame"/> must view the raw camera bytes for the prepared layout.</summary>
    public void Process(OrtValue frame, HandObservation observation)
    {
        if (_layout is null || _palm is null || _hand is null) throw new InvalidOperationException("Call Prepare first.");
        observation.Clear();
        ApplyPendingClassifier();

        if (!_tracking && !Detect(frame, observation)) return;

        RunHand(frame, observation);
        if (observation.Presence < _options.PresenceThreshold)
        {
            if (!_tracking) return; // a fresh detection that did not hold up
            _tracking = false;      // lost: re-detect on this same frame
            if (!Detect(frame, observation)) return;
            RunHand(frame, observation);
            if (observation.Presence < _options.PresenceThreshold) return;
        }

        observation.HasHand = true;
        Array.Copy(_landmarks.B, observation.Landmarks, observation.Landmarks.Length);
        Array.Copy(_gestureInput.B, observation.GestureInput, observation.GestureInput.Length);
        Array.Copy(_embedding.B, observation.Embedding, observation.Embedding.Length);
        Array.Copy(_ready.B, observation.Ready, observation.Ready.Length);
        Array.Copy(_probs.B, observation.Own, observation.Own.Length);

        var next = HandGeometry.FromLandmarks(observation.Landmarks);
        if (HandGeometry.IsUsable(next, _layout.Width, _layout.Height))
        {
            _roi = next;
            _tracking = true;
        }
        else
        {
            _tracking = false;
        }
    }

    private bool Detect(OrtValue frame, HandObservation observation)
    {
        long t0 = QpcClock.Now100ns();
        _palmBinding!.BindInput(HandGraphs.Frame, frame);
        _palm!.RunWithBinding(_run, _palmBinding);
        observation.PalmMs += QpcClock.MsSince(t0);
        observation.GpuRuns++;
        observation.Detected = true;

        var c = _palmOut.B; // candidates sorted by score (TopK)
        observation.PalmScore = c[0];
        if (!(c[0] >= _options.PalmThreshold)) return false;
        var rect = HandGeometry.FromPalm(c.AsSpan(0, HandGraphs.CandidateStride));
        if (!HandGeometry.IsUsable(rect, _layout!.Width, _layout.Height)) return false;
        _roi = rect;
        return true;
    }

    private void RunHand(OrtValue frame, HandObservation observation)
    {
        var (affine, scale) = HandGeometry.CropAffine(_roi, HandGraphs.HandSize);
        affine.CopyTo(_roiAffine.B, 0);
        _roiScale.B[0] = scale;
        long t0 = QpcClock.Now100ns();
        _handBinding!.BindInput(HandGraphs.RoiAffine, _roiAffine.V);
        _handBinding.BindInput(HandGraphs.RoiScale, _roiScale.V);
        _handBinding.BindInput(HandGraphs.Frame, frame);
        _hand!.RunWithBinding(_run, _handBinding);
        observation.HandMs += QpcClock.MsSince(t0);
        observation.GpuRuns++;
        observation.Presence = _presence.B[0];
        observation.Handedness = _handedness.B[0];
        observation.Roi = _roi;
    }

    private void ApplyPendingClassifier()
    {
        ClassifierWeights? weights;
        int classCount;
        lock (_pendingGate)
        {
            if (!_hasPending) return;
            weights = _pendingWeights;
            classCount = _pendingClassCount;
            _hasPending = false;
        }

        if (weights is null)
        {
            Array.Clear(_mean.B);
            Array.Fill(_invStd.B, 1f);
            Array.Clear(_w1.B);
            Array.Clear(_b1.B);
            Array.Clear(_w2.B);
            Array.Clear(_b2.B);
            Array.Clear(_w3.B);
            Array.Clear(_b3.B);
        }
        else
        {
            weights.Mean.CopyTo(_mean.B, 0);
            weights.InvStd.CopyTo(_invStd.B, 0);
            weights.W1.CopyTo(_w1.B, 0);
            weights.B1.CopyTo(_b1.B, 0);
            weights.W2.CopyTo(_w2.B, 0);
            weights.B2.CopyTo(_b2.B, 0);
            weights.W3.CopyTo(_w3.B, 0);
            weights.B3.CopyTo(_b3.B, 0);
        }

        int active = Math.Clamp(classCount, 1, HandGraphs.MaxClasses);
        for (int i = 0; i < HandGraphs.MaxClasses; i++) _mask.B[i] = i < active ? 0f : -1e4f;

        // The weights changed on the CPU: bind them again so the GPU copy is refreshed.
        var b = _handBinding;
        if (b is null) return;
        b.BindInput(HandGraphs.ClfMean, _mean.V);
        b.BindInput(HandGraphs.ClfInvStd, _invStd.V);
        b.BindInput(HandGraphs.ClfW1, _w1.V);
        b.BindInput(HandGraphs.ClfB1, _b1.V);
        b.BindInput(HandGraphs.ClfW2, _w2.V);
        b.BindInput(HandGraphs.ClfB2, _b2.V);
        b.BindInput(HandGraphs.ClfW3, _w3.V);
        b.BindInput(HandGraphs.ClfB3, _b3.V);
        b.BindInput(HandGraphs.ClfMask, _mask.V);
    }

    private void DisposeSessions()
    {
        _palmBinding?.Dispose();
        _handBinding?.Dispose();
        _palm?.Dispose();
        _hand?.Dispose();
        _palmBinding = _handBinding = null;
        _palm = _hand = null;
        _layout = null;
    }

    public void Dispose()
    {
        DisposeSessions();
        _run.Dispose();
        foreach (var v in new[] { _palmAffine, _palmOut, _roiAffine, _roiScale, _landmarks, _presence, _handedness, _gestureInput, _embedding, _ready, _probs, _mean, _invStd, _w1, _b1, _w2, _b2, _w3, _b3, _mask })
            v.V.Dispose();
    }
}

/// <summary>Renders the camera preview on the GPU (scaled, mirrored, BGRA) so the window never converts pixels on the CPU.</summary>
public sealed class PreviewRenderer : IDisposable
{
    private readonly InferenceSession _session;
    private readonly OrtIoBinding _binding;
    private readonly RunOptions _run = new();
    private readonly (float[] B, OrtValue V) _affine = OrtFactory.PinnedFloat(2, 3);
    private readonly (byte[] B, OrtValue V) _pixels;

    public int Width { get; }
    public int Height { get; }
    public FrameLayout Layout { get; }
    public byte[] Pixels => _pixels.B;

    public PreviewRenderer(FrameLayout layout, int maxWidth, bool mirror, HandTrackerOptions options)
    {
        Layout = layout;
        Width = Math.Min(maxWidth, layout.Width) & ~1;
        Height = Math.Max(2, (int)Math.Round(Width * (double)layout.Height / layout.Width) & ~1);
        _pixels = OrtFactory.PinnedBytes(Height, Width, 4);
        HandGeometry.Preview(layout.Width, layout.Height, Width, Height, mirror).CopyTo(_affine.B, 0);
        _session = OrtFactory.CreateSession(HandGraphs.BuildPreview(layout, Width, Height, options.Range), options.DxgiIndex, options.AllowCpuFallback, "preview");
        _binding = _session.CreateIoBinding();
        _binding.BindInput(HandGraphs.PreviewAffine, _affine.V);
        _binding.BindOutput(HandGraphs.PreviewOut, _pixels.V);
    }

    public void SetMirror(bool mirror)
    {
        HandGeometry.Preview(Layout.Width, Layout.Height, Width, Height, mirror).CopyTo(_affine.B, 0);
        _binding.BindInput(HandGraphs.PreviewAffine, _affine.V); // CPU inputs reach the GPU when bound
    }

    public void Render(OrtValue frame)
    {
        _binding.BindInput(HandGraphs.Frame, frame);
        _session.RunWithBinding(_run, _binding);
    }

    public void Dispose()
    {
        _binding.Dispose();
        _session.Dispose();
        _run.Dispose();
        _affine.V.Dispose();
        _pixels.V.Dispose();
    }
}
