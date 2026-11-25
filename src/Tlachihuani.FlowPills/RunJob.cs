// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (native process control, polled twice a second for every run)
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Tlachihuani.FlowPills;

// One run's processes: an unnamed, kill-on-close Job Object. Every agent the run starts is
// created suspended, placed in the job and only then resumed, so not one instruction of it runs
// outside; whatever an agent starts stays in the job too. Stopping a run ends exactly its own
// process tree, verified by job membership at that moment, never by image name or a stale PID.
internal sealed class RunJob : IDisposable
{
    private const int KillOnJobClose = 0x2000;
    private const int BasicProcessIdList = 3, ExtendedLimitInformation = 9;
    private const uint CreateSuspended = 0x4, DetachedProcess = 0x8, CreateNewProcessGroup = 0x200,
        CreateUnicodeEnvironment = 0x400, ExtendedStartupInfoPresent = 0x80000;
    private const int UseStdHandles = 0x100;
    private const uint ProcessTerminate = 0x1, ProcessQueryLimitedInformation = 0x1000;
    private static readonly IntPtr HandleListAttribute = 0x20002;
    private static readonly char[] NeedsQuotes = [' ', '\t', '\n', '\v', '"'];

    private readonly SafeFileHandle job;
    private volatile bool disposed;

    internal RunJob()
    {
        job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = KillOnJobClose } };
        if (!SetInformationJobObject(job, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        {
            int error = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new Win32Exception(error);
        }
    }

    // Starts one process in the job with NUL as its standard handles and no console
    // (DETACHED_PROCESS, the way Tlamatini starts its agents) and returns its process id.
    internal int Launch(string executable, IReadOnlyList<string> arguments, string directory, IReadOnlyDictionary<string, string> environment)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var inherit = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), InheritHandle = 1 };
        using var nul = CreateFileW("NUL", 0xC0000000, 3, ref inherit, 3, 0, IntPtr.Zero);
        if (nul.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        IntPtr attributes = Marshal.AllocHGlobal(size);
        IntPtr handles = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr block = Marshal.StringToHGlobalUni(EnvironmentBlock(environment));
        bool initialized = false;
        try
        {
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            initialized = true;
            // Only the NUL handle is inherited: nothing else of this process leaks into an agent.
            Marshal.WriteIntPtr(handles, nul.DangerousGetHandle());
            if (!UpdateProcThreadAttribute(attributes, 0, HandleListAttribute, handles, IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var startup = new StartupInfoEx();
            startup.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
            startup.StartupInfo.dwFlags = UseStdHandles;
            startup.StartupInfo.hStdInput = nul.DangerousGetHandle();
            startup.StartupInfo.hStdOutput = nul.DangerousGetHandle();
            startup.StartupInfo.hStdError = nul.DangerousGetHandle();
            startup.lpAttributeList = attributes;
            var command = new StringBuilder(CommandLine(executable, arguments));
            if (!CreateProcessW(executable, command, IntPtr.Zero, IntPtr.Zero, true,
                    CreateSuspended | DetachedProcess | CreateNewProcessGroup | CreateUnicodeEnvironment | ExtendedStartupInfoPresent,
                    block, directory, ref startup, out var process))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!AssignProcessToJobObject(job, process.hProcess) || ResumeThread(process.hThread) == uint.MaxValue)
                {
                    int error = Marshal.GetLastWin32Error();
                    TerminateProcess(process.hProcess, 1);
                    throw new Win32Exception(error);
                }
                return process.dwProcessId;
            }
            finally
            {
                CloseHandle(process.hThread);
                CloseHandle(process.hProcess);
            }
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(block);
            Marshal.FreeHGlobal(handles);
            Marshal.FreeHGlobal(attributes);
        }
    }

    // The processes now in the job, or null when Windows could not say; null never means "none".
    internal int[]? ProcessIds()
    {
        if (disposed) return null;
        const int capacity = 8192;
        int size = 8 + IntPtr.Size * capacity;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryInformationJobObject(job, BasicProcessIdList, buffer, (uint)size, out _)) return null;
            int count = Math.Min(Marshal.ReadInt32(buffer, 4), capacity);
            var ids = new int[count];
            for (int i = 0; i < count; i++) ids[i] = (int)Marshal.ReadIntPtr(buffer, 8 + i * IntPtr.Size);
            return ids;
        }
        catch (ObjectDisposedException) { return null; }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    // Ends one process, and only if it is a member of this job at this moment.
    internal bool Terminate(int processId)
    {
        if (disposed || processId <= 0) return false;
        try
        {
            using var process = OpenProcess(ProcessTerminate | ProcessQueryLimitedInformation, false, processId);
            return !process.IsInvalid && IsProcessInJob(process, job, out bool member) && member && TerminateOwned(process, 1);
        }
        catch (ObjectDisposedException) { return false; }
    }

    internal void TerminateAll()
    {
        if (disposed) return;
        try { TerminateJobObject(job, 1); }
        catch (ObjectDisposedException) { }
    }

    // After a run completed: what the flow started on purpose (an application, a server) keeps
    // running when the job's handle closes, as it would have under Tlamatini.
    internal void Release()
    {
        if (disposed) return;
        var limits = new ExtendedLimits();
        try { SetInformationJobObject(job, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()); }
        catch (ObjectDisposedException) { }
    }

    // The most memory the run's processes have used together.
    internal ulong PeakMemory()
    {
        if (disposed) return 0;
        try { return QueryLimits(job, ExtendedLimitInformation, out var limits, (uint)Marshal.SizeOf<ExtendedLimits>(), out _) ? limits.PeakJobMemoryUsed : 0; }
        catch (ObjectDisposedException) { return 0; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        job.Dispose();   // kill-on-close: whatever is still running ends with the job
    }

    // Windows command-line quoting (the rules CommandLineToArgvW and the C runtime read back).
    internal static string CommandLine(string executable, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        Quote(builder, executable);
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            Quote(builder, argument);
        }
        return builder.ToString();
    }

    private static void Quote(StringBuilder builder, string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny(NeedsQuotes) < 0)
        {
            builder.Append(argument);
            return;
        }
        builder.Append('"');
        for (int i = 0; ; i++)
        {
            int backslashes = 0;
            while (i < argument.Length && argument[i] == '\\') { i++; backslashes++; }
            if (i == argument.Length)
            {
                builder.Append('\\', backslashes * 2);
                break;
            }
            if (argument[i] == '"') builder.Append('\\', backslashes * 2 + 1).Append('"');
            else builder.Append('\\', backslashes).Append(argument[i]);
        }
        builder.Append('"');
    }

    // This process's environment with the run's values on top, sorted as Windows expects.
    internal static string EnvironmentBlock(IReadOnlyDictionary<string, string> overrides)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key is string key && key.Length > 0 && entry.Value is string value) merged[key] = value;
        foreach (var (key, value) in overrides) merged[key] = value;
        var builder = new StringBuilder();
        foreach (var (key, value) in merged) builder.Append(key).Append('=').Append(value).Append('\0');
        return builder.Append('\0').ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTime, PerJobUserTime; public uint Flags; public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit; public nuint Affinity; public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong Reads, Writes, Others, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int InheritHandle; }
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int cb; public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr lpAttributeList; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimits information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int informationClass, IntPtr information, uint length, out uint returned);
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "QueryInformationJobObject")]
    private static extern bool QueryLimits(SafeFileHandle job, int informationClass, out ExtendedLimits information, uint length, out uint returned);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessInJob(SafeProcessHandle process, SafeFileHandle job, out bool result);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "TerminateProcess")]
    private static extern bool TerminateOwned(SafeProcessHandle process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, ref SecurityAttributes security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool CreateProcessW(string? application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint flags, IntPtr environment, string? directory, ref StartupInfoEx startup, out ProcessInformation information);
}
