// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using Microsoft.ML.OnnxRuntime;

namespace HandGI.Core.Gpu;

/// <summary>
/// Creates ONNX Runtime sessions that run on the chosen GPU through DirectML.
/// With <c>session.disable_cpu_ep_fallback = 1</c> ONNX Runtime refuses to create the session if even
/// one node would run on the CPU: "exclusively GPU" is enforced by the runtime, not just promised.
/// DirectML runs on every DirectX 12 NVIDIA GPU (GTX 10 series and newer) with no CUDA install.
/// A negative adapter index is the explicit, opt-in CPU compatibility mode for machines without one.
/// </summary>
public static class OrtFactory
{
    /// <summary>Adapter index meaning "CPU compatibility mode" (no GPU at all; chosen by the user).</summary>
    public const int CpuAdapter = -1;

    /// <summary>CPU threads used in compatibility mode: one core is always left for the window.</summary>
    public static int CpuThreads => Math.Clamp(Environment.ProcessorCount - 1, 1, 4);

    public static InferenceSession CreateSession(byte[] model, int dxgiIndex, bool allowCpuFallback, string label)
    {
        var options = new SessionOptions
        {
            // DirectML requirements: no memory pattern, sequential execution.
            EnableMemoryPattern = false,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
            LogId = "HandGI." + label,
        };
        try
        {
            // The CPU thread pool must not spin: on the GPU path it only feeds the DirectML queue,
            // and in compatibility mode spinning would steal the core the window needs.
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
            options.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
            if (dxgiIndex >= 0)
            {
                options.IntraOpNumThreads = 1;
                options.AppendExecutionProvider_DML(dxgiIndex);
                if (!allowCpuFallback) options.AddSessionConfigEntry("session.disable_cpu_ep_fallback", "1");
            }
            else
            {
                options.IntraOpNumThreads = CpuThreads;
            }
            return new InferenceSession(model, options);
        }
        catch (OnnxRuntimeException ex)
        {
            string where = dxgiIndex >= 0 ? $"on DXGI adapter {dxgiIndex}" : "on the CPU";
            throw new InvalidOperationException($"Could not create the '{label}' session {where}: {ex.Message}", ex);
        }
        finally
        {
            options.Dispose();
        }
    }

    /// <summary>A pinned float buffer and an ORT tensor that views it (zero copies, zero allocations per frame).</summary>
    public static (float[] Buffer, OrtValue Value) PinnedFloat(params long[] shape)
    {
        long n = 1;
        foreach (long d in shape) n *= d;
        var buffer = GC.AllocateArray<float>((int)n, pinned: true);
        return (buffer, OrtValue.CreateTensorValueFromMemory(buffer, shape));
    }

    public static (byte[] Buffer, OrtValue Value) PinnedBytes(params long[] shape)
    {
        long n = 1;
        foreach (long d in shape) n *= d;
        var buffer = GC.AllocateArray<byte>((int)n, pinned: true);
        return (buffer, OrtValue.CreateTensorValueFromMemory(buffer, shape));
    }

    /// <summary>An ORT tensor over the first <paramref name="length"/> bytes of a pinned buffer.</summary>
    public static OrtValue BytesView(byte[] pinnedBuffer, int length) =>
        OrtValue.CreateTensorValueFromMemory(OrtMemoryInfo.DefaultInstance, new Memory<byte>(pinnedBuffer, 0, length), new long[] { length });
}
