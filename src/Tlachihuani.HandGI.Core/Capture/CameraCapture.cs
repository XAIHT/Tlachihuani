// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using System.Runtime.InteropServices;
using HandGI.Core.Util;
using HandGI.Core.Vision;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using WinRT;

namespace HandGI.Core.Capture;

public enum CameraKind { Color, Infrared }

/// <summary>A camera stream the user can pick (a colour or infrared source of a camera).</summary>
public sealed record CameraInfo(string GroupId, string GroupName, string SourceId, CameraKind Kind)
{
    public string DisplayName => Kind == CameraKind.Infrared ? $"{GroupName} (infrared)" : GroupName;
    public override string ToString() => DisplayName;
}

/// <summary>One capture mode of a camera.</summary>
public sealed record CameraFormat(string Subtype, uint Width, uint Height, uint FpsNumerator, uint FpsDenominator)
{
    private static readonly string[] Raw = { "NV12", "YUY2", "RGB32", "ARGB32", "L8", "BGRA8" };

    public double Fps => FpsDenominator == 0 ? 0 : (double)FpsNumerator / FpsDenominator;

    /// <summary>Uncompressed formats reach the GPU untouched; MJPEG needs a CPU decode first.</summary>
    public bool Uncompressed => Raw.Contains(Subtype);

    public string Key => $"{Subtype}:{Width}x{Height}@{FpsNumerator}/{FpsDenominator}";

    public override string ToString() =>
        $"{Width}×{Height} · {Fps:0.#} fps · {Subtype}" + (Uncompressed ? "" : "  (needs CPU decode)");

    /// <summary>Lowest latency first: highest frame rate among raw formats, then the size closest to 1280×720.</summary>
    public static CameraFormat? Recommend(IEnumerable<CameraFormat> formats) =>
        formats.Where(f => f.Uncompressed && f.Width <= 1920)
            .OrderByDescending(f => Math.Round(f.Fps))
            .ThenBy(f => Math.Abs((long)f.Width * f.Height - 1280L * 720L))
            .FirstOrDefault()
        ?? formats.OrderByDescending(f => Math.Round(f.Fps)).FirstOrDefault();
}

public static class CameraCatalog
{
    /// <summary>Every colour and infrared camera stream Windows exposes.</summary>
    public static async Task<IReadOnlyList<CameraInfo>> ListAsync()
    {
        var result = new List<CameraInfo>();
        var groups = await MediaFrameSourceGroup.FindAllAsync();
        foreach (var group in groups)
        {
            foreach (var kind in new[] { MediaFrameSourceKind.Color, MediaFrameSourceKind.Infrared })
            {
                var infos = group.SourceInfos
                    .Where(i => i.SourceKind == kind &&
                                (i.MediaStreamType == MediaStreamType.VideoRecord || i.MediaStreamType == MediaStreamType.VideoPreview))
                    .ToList();
                if (infos.Count == 0) continue;
                var info = infos.FirstOrDefault(i => i.MediaStreamType == MediaStreamType.VideoRecord) ?? infos[0];
                result.Add(new CameraInfo(group.Id, group.DisplayName, info.Id,
                    kind == MediaFrameSourceKind.Color ? CameraKind.Color : CameraKind.Infrared));
            }
        }
        return result;
    }

    /// <summary>The capture modes of one camera stream (opened read-only, so it works even if another app uses the camera).</summary>
    public static async Task<IReadOnlyList<CameraFormat>> ListFormatsAsync(CameraInfo camera)
    {
        var group = await MediaFrameSourceGroup.FromIdAsync(camera.GroupId);
        if (group is null) return Array.Empty<CameraFormat>();
        using var capture = new MediaCapture();
        await capture.InitializeAsync(new MediaCaptureInitializationSettings
        {
            SourceGroup = group,
            SharingMode = MediaCaptureSharingMode.SharedReadOnly,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            StreamingCaptureMode = StreamingCaptureMode.Video,
        });
        if (!capture.FrameSources.TryGetValue(camera.SourceId, out var source)) return Array.Empty<CameraFormat>();
        return source.SupportedFormats
            .Where(f => f.VideoFormat is not null)
            .Select(f => new CameraFormat(
                Normalize(f.Subtype),
                f.VideoFormat.Width,
                f.VideoFormat.Height,
                f.FrameRate?.Numerator ?? 0,
                f.FrameRate?.Denominator ?? 1))
            .Distinct()
            .OrderByDescending(f => f.Uncompressed)
            .ThenByDescending(f => f.Width * f.Height)
            .ThenByDescending(f => f.Fps)
            .ToList();
    }

    internal static string Normalize(string subtype) => (subtype ?? "").Trim().ToUpperInvariant();
}

/// <summary>Raw camera bytes, their layout and the capture timestamp (QPC, 100-ns units).</summary>
public delegate void RawFrameHandler(ReadOnlySpan<byte> data, FrameLayout layout, long timestamp100ns);

/// <summary>
/// Real-time camera capture through Windows.Media.Capture: <c>MediaFrameReaderAcquisitionMode.Realtime</c>
/// drops frames the consumer has not taken (no queue, no added latency), frames stay in their native
/// NV12 / YUY2 / BGRA / Gray layout and are handed over without any CPU conversion.
/// </summary>
public sealed class CameraCapture : IAsyncDisposable
{
    private readonly ILog _log;
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private int _busy;
    private long _arrived;
    private long _withoutBitmap;
    private long _skippedBusy;
    private long _lastErrorLog;
    private Windows.Media.Devices.ExposurePriorityVideoControl? _restoreExposurePriority;

    public event RawFrameHandler? FrameArrived;
    public event Action<string>? Failed;

    private CameraCapture(ILog log) => _log = log;

    public string Description { get; private set; } = "";
    public bool CpuDecode { get; private set; }
    public bool Shared { get; private set; }
    public CameraFormat? ActiveFormat { get; private set; }
    public long FramesArrived => Interlocked.Read(ref _arrived);
    public long FramesWithoutBitmap => Interlocked.Read(ref _withoutBitmap);
    /// <summary>Frames Windows announced while the previous one was still being copied (dropped, newest wins).</summary>
    public long FramesSkippedBusy => Interlocked.Read(ref _skippedBusy);

    public static async Task<CameraCapture> OpenAsync(CameraInfo camera, CameraFormat? format, bool shared, ILog log)
    {
        var group = await MediaFrameSourceGroup.FromIdAsync(camera.GroupId)
            ?? throw new InvalidOperationException("The selected camera is no longer connected.");

        var result = new CameraCapture(log);
        var capture = new MediaCapture();
        try
        {
            await capture.InitializeAsync(Settings(group, shared));
        }
        catch (UnauthorizedAccessException)
        {
            capture.Dispose();
            throw new InvalidOperationException(
                "Windows blocked camera access. Open Settings → Privacy & security → Camera and allow camera access for desktop apps.");
        }
        catch (Exception ex) when (!shared)
        {
            log.Warn($"Exclusive camera access failed ({ex.Message}); retrying in shared read-only mode.");
            capture.Dispose();
            capture = new MediaCapture();
            await capture.InitializeAsync(Settings(group, shared: true));
            shared = true;
        }

        if (!capture.FrameSources.TryGetValue(camera.SourceId, out var source))
        {
            capture.Dispose();
            throw new InvalidOperationException("The selected camera stream is not available.");
        }

        if (!shared && format is not null)
        {
            var match = source.SupportedFormats.FirstOrDefault(f =>
                f.VideoFormat is not null &&
                CameraCatalog.Normalize(f.Subtype) == format.Subtype &&
                f.VideoFormat.Width == format.Width &&
                f.VideoFormat.Height == format.Height &&
                (f.FrameRate?.Numerator ?? 0) == format.FpsNumerator &&
                (f.FrameRate?.Denominator ?? 1) == format.FpsDenominator);
            if (match is not null) await source.SetFormatAsync(match);
            else log.Warn($"Format {format} not offered any more; using the camera's current format.");
        }
        if (!shared) result.KeepFullFrameRate(source);

        var current = source.CurrentFormat;
        var active = new CameraFormat(
            CameraCatalog.Normalize(current.Subtype),
            current.VideoFormat?.Width ?? 0,
            current.VideoFormat?.Height ?? 0,
            current.FrameRate?.Numerator ?? 0,
            current.FrameRate?.Denominator ?? 1);
        bool decode = !active.Uncompressed;

        var reader = decode
            ? await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Nv12)
            : await capture.CreateFrameReaderAsync(source);
        reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
        reader.FrameArrived += result.OnFrameArrived;
        capture.Failed += (_, e) => result.Failed?.Invoke(e.Message);

        var status = await reader.StartAsync();
        if (status != MediaFrameReaderStartStatus.Success)
        {
            reader.Dispose();
            result.RestoreExposurePriority();
            capture.Dispose();
            throw new InvalidOperationException($"The camera did not start ({status}).");
        }

        result._capture = capture;
        result._reader = reader;
        result.CpuDecode = decode;
        result.Shared = shared;
        result.ActiveFormat = active;
        result.Description = $"{camera.DisplayName} · {active.Width}×{active.Height} @ {active.Fps:0.#} fps · {active.Subtype}"
                             + (decode ? " → NV12 (CPU decode)" : "")
                             + (shared ? " · shared" : "");
        log.Info($"Camera started: {result.Description}");
        return result;
    }

    private static MediaCaptureInitializationSettings Settings(MediaFrameSourceGroup group, bool shared) => new()
    {
        SourceGroup = group,
        SharingMode = shared ? MediaCaptureSharingMode.SharedReadOnly : MediaCaptureSharingMode.ExclusiveControl,
        MemoryPreference = MediaCaptureMemoryPreference.Cpu,
        StreamingCaptureMode = StreamingCaptureMode.Video,
    };

    /// <summary>
    /// In dim light most webcams lengthen the exposure and lower their frame rate ("exposure priority"):
    /// 30 fps becomes 20 fps or less, and every frame waits longer inside the camera. HandGI asks the camera
    /// to keep its full frame rate instead; it raises the gain (a slightly noisier picture, which the hand
    /// models tolerate well). The camera's own setting is restored when HandGI closes it.
    /// </summary>
    private void KeepFullFrameRate(MediaFrameSource source)
    {
        try
        {
            var control = source.Controller.VideoDeviceController.ExposurePriorityVideoControl;
            if (!control.Supported)
            {
                _log.Info("Exposure priority: this camera does not offer the setting, so in dim light it may still lower its frame rate (more light helps).");
                return;
            }
            if (!control.Enabled)
            {
                _log.Info("Exposure priority is already off: the camera keeps its full frame rate in dim light.");
                return;
            }
            control.Enabled = false;
            _restoreExposurePriority = control;
            _log.Info("Exposure priority turned off: the camera keeps its full frame rate in dim light instead of exposing longer (restored when HandGI closes the camera).");
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not change the camera's exposure priority ({ex.Message}); in dim light it may lower its frame rate.");
        }
    }

    private unsafe void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            Interlocked.Increment(ref _skippedBusy);
            return;
        }
        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            if (frame is null) return;
            var bitmap = frame.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap is null)
            {
                Interlocked.Increment(ref _withoutBitmap);
                return;
            }

            using (bitmap)
            {
                PixelLayoutKind? kind = bitmap.BitmapPixelFormat switch
                {
                    BitmapPixelFormat.Nv12 => PixelLayoutKind.Nv12,
                    BitmapPixelFormat.Yuy2 => PixelLayoutKind.Yuy2,
                    BitmapPixelFormat.Bgra8 => PixelLayoutKind.Bgra8,
                    BitmapPixelFormat.Rgba8 => PixelLayoutKind.Rgba8,
                    BitmapPixelFormat.Gray8 => PixelLayoutKind.Gray8,
                    _ => null,
                };
                if (kind is null)
                {
                    LogRateLimited($"Unsupported camera pixel format {bitmap.BitmapPixelFormat}.");
                    return;
                }

                using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
                int planes = buffer.GetPlaneCount();
                var p0 = buffer.GetPlaneDescription(0);
                var p1 = planes > 1 ? buffer.GetPlaneDescription(1) : default;
                using var reference = buffer.CreateReference();
                var access = reference.As<IMemoryBufferByteAccess>();
                access.GetBuffer(out byte* data, out uint capacity);

                var layout = new FrameLayout(kind.Value, bitmap.PixelWidth, bitmap.PixelHeight, (int)capacity,
                    p0.StartIndex, p0.Stride, p1.StartIndex, p1.Stride);
                long timestamp = frame.SystemRelativeTime?.Ticks ?? QpcClock.Now100ns();
                Interlocked.Increment(ref _arrived);
                FrameArrived?.Invoke(new ReadOnlySpan<byte>(data, (int)capacity), layout, timestamp);
            }
        }
        catch (Exception ex)
        {
            LogRateLimited($"Camera frame error: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private void LogRateLimited(string message)
    {
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastErrorLog) < 5000) return;
        Interlocked.Exchange(ref _lastErrorLog, now);
        _log.Warn(message);
    }

    public async ValueTask DisposeAsync()
    {
        var reader = _reader;
        var capture = _capture;
        _reader = null;
        _capture = null;
        if (reader is not null)
        {
            try
            {
                reader.FrameArrived -= OnFrameArrived;
                await reader.StopAsync();
            }
            catch
            {
                // camera may already be gone
            }
            reader.Dispose();
        }
        RestoreExposurePriority();
        capture?.Dispose();
    }

    /// <summary>Gives the camera back the exposure-priority setting it had before HandGI opened it.</summary>
    private void RestoreExposurePriority()
    {
        var restore = _restoreExposurePriority;
        _restoreExposurePriority = null;
        if (restore is null) return;
        try
        {
            restore.Enabled = true;
        }
        catch
        {
            // camera may already be gone
        }
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}
