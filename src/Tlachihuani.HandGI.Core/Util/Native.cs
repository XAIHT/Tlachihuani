// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using System.Runtime.InteropServices;

namespace HandGI.Core.Util;

/// <summary>
/// Scheduling helpers for the real-time thread: MMCSS registration and opting out of
/// EcoQoS power throttling (on hybrid Intel CPUs a throttled thread can land on an
/// efficiency core and add milliseconds of jitter). Every call is best-effort.
/// </summary>
public static class Native
{
    private const int ProcessPowerThrottling = 4;
    private const int ThreadPowerThrottling = 3;
    private const uint ExecutionSpeed = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristicsW(string taskName, ref uint taskIndex);

    [DllImport("avrt.dll", SetLastError = true)]
    private static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetThreadInformation(IntPtr thread, int infoClass, ref PowerThrottlingState info, int size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    /// <summary>Registers the calling thread with the Multimedia Class Scheduler (e.g. "Games", "Capture").</summary>
    public static IntPtr EnterMmcss(string task)
    {
        try
        {
            uint index = 0;
            return AvSetMmThreadCharacteristicsW(task, ref index);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    public static void LeaveMmcss(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        try { AvRevertMmThreadCharacteristics(handle); } catch { /* best effort */ }
    }

    /// <summary>Never let Windows run this process under EcoQoS.</summary>
    public static bool DisableProcessPowerThrottling()
    {
        try
        {
            var state = new PowerThrottlingState { Version = 1, ControlMask = ExecutionSpeed, StateMask = 0 };
            return SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
        }
        catch
        {
            return false;
        }
    }

    public static bool DisableThreadPowerThrottling()
    {
        try
        {
            var state = new PowerThrottlingState { Version = 1, ControlMask = ExecutionSpeed, StateMask = 0 };
            return SetThreadInformation(GetCurrentThread(), ThreadPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
        }
        catch
        {
            return false;
        }
    }
}
