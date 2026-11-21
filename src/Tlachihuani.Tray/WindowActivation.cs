// Tlachihuani — Angela López Mendoza · XAIHT
namespace Tlachihuani.Tray;

/// <summary>A repeated user launch raises the existing window; it never starts a service.</summary>
internal sealed class WindowActivation : IDisposable
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };

    internal WindowActivation(Form window, EventWaitHandle signal)
    {
        // Five checks a second: written only when a second launch actually asks to show the window.
        timer.Tick += (_, _) => { using var pass = Tlachihuani.Contracts.TraceLog.Pass("XT window activation check"); if (signal.WaitOne(0)) Show(window); };
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
