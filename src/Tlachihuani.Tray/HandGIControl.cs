// Tlachihuani — Angela López Mendoza · XAIHT
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Tlachihuani.Contracts;
using Tlachihuani.Core;
using Tlachihuani.Windows;

namespace Tlachihuani.Tray;

// What the Services panel does with HandGI. HandGI is its own process in the user's session; every
// request goes to the process its record names, checked by HandGIEndpoint.
internal static class HandGIControl
{
    internal sealed record State(string Word, bool Running, bool Answering, bool HandControlOn, int SignsInUse, string Camera, string Message);

    internal static readonly State NotInstalled = new("not_installed", false, false, false, 0, "", "");
    internal static readonly State Stopped = new("stopped", false, false, false, 0, "", "");

    internal static async Task<State> ObserveAsync(CancellationToken token)
    {
        if (!HandGIEndpoint.IsRunning()) return HandGIEndpoint.FindExecutable() is null ? NotInstalled : Stopped;
        try
        {
            var reply = await HandGIEndpoint.CallAsync("status", token);
            return new((string?)reply["state"] ?? "unknown", true, true, (string?)reply["hand_control"] == "on",
                (int?)reply["signs_in_use"] ?? 0, (string?)reply["camera"] ?? "", (string?)reply["message"] ?? "");
        }
        catch (Exception ex) when (ex is LifecycleException or TimeoutException or IOException or Win32Exception or ProtocolException or UnauthorizedAccessException)
        {
            return new("unreachable", true, false, false, 0, "", ex.Message);
        }
    }

    // Starts HandGI (and opens a window when a page is given), then waits until it answers.
    internal static async Task StartAsync(CancellationToken token, string? page = null)
    {
        if (HandGIEndpoint.IsRunning())
        {
            if (page is not null) await ShowAsync(page, token);
            return;
        }
        HandGIEndpoint.Launch(page is null ? ["--service"] : ["--service", "--show", page]);
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(15))
        {
            await Task.Delay(250, token);
            if (!HandGIEndpoint.IsRunning()) continue;
            try
            {
                await HandGIEndpoint.CallAsync("status", token);
                TraceLog.I("HandGI answers XT");
                return;
            }
            catch (Exception ex) when (ex is LifecycleException or TimeoutException or IOException or Win32Exception or ProtocolException) { }
        }
        throw new TimeoutException("HandGI did not answer within 15 seconds of starting. See Tlachihuani.log (component handgi).");
    }

    // Asks HandGI to stop; one that does not leave within 8 seconds is ended.
    internal static async Task StopAsync(CancellationToken token)
    {
        if (!HandGIEndpoint.IsRunning()) return;
        try { await HandGIEndpoint.CallAsync("stop", token); }
        catch (Exception ex) when (ex is LifecycleException or TimeoutException or IOException or Win32Exception or ProtocolException)
        { TraceLog.W($"HandGI did not take the stop request ({ex.Message}); it will be ended"); }
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(8))
        {
            if (!HandGIEndpoint.IsRunning()) return;
            await Task.Delay(200, token);
        }
        var outcome = await ForcedStop.UserProcessAsync("HandGI", HandGIEndpoint.RecordPath, HandGIEndpoint.ExecutableName, token);
        if (!outcome.Down) throw new InvalidOperationException(outcome.Message);
    }

    internal static Task SetHandControlAsync(bool on, CancellationToken token) => HandGIEndpoint.CallAsync(on ? "arm" : "disarm", token);

    // Opens one of HandGI's windows, starting HandGI when it is not running.
    internal static async Task ShowAsync(string page, CancellationToken token)
    {
        int pid;
        using (var process = HandGIEndpoint.OpenRunning())
        {
            if (process is null)
            {
                await StartAsync(token, page);
                return;
            }
            pid = process.Id;
        }
        // XT is in front, so it may let HandGI bring its window forward.
        AllowSetForegroundWindow(pid);
        await HandGIEndpoint.CallAsync("show", token, page);
    }

    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
}
