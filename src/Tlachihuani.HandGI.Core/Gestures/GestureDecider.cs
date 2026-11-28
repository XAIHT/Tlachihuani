// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using HandGI.Core.Audio;
using HandGI.Core.Vision;

namespace HandGI.Core.Gestures;

public enum GestureEventKind { Started, Ended, Action }

/// <summary>Something the decision layer did, for the tester log, the OSD and the service host.</summary>
public sealed record GestureEvent(
    GestureEventKind Kind,
    int ClassIndex,
    string ClassName,
    GestureAction Action,
    string Description,
    float Volume,
    bool DryRun,
    double FrameToDecisionMs,
    DateTime When);

/// <summary>The decision state after one frame (copied for the UI).</summary>
public sealed record DecisionSnapshot(int ActiveClass, int CandidateClass, float[] Smoothed);

/// <summary>
/// Turns noisy per-frame probabilities into deliberate actions: exponential smoothing, an enter
/// threshold held for N frames, a lower exit threshold (hysteresis), repeat with acceleration,
/// cool-down after every gesture, and a dry-run mode when HandGI is not armed.
/// Pure CPU bookkeeping on one number per gesture per frame.
/// </summary>
public sealed class GestureDecider
{
    private readonly GestureProfile _profile;
    private readonly IVolumeSink _volume;
    private readonly Func<bool> _armed;
    private readonly float[] _smoothed = new float[HandGraphs.MaxGestures];
    private int _active = -1;
    private int _candidate = -1;
    private int _candidateFrames;
    private double _cooldownUntilMs;
    private double _enteredAtMs;
    private double _nextRepeatMs;
    private float _leverStartVolume;
    private float _leverStartY;
    private float _leverLastSet = -1f;

    public GestureDecider(GestureProfile profile, IVolumeSink volume, Func<bool> armed)
    {
        _profile = profile;
        _volume = volume;
        _armed = armed;
    }

    public int ActiveClass => _active;

    /// <param name="hasHand">Whether a hand is tracked in this frame.</param>
    /// <param name="probs">One probability per gesture of the profile (see <see cref="GestureFusion"/>).</param>
    /// <param name="wristY">Wrist y normalised to 0 (top) .. 1 (bottom).</param>
    /// <param name="nowMs">Monotonic time in ms.</param>
    /// <param name="frameToNowMs">Latency from the camera timestamp to now (reported with actions).</param>
    public DecisionSnapshot Update(bool hasHand, ReadOnlySpan<float> probs, float wristY, double nowMs, double frameToNowMs, List<GestureEvent> events)
    {
        int k = Math.Min(_profile.Classes.Count, Math.Min(probs.Length, _smoothed.Length));
        var d = _profile.Decision;

        if (!_profile.IsUsable || k < 1)
        {
            if (_active >= 0) End(nowMs, frameToNowMs, events);
            return Snapshot();
        }

        if (!hasHand)
        {
            if (_active >= 0) End(nowMs, frameToNowMs, events);
            Array.Clear(_smoothed);
            _candidate = -1;
            _candidateFrames = 0;
            return Snapshot();
        }

        float alpha = Math.Clamp(d.Smoothing, 0.05f, 1f);
        int best = 0;
        for (int i = 0; i < k; i++)
        {
            _smoothed[i] = alpha * probs[i] + (1 - alpha) * _smoothed[i];
            if (_smoothed[i] > _smoothed[best]) best = i;
        }

        if (_active < 0)
        {
            if (nowMs < _cooldownUntilMs) return Snapshot();
            if (_smoothed[best] >= d.EnterThreshold)
            {
                if (_candidate == best) _candidateFrames++;
                else
                {
                    _candidate = best;
                    _candidateFrames = 1;
                }
                if (_candidateFrames >= Math.Max(1, d.EnterFrames)) Start(best, wristY, nowMs, frameToNowMs, events);
            }
            else
            {
                _candidate = -1;
                _candidateFrames = 0;
            }
        }
        else
        {
            bool fading = _smoothed[_active] < d.ExitThreshold;
            bool overtaken = best != _active && _smoothed[best] >= d.EnterThreshold && _smoothed[best] > _smoothed[_active] + 0.1f;
            if (fading || overtaken) End(nowMs, frameToNowMs, events);
            else Hold(wristY, nowMs, frameToNowMs, events);
        }

        return Snapshot();
    }

    private DecisionSnapshot Snapshot() => new(_active, _candidate, (float[])_smoothed.Clone());

    private void Start(int cls, float wristY, double nowMs, double latencyMs, List<GestureEvent> events)
    {
        _active = cls;
        _candidate = -1;
        _candidateFrames = 0;
        _enteredAtMs = nowMs;
        _nextRepeatMs = nowMs + _profile.Decision.RepeatMs;
        var c = _profile.Classes[cls];
        events.Add(new GestureEvent(GestureEventKind.Started, cls, c.Name, c.Action, $"{c.Name} started", _volume.Level, !_armed(), latencyMs, DateTime.Now));

        switch (c.Action)
        {
            case GestureAction.VolumeUp:
                Step(cls, +_profile.Decision.Step, latencyMs, events);
                break;
            case GestureAction.VolumeDown:
                Step(cls, -_profile.Decision.Step, latencyMs, events);
                break;
            case GestureAction.MuteToggle:
            {
                bool dry = !_armed();
                bool willMute = !_volume.Muted;
                if (!dry) _volume.ToggleMute();
                events.Add(new GestureEvent(GestureEventKind.Action, cls, c.Name, c.Action, willMute ? "Mute" : "Unmute", _volume.Level, dry, latencyMs, DateTime.Now));
                break;
            }
            case GestureAction.VolumeLever:
                _leverStartVolume = _volume.Level;
                _leverStartY = wristY;
                _leverLastSet = _leverStartVolume;
                break;
        }
    }

    private void Hold(float wristY, double nowMs, double latencyMs, List<GestureEvent> events)
    {
        var c = _profile.Classes[_active];
        var d = _profile.Decision;
        switch (c.Action)
        {
            case GestureAction.VolumeUp:
            case GestureAction.VolumeDown:
                if (nowMs >= _nextRepeatMs)
                {
                    float step = nowMs - _enteredAtMs >= d.AccelerateAfterMs ? d.AcceleratedStep : d.Step;
                    Step(_active, c.Action == GestureAction.VolumeUp ? step : -step, latencyMs, events);
                    _nextRepeatMs += d.RepeatMs;
                    if (_nextRepeatMs < nowMs) _nextRepeatMs = nowMs + d.RepeatMs;
                }
                break;
            case GestureAction.VolumeLever:
            {
                float target = Math.Clamp(_leverStartVolume + d.LeverGain * (_leverStartY - wristY), 0f, d.MaxVolume);
                if (MathF.Abs(target - _leverLastSet) >= 0.01f)
                {
                    bool dry = !_armed();
                    if (!dry) _volume.SetLevel(target);
                    _leverLastSet = target;
                    events.Add(new GestureEvent(GestureEventKind.Action, _active, c.Name, c.Action, $"Lever → {target:P0}", target, dry, latencyMs, DateTime.Now));
                }
                break;
            }
        }
    }

    private void Step(int cls, float delta, double latencyMs, List<GestureEvent> events)
    {
        var c = _profile.Classes[cls];
        float current = _volume.Level;
        float target = Math.Clamp(current + delta, 0f, _profile.Decision.MaxVolume);
        if (delta > 0 && current > _profile.Decision.MaxVolume) target = current; // never lower a manual level
        bool dry = !_armed();
        if (!dry) _volume.SetLevel(target);
        events.Add(new GestureEvent(GestureEventKind.Action, cls, c.Name, c.Action, $"{(delta > 0 ? "+" : "−")}{MathF.Abs(delta):P0} → {target:P0}", target, dry, latencyMs, DateTime.Now));
    }

    private void End(double nowMs, double latencyMs, List<GestureEvent> events)
    {
        if (_active < 0) return;
        var c = _profile.Classes[_active];
        events.Add(new GestureEvent(GestureEventKind.Ended, _active, c.Name, c.Action, $"{c.Name} ended", _volume.Level, !_armed(), latencyMs, DateTime.Now));
        _active = -1;
        _cooldownUntilMs = nowMs + _profile.Decision.CooldownMs;
    }
}
