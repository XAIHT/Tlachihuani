// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (window messages; the service writes what the shortcut does)
using System.Runtime.InteropServices;

namespace HandGI.App;

// Ctrl+Alt+H pauses or resumes hand control from any program. A message-only window holds the
// shortcut, so it works with no HandGI window open.
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int Id = 0x4847;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x1, ModControl = 0x2, ModNoRepeat = 0x4000;

    internal event Action? Pressed;
    internal bool Registered { get; }

    internal HotkeyWindow()
    {
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
        Registered = RegisterHotKey(Handle, Id, ModControl | ModAlt | ModNoRepeat, (uint)Keys.H);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == Id)
        {
            Pressed?.Invoke();
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        if (Registered) UnregisterHotKey(Handle, Id);
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr window, int id);
}
