// Tlachihuani — Angela López Mendoza · XAIHT
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Tlachihuani.Windows;

namespace Tlachihuani.Tray;

/// <summary>Signals only a verified, dedicated installed-application console.</summary>
internal static class RcConsoleStop
{
    internal static int Run(string edition, string pidText, string createdText)
    {
        var observed = new List<ProcessIdentity>();
        bool attached = false;
        try
        {
            if (!int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0
                || !long.TryParse(createdText, NumberStyles.None, CultureInfo.InvariantCulture, out var created)) return 2;
            using var self = Process.GetCurrentProcess();
            using var user = WindowsIdentity.GetCurrent();
            if (!Environment.UserInteractive || self.SessionId == 0 || user.User is null
                || new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator)) return 2;
            var target = RcApplicationBroker.DiscoverInstalled(edition);
            var primary = ProcessIdentity.Open(pid); observed.Add(primary);
            if (primary.CreationTime != created || !Matches(primary, target, user.User.Value, self.SessionId)) return 2;
            if (primary.HasExited) return 0;
            // Refuse if this helper already inherited a developer/shared console.
            if (GetConsoleProcessList(new uint[1], 1) != 0 || !AttachConsole((uint)pid)) return 3;
            attached = true;
            if (!SetConsoleCtrlHandler(IntPtr.Zero, true)) return 2;
            var members = ConsoleMembers();
            if (!members.Contains((uint)pid) || !members.Contains((uint)self.Id)) return 3;
            foreach (var member in members.Where(p => p != self.Id && p != pid))
            {
                var process = ProcessIdentity.Open(checked((int)member)); observed.Add(process);
                // No shells, unrelated programs or another edition receive Ctrl+C.
                if (!Matches(process, target, user.User.Value, self.SessionId)) return 3;
            }
            if (!members.SequenceEqual(ConsoleMembers())) return 3;
            if (!GenerateConsoleCtrlEvent(0, 0)) return 2;
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromSeconds(35))
            {
                if (observed.All(p => p.HasExited)) return 0;
                Thread.Sleep(100);
            }
            return 4; // Never substitute forced termination for confirmed cleanup.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return 2; }
        finally
        {
            if (attached) FreeConsole();
            foreach (var process in observed) process.Dispose();
        }
    }

    private static uint[] ConsoleMembers()
    {
        var buffer = new uint[128];
        var count = GetConsoleProcessList(buffer, (uint)buffer.Length);
        if (count == 0 || count > buffer.Length) throw new InvalidOperationException("Console membership unavailable.");
        return buffer.Take((int)count).Order().ToArray();
    }

    private static bool Matches(ProcessIdentity process, string target, string sid, int session)
    {
        if (process.HasExited || !Path.GetFullPath(process.ImagePath).Equals(target, StringComparison.OrdinalIgnoreCase)) return false;
        using var instance = Process.GetProcessById(process.Id);
        if (instance.SessionId != session) return false;
        using var handle = OpenProcess(0x1000, false, process.Id);
        if (handle.IsInvalid || !OpenProcessToken(handle, 8, out var access)) return false;
        using (access)
        using (var identity = new WindowsIdentity(access.DangerousGetHandle())) return identity.User?.Value == sid;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetConsoleProcessList(uint[] members, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool ignore);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GenerateConsoleCtrlEvent(uint control, uint group);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
}
