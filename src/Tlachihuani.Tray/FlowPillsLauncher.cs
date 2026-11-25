// Tlachihuani — Angela López Mendoza · XAIHT
namespace Tlachihuani.Tray;

internal static class FlowPillsLauncher
{
    // Flow Pills runs as its own XT process, so its flows go on when the tray or the service
    // panel closes, and a flow can never take the tray down with it. The executable and the
    // argument are fixed by this app.
    internal static void Open(Form? owner)
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--flow-pills");
            using var launched = System.Diagnostics.Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ServiceDialogs.Notice(owner, "Open Flow Pills", "Windows could not open the Flow Pills Service window. " + ex.Message);
        }
    }
}
