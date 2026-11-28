// Tlachihuani — Angela López Mendoza · XAIHT
using Tlachihuani.Contracts;
using Tlachihuani.Windows;

namespace HandGI.App;

// Tlachihuani.HandGI.exe, Tlachihuani's hand-gesture service. It runs in the signed-in user's
// session, because the camera, the sound volume and its keyboard shortcut belong to that session.
//
//   --service                         run in the background: camera, signs, Ctrl+Alt+H
//   --camera-off                      start with the camera off (Camera settings turn it on)
//   --show signs|camera|try|expert    also open that window (no arguments opens "signs")
//   selftest | gpus | cameras | formats | inspect | run ...    the engine's own checks (Tools)
//
// A second launch never starts a second HandGI: it asks the running one to open the window.
// XT starts HandGI, asks it how it is through its pipe and ends it with Stop Services. Everything
// HandGI does is written to Tlachihuani.log as component "handgi".
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && Tools.Handles(args[0]))
        {
            LogPaths.UseUserLog("handgi-tool");
            return Tools.Run(args);
        }
        LogPaths.UseUserLog("handgi");
        string? page = args.Length == 0 ? "signs" : null;
        bool cameraOff = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--service") continue;
            if (args[i] == "--camera-off") { cameraOff = true; continue; }
            if (args[i] == "--show" && i + 1 < args.Length) { page = args[++i]; continue; }
            TraceLog.W($"HandGI ignored the unknown argument \"{args[i]}\"");
        }
        if (page is not null && !HandGIEndpoint.Pages.Contains(page))
        {
            TraceLog.W($"HandGI has no \"{page}\" window; opening the signs");
            page = "signs";
        }
        ApplicationConfiguration.Initialize();
        return HandGIService.Run(page, cameraOff);
    }
}
