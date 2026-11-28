// Tlachihuani — Angela López Mendoza · XAIHT
using System.Diagnostics;
using HandGI.App;
using HandGI.Core.Gestures;
using HandGI.Core.Util;
using Tlachihuani.Tray;
using Tlachihuani.Windows;
using static DesktopChecks;

// HandGI's windows, held to the rules XT's windows are (TrayLayoutChecks): at the screen's own DPI
// ("desktop") or at 96 DPI, every window opens on the main screen, Shoter photographs it, and all it
// shows must fit, with nothing cut, covered or scrolling sideways. At its usual size the Signs page
// shows every sign without scrolling. HandGI runs in a test folder of its own and the camera stays
// off: nothing here creates HandGI's engine, and the expert view, which turns the camera on as it
// opens, is not shown.
internal static class HandGILayoutChecks
{
    // The longest name Teach a sign accepts: 40 characters.
    private const string LongestName = "Open hand twice to skip to the next song";

    internal static int Run(string mode)
    {
        // HandGI keeps its settings, signs and log here. It is set before anything of HandGI runs,
        // because HandGI reads it once.
        string folder = Path.Combine(Path.GetTempPath(), "tlachihuani-checks", "handgi-layout-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("TLACHIHUANI_USER_DIR", folder);
        Console.WriteLine("HandGI test folder: " + folder);
        var problems = new List<string>();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Exercise(mode, problems); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) { Console.Error.WriteLine(failure); return 1; }
        if (problems.Count == 0) { Console.WriteLine("Every HandGI window passed."); return 0; }
        Console.WriteLine($"{problems.Count} HandGI window(s) did not pass:");
        foreach (string problem in problems) Console.WriteLine("  " + problem);
        return 1;
    }

    private static void Exercise(string mode, List<string> problems)
    {
        Application.SetHighDpiMode(mode switch
        {
            "desktop" => HighDpiMode.PerMonitorV2, "96dpi" => HighDpiMode.DpiUnaware,
            _ => throw new ArgumentException("Choose desktop or 96dpi.")
        });
        Application.EnableVisualStyles();
        LogPaths.UseUserLog("handgi-checks");
        var log = new HandGiLog(null) { MinimumLevel = LogLevel.Debug };
        LogBridge.Attach(log);
        var app = new AppController(log);
        var host = new LayoutHost(app);
        try
        {
            // Two signs of the user's own beside the seven ready-made ones; one has the longest name.
            app.Profile.AddMine("Peace sign", GestureAction.VolumeUp);
            app.Profile.AddMine(LongestName, GestureAction.VolumeDown);
            int signs = app.Profile.Classes.Count;

            void Snap(Form window, string name, bool smallest = false)
            {
                window.Activate(); TakeForeground(window); Application.DoEvents(); Pump(180);
                Photo($"{name}-{mode}");
                try
                {
                    if (GetForegroundWindow() != window.Handle || !window.Visible) throw new Exception($"{name}: the window is not in front.");
                    // Only at the smallest size may a list scroll, and only up and down.
                    Func<Control, bool>? scrolls = smallest ? c => c is ScrollableControl { AutoScroll: true } : null;
                    int count = Audit(window, name, scrolls, choices: true);
                    if (!Screen.FromControl(window).WorkingArea.Contains(window.Bounds)) throw new Exception($"{name}: the window does not fit on its screen.");
                    Console.WriteLine($"PASS {name}: {count} visible controls, {window.DeviceDpi} DPI, client {window.ClientSize}.");
                }
                catch (Exception ex)
                {
                    problems.Add(ex.Message);
                    Console.WriteLine("FAIL " + ex.Message);
                }
            }

            // A window that waits for an answer: open shows it and returns when it closes.
            void Modal(string name, Action open, Action<Form>? then = null)
            {
                Form? seen = null;
                Exception? error = null;
                using var timer = new System.Windows.Forms.Timer { Interval = 200 };
                timer.Tick += (_, _) =>
                {
                    var dialog = Application.OpenForms.OfType<StudioForm>().FirstOrDefault(f => f.Modal && f.Visible);
                    if (dialog is null) return;
                    timer.Stop();
                    seen = dialog;
                    try { Snap(dialog, name); then?.Invoke(dialog); }
                    catch (Exception ex) { error = ex; }
                    finally { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); }
                };
                timer.Start(); open(); timer.Stop();
                if (error != null) throw error;
                if (seen is null) throw new Exception($"{name}: the window did not open.");
            }

            using var settings = new SettingsForm(host);
            OnMainScreen(settings);
            settings.Show(); Pump(420);
            // The sign list, inside the one panel that scrolls.
            var list = All(settings).OfType<Panel>().Single(p => p.AutoScroll).Controls.OfType<TableLayoutPanel>().Single();
            if (list.Controls.Count != signs) throw new Exception($"The Signs page shows {list.Controls.Count} signs, not {signs}.");
            // The usual size, the smallest, and the usual size again: a window must also look right once
            // it has been made smaller and bigger again.
            var usual = settings.Size;
            foreach (string size in new[] { "", "-min", "-after-resize" })
            {
                bool smallest = size == "-min";
                settings.Size = smallest ? settings.MinimumSize : usual; Pump(200);
                foreach (string page in new[] { "signs", "camera", "try" })
                {
                    settings.ShowPage(page); Pump(120);
                    if (page == "camera") Console.WriteLine("The Camera page lists: " + CamerasListed(settings));
                    Snap(settings, $"handgi-{page}{size}", smallest);
                }
            }
            settings.ShowPage("signs"); Pump(200);

            // Removing a sign of the user's own asks first; the check answers Cancel.
            Modal("handgi-remove-sign", () => Press(settings, "Remove"));
            if (app.Profile.MineCount != 2) throw new Exception("Cancel in the Remove question removed the sign anyway.");

            // Teach a sign at its usual and smallest sizes, then Start with the camera off: HandGI asks
            // to turn the camera on, the check says no, and the window says what to do.
            Modal("handgi-teach", () => Press(settings, "Teach a new sign…"), teach =>
            {
                var size = teach.Size;
                teach.Size = teach.MinimumSize; Pump(200);
                Snap(teach, "handgi-teach-min", smallest: true);
                teach.Size = size; Pump(200);
                Press(teach, "Start"); Pump(200);
                Snap(teach, "handgi-teach-camera-off");
            });
            if (host.CameraRequests != 1) throw new Exception($"Start in Teach a sign asked for the camera {host.CameraRequests} times, not once.");

            // The volume display: at the bottom of the main screen, in front of everything, without
            // taking the keyboard from the window in front.
            using (var osd = new VolumeOsd())
            {
                settings.Activate(); TakeForeground(settings); Pump(180);
                var front = GetForegroundWindow();
                osd.ShowLevel(0.64f, false, "Peace sign · " + SettingsForm.Describe(GestureAction.VolumeUp)); Pump(300);
                Photo($"handgi-volume-display-{mode}");
                var area = Screen.PrimaryScreen!.WorkingArea;
                var now = GetForegroundWindow();
                string? problem = !osd.Visible || !area.Contains(osd.Bounds) ? "handgi-volume-display: it is not inside the main screen."
                    : front != settings.Handle ? "handgi-volume-display: the Signs window could not be brought to the front first."
                    : now == osd.Handle ? "handgi-volume-display: the volume display itself took the keyboard from the Signs window."
                    : now != front ? $"handgi-volume-display: the keyboard moved to {(Control.FromHandle(now) is Form other ? "\"" + other.Text + "\"" : "a window of another program")}."
                    : null;
                if (problem is null) Console.WriteLine($"PASS handgi-volume-display: {osd.Bounds.Size} at the bottom of the main screen; the keyboard stayed where it was.");
                else { problems.Add(problem); Console.WriteLine("FAIL " + problem); }
                osd.Hide();
            }

            // HandGI's engine owns the camera; it was never created, so the camera never came on.
            if (app.Engine is not null) throw new Exception("HandGI's engine was created during the layout check.");
            Console.WriteLine("The camera stayed off: HandGI's engine was never created, and Teach a sign was told no.");
            settings.Close();
        }
        finally { app.Shutdown(); }
    }

    // Every window opens on the main screen, whichever screen the mouse is on; StudioForm centres it there.
    private static void OnMainScreen(Form window)
    {
        window.StartPosition = FormStartPosition.Manual;
        window.Location = Screen.PrimaryScreen!.WorkingArea.Location;
    }

    // Clicks a button the way a person would: only one that is showing and can be used.
    private static void Press(Control window, string text)
    {
        var button = All(window).OfType<Button>().FirstOrDefault(b => b.Text == text && b.Visible && b.Enabled)
            ?? throw new Exception($"No \"{text}\" button is showing.");
        button.PerformClick();
    }

    // The Camera page lists the cameras the first time it opens; listing never turns one on.
    private static string CamerasListed(Form window)
    {
        var choice = All(window).OfType<ComboBox>().Single(c => c.AccessibleName == "Camera");
        var clock = Stopwatch.StartNew();
        while (choice.Text == "Looking for cameras…")
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(20)) throw new Exception("The Camera page was still looking for cameras after 20 seconds.");
            Application.DoEvents(); Thread.Sleep(20);
        }
        return choice.Text;
    }

    // HandGI's service as far as its windows need it, without a camera: a request to turn the camera
    // on is counted and refused, and the expert view, which would turn it on, never opens.
    private sealed class LayoutHost(AppController app) : IHandGIHost
    {
        public AppController App => app;
        public bool HandControlOn { get; private set; }
        public bool Starting => false;
        public string ProblemText => "";
        public int CameraRequests { get; private set; }
        public void SetHandControl(bool on, string source) => HandControlOn = on;
        public Task StartCameraAsync() { CameraRequests++; return Task.CompletedTask; }
        public Task StopCameraAsync() => Task.CompletedTask;
        public void ShowExpert() => throw new InvalidOperationException("The layout check never opens the expert view: it turns the camera on.");
    }
}
