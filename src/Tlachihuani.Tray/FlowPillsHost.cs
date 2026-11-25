// Tlachihuani — Angela López Mendoza · XAIHT
using Tlachihuani.Contracts;
using Tlachihuani.FlowPills;

namespace Tlachihuani.Tray;

// XT's Flow Pills mode: Tlachihuani.Tray.exe --flow-pills [flow files]. It runs in the signed-in
// user's session, because Tlamatini's agents need that user's desktop, profile and models; it is
// never a Session 0 service. One window per user: a second launch hands its files to the first
// one through the inbox and asks it to come forward.
internal static class FlowPillsHost
{
    internal static int Run(IReadOnlyList<string> files)
    {
        var flows = new List<string>();
        foreach (var file in files)
        {
            try { flows.Add(Path.GetFullPath(file)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { TraceLog.W($"Flow Pills ignored an unusable path ({ex.GetType().Name})"); }
        }
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\XAIHT.Tlachihuani.FlowPills.Show.v1");
        using var singleton = new Mutex(true, @"Local\XAIHT.Tlachihuani.FlowPills.v1", out bool first);
        if (!first)
        {
            if (flows.Count > 0) FlowPillsInbox.Post(flows);
            show.Set();
            TraceLog.I($"Flow Pills is already open; handed it {flows.Count} file(s)");
            return 0;
        }
        TraceLog.I($"Flow Pills opens (version {Application.ProductVersion.Split('+')[0]}, {flows.Count} file(s) given)");
        using var resource = typeof(FlowPillsHost).Assembly.GetManifestResourceStream("Tlachihuani.XT.ico")!;
        using var icon = new Icon(resource);
        using var window = new FlowPillsForm(icon, show, flows);
        Application.Run(window);
        singleton.ReleaseMutex();
        return 0;
    }
}
