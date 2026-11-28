// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using System.Diagnostics;

namespace HandGI.Core.Util;

/// <summary>
/// One clock for the whole pipeline: QueryPerformanceCounter expressed in 100-ns ticks,
/// the same domain as MediaFrameReference.SystemRelativeTime, so camera timestamps and
/// processing timestamps can be subtracted directly.
/// </summary>
public static class QpcClock
{
    private static readonly long Frequency = Stopwatch.Frequency;

    public static long Now100ns()
    {
        long t = Stopwatch.GetTimestamp();
        // split to avoid overflow on long uptimes
        return t / Frequency * 10_000_000L + t % Frequency * 10_000_000L / Frequency;
    }

    public static double ToMs(long delta100ns) => delta100ns / 10_000.0;

    public static double MsSince(long start100ns) => ToMs(Now100ns() - start100ns);
}
