// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (asked every second while flows wait; the window shows what was decided and why)
using System.Globalization;
using System.Runtime.InteropServices;

namespace Tlachihuani.FlowPills;

// How many flows may run at once on THIS machine: up to 256 by default, or exactly what
// settings.json says, and never so many agent processes that Windows and the desktop run out
// of memory. Another agentic flow starts only when the memory free right now covers a reserve
// plus what its agents need; one that does not fit waits, and the window says why, with the
// measurement. A flow pill is a thread of this process whose model runs in Ollama, so memory
// never holds a pill back: only the count limit does.
public sealed class FlowCapacity
{
    public const int DefaultMaxRuns = 256;
    public const long AgentFlowBytes = 192L * 1024 * 1024;

    public FlowCapacity(FlowPillsSettings settings)
    {
        MaxRuns = settings.MaxRuns ?? DefaultMaxRuns;
        MaxRunsSource = settings.MaxRuns is null ? "default" : "settings.json";
        ReserveBytes = settings.ReserveBytes ?? AutomaticReserve(Measure().Total);
        ReserveSource = settings.ReserveBytes is null ? "decided from this machine" : "settings.json";
    }

    public int MaxRuns { get; }
    public string MaxRunsSource { get; }
    public long ReserveBytes { get; }
    public string ReserveSource { get; }

    // Windows and the desktop keep 5% of the memory: at least 512 MB, at most 2 GB.
    public static long AutomaticReserve(ulong total)
        => total == 0 ? 512L << 20 : Math.Clamp((long)(total * 0.05), 512L << 20, 2L << 30);

    // Null when one more flow may start now; otherwise the reason it waits.
    public string? WhyNot(int active, FlowKind kind)
    {
        if (active >= MaxRuns)
            return $"{active} flows are running and the limit is {MaxRuns} ({MaxRunsSource}); it starts when one ends.";
        // Memory decides how many flows run together, never whether one can run at all: on a
        // busy machine the first flow still starts, and the next ones wait for room.
        if (active == 0 || kind == FlowKind.Prompts) return null;
        var memory = Measure();
        if (memory.Total == 0) return null;   // unmeasured hardware is not treated as the worst case
        long needed = ReserveBytes + AgentFlowBytes;
        if ((long)Math.Min(memory.Available, (ulong)long.MaxValue) < needed)
            return $"Waiting for memory: {Size(memory.Available)} is free and one more flow needs {Size(needed)} "
                + $"({Size(ReserveBytes)} stays free for Windows, {ReserveSource}).";
        return null;
    }

    public string Describe(int active, int waiting)
    {
        var memory = Measure();
        string room = memory.Total == 0 ? "memory not measured" : $"{Size(memory.Available)} free of {Size(memory.Total)}";
        return $"{active} running · {waiting} waiting · limit {MaxRuns} ({MaxRunsSource}) · {room} · keeps {Size(ReserveBytes)} free";
    }

    public static string Size(ulong bytes) => Size((long)Math.Min(bytes, (ulong)long.MaxValue));

    public static string Size(long bytes) => bytes >= 1L << 30
        ? (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
        : (bytes / (double)(1L << 20)).ToString("0", CultureInfo.InvariantCulture) + " MB";

    public readonly record struct Memory(ulong Total, ulong Available);

    public static Memory Measure()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? new Memory(status.TotalPhysical, status.AvailablePhysical) : new Memory(0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
