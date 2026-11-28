// Tlachihuani — Angela López Mendoza · XAIHT
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Tlachihuani.Core;

namespace Tlachihuani.Windows;

public static class LocalPipe
{
    // Excludes FILE_CREATE_PIPE_INSTANCE (0x4) from client rights. Remote clients are refused by the kernel.
    private const uint ClientRights = 0x12019b;
    public static NamedPipeServerStream CreateServer(string name, string operatorSid, bool first)
    {
        var owner = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("No Windows identity.");
        _ = new SecurityIdentifier(operatorSid);
        using var security = new NativeSecurity($"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{owner})(A;;0x{ClientRights:x};;;{operatorSid})");
        var attrs = security.Attributes;
        var handle = CreateNamedPipe($@"\\.\pipe\{name}", 3 | 0x40000000u | (first ? 0x80000u : 0), 8, 16, 65540, 65540, 0, ref attrs);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
        try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle); }
        catch { handle.Dispose(); throw; }
    }

    public static async Task<NamedPipeClientStream> ConnectAsync(string name, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            // Identification-only SQOS: even an impersonating server cannot use the caller's credentials.
            var handle = CreateFile($@"\\.\pipe\{name}", ClientRights, 0, IntPtr.Zero, 3, 0x40000000u | 0x100000u | 0x10000u, IntPtr.Zero);
            if (!handle.IsInvalid)
            {
                try { return new NamedPipeClientStream(PipeDirection.InOut, true, true, handle); }
                catch { handle.Dispose(); throw; }
            }
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (error is not (2 or 231)) throw new Win32Exception(error);
            await Task.Delay(40, token);
        }
    }

    public static ProcessIdentity VerifyServer(NamedPipeClientStream pipe, int expectedPid, string expectedPath)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (pid != expectedPid || pid == 0) throw new LifecycleException("identity_changed", "The pipe server is not the expected service process.");
        var identity = ProcessIdentity.Open(checked((int)pid));
        if (!string.Equals(Path.GetFullPath(identity.ImagePath), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase))
        {
            identity.Dispose();
            throw new LifecycleException("identity_changed", "The pipe server executable does not match the installation.");
        }
        return identity;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafePipeHandle CreateNamedPipe(string name, uint mode, uint pipeMode, uint maxInstances, uint outputSize, uint inputSize, uint timeout, ref SecurityAttributes attributes);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafePipeHandle CreateFile(string name, uint access, uint sharing, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
}

[StructLayout(LayoutKind.Sequential)] internal struct SecurityAttributes
{
    public int Length;
    public IntPtr Descriptor;
    public int Inherit;
}

internal sealed class NativeSecurity : IDisposable
{
    private IntPtr descriptor;
    public SecurityAttributes Attributes => new() { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
    public NativeSecurity(string sddl)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out descriptor, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Dispose() { if (descriptor != IntPtr.Zero) { LocalFree(descriptor); descriptor = IntPtr.Zero; } }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
}

public sealed class ProcessIdentity : IEndableProcess
{
    private readonly SafeProcessHandle handle;
    public int Id { get; }
    public string ImagePath { get; }
    public long CreationTime { get; }
    public bool CanTerminate { get; }
    public bool HasExited
    {
        get
        {
            if (!GetExitCodeProcess(handle, out var exitCode)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return exitCode != 259;
        }
    }
    private ProcessIdentity(SafeProcessHandle handle, int id, string path, long creation, bool terminate)
    {
        this.handle = handle; Id = id; ImagePath = path; CreationTime = creation; CanTerminate = terminate;
    }
    public static ProcessIdentity Open(int pid) => Open(pid, terminate: false);

    // terminate also asks for PROCESS_TERMINATE. Windows grants it for the caller's own processes
    // and for a service that gave its operator that right (OperatorProcessAccess.AllowOperatorStop).
    public static ProcessIdentity Open(int pid, bool terminate)
    {
        var handle = OpenProcess(terminate ? 0x1001u : 0x1000u, false, pid);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
        try
        {
            var path = new StringBuilder(32768); var length = path.Capacity;
            if (!QueryFullProcessImageName(handle, 0, path, ref length) || !GetProcessTimes(handle, out var created, out _, out _, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return new(handle, pid, path.ToString(), created, terminate);
        }
        catch { handle.Dispose(); throw; }
    }

    // Ends this exact process (the handle was opened for it, so a reused PID cannot be hit).
    // Requires an identity opened with terminate: true.
    public void Terminate(uint exitCode)
    {
        if (TerminateProcess(handle, exitCode)) return;
        int error = Marshal.GetLastWin32Error();
        if (!HasExited) throw new Win32Exception(error);
    }

    // Polls the exit code, so it works with the limited query right alone.
    public bool WaitForExit(TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (!HasExited)
        {
            if (clock.Elapsed >= timeout) return false;
            Thread.Sleep(25);
        }
        return true;
    }

    public void Dispose() => handle.Dispose();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(SafeProcessHandle handle, out uint code);
}
