// Tlachihuani — Angela López Mendoza · XAIHT
using System.Diagnostics;
using Tlachihuani.Contracts;
using Tlachihuani.FlowPills;
using Tlachihuani.Windows;

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
            var start = new ProcessStartInfo(Application.ExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--flow-pills");
            using var launched = Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ServiceDialogs.Notice(owner, "Open Flow Pills", "Windows could not open the Flow Pills Service window. " + ex.Message);
        }
    }

    // The open Flow Pills: whether it runs and how many flows run or wait in it.
    internal static (bool Running, int Runs) Observe()
    {
        var record = UserProcessRecord.Read(FlowPillsPaths.ProcessRecord);
        using var process = record?.OpenVerified(FlowPillsSignals.ExecutableName);
        if (process is null || record is null) return (false, 0);
        return (true, int.TryParse(record.Detail, out int runs) ? runs : 0);
    }

    // Asks the open Flow Pills to stop every flow the way Tlamatini's Stop does, and to close.
    // Flows get 30 seconds to end; Flow Pills is ended if it has not closed 10 seconds after that.
    internal static async Task StopAsync(CancellationToken token)
    {
        if (!Observe().Running) return;
        if (!FlowPillsSignals.RequestStop()) throw new InvalidOperationException("Flow Pills did not receive the request to stop.");
        TraceLog.I("XT asked Flow Pills to stop its flows and close");
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(40))
        {
            if (!Observe().Running) return;
            await Task.Delay(250, token);
        }
        var outcome = await ForcedStop.UserProcessAsync("Flow Pills", FlowPillsPaths.ProcessRecord, FlowPillsSignals.ExecutableName, token);
        if (!outcome.Down) throw new InvalidOperationException(outcome.Message);
    }
}
