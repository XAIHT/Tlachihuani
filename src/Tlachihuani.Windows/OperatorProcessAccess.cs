using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Tlachihuani.Windows;

public static class OperatorProcessAccess
{
    private const int QueryLimitedInformation = 0x1000;
    private const int Terminate = 0x0001;
    private const int Synchronize = 0x00100000;

    // The native client holds a real process handle and verifies the executable before
    // sending a request. A virtual account's default process ACL denies that query.
    public static void AllowIdentityCheck(string operatorSid) => Allow(operatorSid, QueryLimitedInformation);

    // Also lets the operator end this process. Stop Services in XT uses it when a stop
    // request is not answered in time. The operator can already stop the service through
    // the Service Control Manager; this adds no right to read or change the process.
    public static void AllowOperatorStop(string operatorSid) => Allow(operatorSid, QueryLimitedInformation | Terminate | Synchronize);

    private static void Allow(string operatorSid, int rights)
    {
        const int daclInformation = 4;
        const int insufficientBuffer = 122;
        var self = GetCurrentProcess();
        GetKernelObjectSecurity(self, daclInformation, null, 0, out int length);
        if (Marshal.GetLastWin32Error() != insufficientBuffer || length <= 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        byte[] bytes = new byte[length];
        if (!GetKernelObjectSecurity(self, daclInformation, bytes, bytes.Length, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var descriptor = new CommonSecurityDescriptor(false, false, bytes, 0);
        descriptor.DiscretionaryAcl!.AddAccess(AccessControlType.Allow, new SecurityIdentifier(operatorSid),
            rights, InheritanceFlags.None, PropagationFlags.None);
        bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        if (!SetKernelObjectSecurity(self, daclInformation, bytes))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetKernelObjectSecurity(IntPtr handle, int information, byte[]? descriptor, int length, out int needed);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetKernelObjectSecurity(IntPtr handle, int information, byte[] descriptor);
}
