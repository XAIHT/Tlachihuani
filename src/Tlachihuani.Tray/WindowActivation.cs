// Tlachihuani — Angela López Mendoza · XAIHT
namespace Tlachihuani.Tray;

/// <summary>A repeated user launch raises the existing window; it never starts a service.</summary>
internal sealed class WindowActivation : IDisposable
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };

    internal WindowActivation(Form window, EventWaitHandle signal)
    {
        timer.Tick += (_, _) => { if (signal.WaitOne(0)) Show(window); };
        timer.Start();
    }

    internal static void Show(Form window)
    {
        if (window.IsDisposed) return;
        window.Show();
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
        window.BringToFront();
        window.Activate();
    }

    public void Dispose() => timer.Dispose();
}
