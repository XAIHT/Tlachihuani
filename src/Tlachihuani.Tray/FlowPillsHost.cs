// Tlachihuani — Angela López Mendoza · XAIHT
using System.Globalization;
using Tlachihuani.Contracts;
using Tlachihuani.FlowPills;
using Tlachihuani.Windows;

namespace Tlachihuani.Tray;

// XT's Flow Pills mode: Tlachihuani.Tray.exe --flow-pills [flow files]. It runs in the signed-in
// user's session, because Tlamatini's agents need that user's desktop, profile and models; it is
// never a Session 0 service. One window per user: a second launch hands its files to the first
// one through the inbox and asks it to come forward.
//
// The open window publishes its process (FlowPills\process.json, with the number of flows that run
// or wait), so the Services panel can show it and Stop Services can end it. Its Stop signal asks it
// to stop every flow and close.
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
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, FlowPillsSignals.Show);
        using var stop = new EventWaitHandle(false, EventResetMode.AutoReset, FlowPillsSignals.Stop);
        using var singleton = new Mutex(true, FlowPillsSignals.Singleton, out bool first);
        if (!first)
        {
            if (flows.Count > 0) FlowPillsInbox.Post(flows);
            show.Set();
            TraceLog.I($"Flow Pills is already open; handed it {flows.Count} file(s)");
            return 0;
        }
        TraceLog.I($"Flow Pills opens (version {Application.ProductVersion.Split('+')[0]}, {flows.Count} file(s) given)");
        var record = UserProcessRecord.ForThisProcess("0");
        void Publish(int runs)
        {
            try { (record with { Detail = runs.ToString(CultureInfo.InvariantCulture) }).Publish(FlowPillsPaths.ProcessRecord); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TraceLog.W($"Flow Pills could not publish its process: {ex.Message}"); }
        }
        Publish(0);
        try
        {
            using var resource = typeof(FlowPillsHost).Assembly.GetManifestResourceStream("Tlachihuani.XT.ico")!;
            using var icon = new Icon(resource);
            using var window = new FlowPillsForm(icon, show, stop, flows, Publish);
            Application.Run(window);
        }
        finally
        {
            UserProcessRecord.Clear(FlowPillsPaths.ProcessRecord, Environment.ProcessId);
            singleton.ReleaseMutex();
        }
        return 0;
    }
}
