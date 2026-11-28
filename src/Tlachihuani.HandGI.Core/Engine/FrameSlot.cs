// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using HandGI.Core.Gpu;
using HandGI.Core.Util;
using HandGI.Core.Vision;
using Microsoft.ML.OnnxRuntime;

namespace HandGI.Core.Engine;

/// <summary>One pinned copy of a raw camera frame plus the ONNX tensor that views it (no per-frame allocation).</summary>
internal sealed class FrameBuffer : IDisposable
{
    public FrameBuffer(FrameLayout layout)
    {
        Layout = layout;
        Data = GC.AllocateArray<byte>(layout.ByteLength, pinned: true);
        Value = OrtFactory.BytesView(Data, layout.ByteLength);
    }

    public FrameLayout Layout { get; }
    public byte[] Data { get; }
    public OrtValue Value { get; }
    public long CameraTime100ns;
    public long ArrivedTime100ns;
    public long Sequence;

    public void Dispose() => Value.Dispose();
}

/// <summary>
/// Latest-frame-wins hand-over between the camera thread and the processing thread.
/// The camera never waits and nothing queues: if the engine is still busy, the older unprocessed
/// frame is recycled and only the newest survives, so latency can never accumulate.
/// The single CPU step is one memcpy out of the driver's buffer, which is only valid during the callback.
/// </summary>
internal sealed class LatestFrameSlot : IDisposable
{
    private const int MaxFree = 3;
    private readonly object _gate = new();
    private readonly Stack<FrameBuffer> _free = new();
    private readonly AutoResetEvent _signal = new(false);
    private FrameBuffer? _latest;
    private FrameLayout? _layout;
    private long _sequence;
    private long _replaced;
    private bool _disposed;

    public long Published => Interlocked.Read(ref _sequence);
    public long Replaced => Interlocked.Read(ref _replaced);

    public void Publish(ReadOnlySpan<byte> data, FrameLayout layout, long cameraTime100ns)
    {
        FrameBuffer? buffer = null;
        lock (_gate)
        {
            if (_disposed) return;
            if (_layout != layout)
            {
                while (_free.Count > 0) _free.Pop().Dispose();
                _layout = layout;
            }
            if (_free.Count > 0) buffer = _free.Pop();
        }

        buffer ??= new FrameBuffer(layout);
        int n = Math.Min(data.Length, buffer.Data.Length);
        data[..n].CopyTo(buffer.Data);
        buffer.CameraTime100ns = cameraTime100ns;
        buffer.ArrivedTime100ns = QpcClock.Now100ns();

        lock (_gate)
        {
            if (_disposed)
            {
                buffer.Dispose();
                return;
            }
            buffer.Sequence = Interlocked.Increment(ref _sequence);
            var stale = _latest;
            _latest = buffer;
            if (stale is not null)
            {
                Interlocked.Increment(ref _replaced);
                RecycleLocked(stale);
            }
        }
        try
        {
            _signal.Set();
        }
        catch (ObjectDisposedException)
        {
            // stopping: a late camera callback raced the shutdown
        }
    }

    /// <summary>Takes the newest frame, waiting up to <paramref name="timeoutMs"/> for one.</summary>
    public FrameBuffer? Take(int timeoutMs)
    {
        lock (_gate)
        {
            if (_latest is not null)
            {
                var ready = _latest;
                _latest = null;
                return ready;
            }
        }
        _signal.WaitOne(timeoutMs);
        lock (_gate)
        {
            var ready = _latest;
            _latest = null;
            return ready;
        }
    }

    public void Return(FrameBuffer buffer)
    {
        lock (_gate) RecycleLocked(buffer);
    }

    public void Wake() => _signal.Set();

    private void RecycleLocked(FrameBuffer buffer)
    {
        if (!_disposed && buffer.Layout == _layout && _free.Count < MaxFree) _free.Push(buffer);
        else buffer.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _latest?.Dispose();
            _latest = null;
            while (_free.Count > 0) _free.Pop().Dispose();
        }
        _signal.Dispose();
    }
}
