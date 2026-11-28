// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using HandGI.Core.Util;

namespace HandGI.Core.Audio;

/// <summary>Where the decision layer sends volume changes. Calls never block the real-time thread.</summary>
public interface IVolumeSink
{
    /// <summary>Cached master volume 0..1 (refreshed after every change and periodically).</summary>
    float Level { get; }
    bool Muted { get; }
    void SetLevel(float level);
    void ToggleMute();
    event Action<float, bool>? Changed;
}

/// <summary>
/// Windows master volume through Core Audio (IAudioEndpointVolume) on a dedicated MTA worker thread:
/// the gesture thread only enqueues, so a slow audio driver can never stall tracking.
/// </summary>
public sealed class VolumeController : IVolumeSink, IDisposable
{
    private static readonly Guid EventContext = new("6f1c2a10-8b5e-4c11-9a3d-527e1044c29b");
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _worker;
    private readonly ILog _log;
    private IAudioEndpointVolume? _endpoint;
    private volatile float _level;
    private volatile bool _muted;
    private DateTime _lastRefresh = DateTime.MinValue;

    public event Action<float, bool>? Changed;

    public VolumeController(ILog log)
    {
        _log = log;
        _worker = new Thread(Run) { IsBackground = true, Name = "HandGI.Volume" };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
    }

    public float Level => _level;
    public bool Muted => _muted;
    public bool Available => _endpoint is not null;

    public void SetLevel(float level) => _queue.Add(() =>
    {
        var ep = Endpoint();
        if (ep is null) return;
        var ctx = EventContext;
        ep.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref ctx);
        if (_muted && level > 0)
        {
            ep.SetMute(false, ref ctx);
        }
        Refresh(force: true);
    });

    public void ToggleMute() => _queue.Add(() =>
    {
        var ep = Endpoint();
        if (ep is null) return;
        var ctx = EventContext;
        ep.GetMute(out bool muted);
        ep.SetMute(!muted, ref ctx);
        Refresh(force: true);
    });

    private void Run()
    {
        while (!_queue.IsAddingCompleted)
        {
            try
            {
                if (_queue.TryTake(out var action, 300)) action();
                else Refresh(force: false);
            }
            catch (InvalidOperationException)
            {
                break; // completed
            }
            catch (Exception ex)
            {
                _log.Warn($"Volume: {ex.Message}");
                ReleaseEndpoint();
            }
        }
        ReleaseEndpoint();
    }

    private void Refresh(bool force)
    {
        if (!force && DateTime.UtcNow - _lastRefresh < TimeSpan.FromMilliseconds(400)) return;
        _lastRefresh = DateTime.UtcNow;
        var ep = Endpoint();
        if (ep is null) return;
        float level = _level;
        bool muted = _muted;
        if (ep.GetMasterVolumeLevelScalar(out float l) == 0) level = l;
        if (ep.GetMute(out bool m) == 0) muted = m;
        bool changed = MathF.Abs(level - _level) > 0.0005f || muted != _muted;
        _level = level;
        _muted = muted;
        if (changed || force)
        {
            try { Changed?.Invoke(level, muted); } catch { /* listeners must not break audio */ }
        }
    }

    private IAudioEndpointVolume? Endpoint()
    {
        // Re-acquire every few seconds so a new default device (headphones plugged in) is followed.
        if (_endpoint is not null && DateTime.UtcNow - _acquiredAt < TimeSpan.FromSeconds(5)) return _endpoint;
        ReleaseEndpoint();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device) != 0 || device is null) return null;
                var iid = typeof(IAudioEndpointVolume).GUID;
                if (device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out object obj) != 0) return null;
                _endpoint = (IAudioEndpointVolume)obj;
                _acquiredAt = DateTime.UtcNow;
                Marshal.ReleaseComObject(device);
                return _endpoint;
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Volume: no default playback device ({ex.Message}).");
            return null;
        }
    }

    private DateTime _acquiredAt;

    private void ReleaseEndpoint()
    {
        if (_endpoint is null) return;
        try { Marshal.ReleaseComObject(_endpoint); } catch { /* ignore */ }
        _endpoint = null;
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _worker.Join(1000);
    }

    // ---------------------------------------------------------------- Core Audio interop

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
