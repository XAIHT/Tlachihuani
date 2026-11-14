// Tlachihuani — Angela López Mendoza · XAIHT
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Tlachihuani.Core;

namespace Tlachihuani.Windows;

public static class RcLifecycle
{
    public static Task<NativeStatus> ExecuteAsync(string action, CancellationToken token) => Task.Run(() =>
    {
        if (action is not ("start" or "stop" or "restart")) throw new LifecycleException("invalid_request", "Unknown RC lifecycle action.");
        var installation = RcInstallation.Load();
        using var security = new NativeSecurity($"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x100001;;;{installation.OperatorSid})");
        var attributes = security.Attributes;
        using var mutex = CreateMutexEx(ref attributes, $"Global\\XAIHT.Tlachihuani.RC.{installation.InstallationId:N}.Lifecycle", 0, 0x100001);
        if (mutex.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var wait = WaitForSingleObject(mutex, 0);
        if (wait == 258) throw new LifecycleException("busy", "Another Remote-Controller lifecycle operation is in progress.");
        if (wait is not (0 or 0x80)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var scm = ScmBackend.ForRemoteController();
            scm.ValidateRemoteControllerInstallation();
            if (action is "stop" or "restart") Stop(scm, token).GetAwaiter().GetResult();
            if (action is "start" or "restart") new Lifecycle(scm).ExecuteAsync("start", TimeSpan.FromSeconds(30), token).GetAwaiter().GetResult();
            return scm.Query();
        }
        finally { ReleaseMutex(mutex); }
    }, token);

    private static async Task Stop(ScmBackend scm, CancellationToken token)
    {
        var before = scm.Query();
        using var process = before.Pid == 0 ? null : ProcessIdentity.Open(before.Pid);
        if (process is not null && !string.Equals(process.ImagePath, RcInstallation.Image, StringComparison.OrdinalIgnoreCase))
            throw new LifecycleException("identity_changed", "RC process identity differs from the installation.");
        await new Lifecycle(scm).ExecuteAsync("stop", TimeSpan.FromSeconds(30), token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (process is not null && !process.HasExited) await Task.Delay(100, deadline.Token);
        if (scm.Query().State != "stopped") throw new LifecycleException("state_changed", "RC state changed during shutdown.");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeWaitHandle CreateMutexEx(ref SecurityAttributes security, string name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint timeout);
    [DllImport("kernel32.dll")] private static extern bool ReleaseMutex(SafeWaitHandle handle);
}
