// Tlachihuani — Angela López Mendoza · XAIHT
namespace Tlachihuani.Tray;

internal static class RemoteControllerLauncher
{
    internal static void Open(Form? owner)
    {
        try
        {
            // Every user entry point shares Program's standalone panel
            // singleton. The executable and argument are fixed by this app.
            var start = new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--remote-controller");
            using var launched = System.Diagnostics.Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ServiceDialogs.Notice(owner, "Open Remote-Controller", "Windows could not open the Remote-Controller panel. " + ex.Message);
        }
    }
}
