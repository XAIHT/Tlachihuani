// Tlachihuani — Angela López Mendoza · XAIHT
namespace Tlachihuani.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Tlachihuani.Windows.LogPaths.UseUserLog(args.FirstOrDefault() switch
        {
            "--rc-stop-console" => "xt-rc-stop", "--remote-controller" => "xt-rc-panel", "--flow-pills" => "xt-flow-pills", _ => "xt"
        });
        if (args is ["--rc-stop-console", var edition, var pid, var created])
        {
            Environment.ExitCode = RcConsoleStop.Run(edition, pid, created);
            return;
        }
        ApplicationConfiguration.Initialize();
        if (args.Length > 0 && args[0] == "--flow-pills")
        {
            Environment.ExitCode = FlowPillsHost.Run(args[1..]);
            return;
        }
        if (args is ["--remote-controller"])
        {
            using var showPanel = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\XAIHT.Tlachihuani.RC.Show.v1");
            using var panelSingleton = new Mutex(true, @"Local\XAIHT.Tlachihuani.RC.Panel.v1", out var firstPanel);
            if (!firstPanel) { showPanel.Set(); return; }
            using var resource = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Tlachihuani.XT.ico")!;
            using var icon = new Icon(resource);
            using var panel = new RemoteControllerForm(icon);
            using var activation = new WindowActivation(panel, showPanel);
            Application.Run(panel);
            panelSingleton.ReleaseMutex();
            return;
        }
        if (args.Length > 1 || (args.Length == 1 && args[0] != "--show"))
        {
            ServiceDialogs.Notice(null, "Open the XT tray", "Run Tlachihuani.Tray.exe to show the tray icon. Add --show to open the service overview.");
            return;
        }
        using var showTray = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\XAIHT.Tlachihuani.Tray.Show.v1");
        using var singleton = new Mutex(true, @"Local\XAIHT.Tlachihuani.Tray.v1", out var first);
        if (!first) { if (args.Contains("--show")) showTray.Set(); return; }
        using var context = new TrayContext(args.Contains("--show"), showTray);
        Application.Run(context);
        singleton.ReleaseMutex();
    }
}
