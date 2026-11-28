// Tlachihuani — Angela López Mendoza · XAIHT
using System.Text.Json.Nodes;
using Tlachihuani.Contracts;
using Tlachihuani.Tray;
using static DesktopChecks;

internal static class TrayLayoutChecks
{
    internal static int Run(string directory, string mode)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Exercise(Path.GetFullPath(directory), mode); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure == null) return 0;
        Console.Error.WriteLine(failure); return 1;
    }

    private static void Exercise(string directory, string mode)
    {
        Application.SetHighDpiMode(mode switch
        {
            "desktop" => HighDpiMode.PerMonitorV2, "96dpi" => HighDpiMode.DpiUnaware,
            _ => throw new ArgumentException("Choose desktop or 96dpi.")
        });
        Application.EnableVisualStyles(); Directory.CreateDirectory(directory);
        using var resource = typeof(StatusForm).Assembly.GetManifestResourceStream("Tlachihuani.XT.ico")!;
        using var icon = new Icon(resource);
        using var owner = new StatusForm(icon);
        owner.Show();
        void Render(string scm, string health, ErrorInfo? error = null)
        {
            bool online = scm == "running";
            var service = new ServiceStatus("Tlachihuani", scm, health, online ? 12345 : null, null, online ? "0.1.0" : null,
                online ? 1234567 : null, null);
            owner.Render(new(1, Guid.NewGuid(), true, "status", "succeeded", DateTimeOffset.UtcNow, "test", null, service,
                new(true, true, true, false, null), error), new JobsState(online ? "running" : "stopped", online), false);
        }
        void Snap(Form dialog, string name)
        {
            dialog.Activate(); TakeForeground(dialog); Application.DoEvents(); Pump(180);
            if (GetForegroundWindow() != dialog.Handle || !dialog.Visible) throw new Exception($"Window is not foreground: {name}");
            int count = Audit(dialog, name);
            if (!Screen.FromControl(dialog).WorkingArea.Contains(dialog.Bounds)) throw new Exception($"{name}: window exceeds the working area.");
            var rectangle = dialog.RectangleToScreen(dialog.ClientRectangle);
            using var screenshot = new Bitmap(rectangle.Width, rectangle.Height);
            using (var graphics = Graphics.FromImage(screenshot))
                graphics.CopyFromScreen(rectangle.Location, Point.Empty, rectangle.Size);
            screenshot.Save(Path.Combine(directory, $"{name}-{dialog.DeviceDpi}.png"));
            Console.WriteLine($"PASS {name}: {count} visible controls, {dialog.DeviceDpi} DPI, client {dialog.ClientSize}.");
        }

        Render("stopped", "unknown"); Snap(owner, "overview-stopped");
        owner.Size = owner.MinimumSize;
        foreach (var scm in new[] { "stopped", "start_pending", "running", "stop_pending", "paused", "not_installed", "unknown" })
        {
            Render(scm, scm == "running" ? "healthy" : "unknown"); Snap(owner, "overview-min-" + scm);
        }
        Render("stop_pending", "unknown", new("timeout", "The service did not reach the requested state before the deadline. Refresh its status; no force termination was attempted."));
        Snap(owner, "overview-min-timeout");
        using (var services = new ServicesForm(icon))
        {
            services.Show(owner); Pump(420);
            var size = services.Size;
            foreach (bool minimum in new[] { false, true })
            {
                services.Size = minimum ? services.MinimumSize : size;
                foreach (string view in new[] { "Runtime", "Access", "xaiht.org" })
                {
                    All(services).OfType<Button>().Single(b => b.Text == view).PerformClick();
                    Snap(services, "services-" + view.ToLowerInvariant() + (minimum ? "-min" : ""));
                    if (!minimum) Photo($"services-{view.ToLowerInvariant()}-{mode}");
                }
            }
            services.Close();
        }

        void Modal(string name, Action open)
        {
            Exception? error = null;
            using var timer = new System.Windows.Forms.Timer { Interval = 200 };
            timer.Tick += (_, _) =>
            {
                if (Form.ActiveForm is not StudioForm dialog || dialog == owner) return;
                timer.Stop();
                try
                {
                    Snap(dialog, name);
                    if (name == "credential-result")
                    {
                        var reveal = All(dialog).OfType<Button>().Single(b => b.Text == "Show token");
                        reveal.PerformClick(); Snap(dialog, "credential-result-revealed");
                        reveal.PerformClick();
                    }
                }
                catch (Exception ex) { error = ex; }
                finally { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); }
            };
            timer.Start(); open(); if (error != null) throw error;
        }

        Modal("credential-issue", () => ServiceDialogs.RequestCredential(owner));
        Modal("credential-result", () => ServiceDialogs.ShowCredential(owner, "sample-token-for-layout-verification-only"));
        const string transfers = "Active transfers will be checkpointed. 10 active transfers across 250 records.";
        foreach (var (title, text, button) in new[]
        {
            ("Stop the Jobs host?", transfers, "Stop Jobs"), ("Stop Torrenter?", transfers, "Stop provider"),
            ("Restart Torrenter?", transfers, "Restart provider"),
            ("Revoke account access?", "This revokes all REST sessions and enrolled gateways for your Windows account.", "Revoke access"),
            ("Stop Remote-Controller?", "Remote clients will disconnect and RC schedules will pause. Tlamatini and Torrenter keep running.", "Stop server"),
            ("Stop Flow Pills?", "12 flows will be stopped the way Tlamatini's Stop does, then Flow Pills closes.", "Stop flows")
        })
            Modal("confirm-" + button.Replace(' ', '-'), () => ServiceDialogs.Confirm(owner, title, text, button));
        Modal("service-ready", () => ServiceDialogs.Notice(owner, "Services ready", "Open your downloads from the signed-in Tlamatini account."));
        Modal("no-files", () => ServiceDialogs.Notice(owner, "File erasure", "There are no pending file-erasure requests."));
        Modal("operation-error", () => ServiceDialogs.Notice(owner, "Service action failed", "Windows could not complete this operation. Check the service status and try again."));
        Modal("link-error", () => ServiceDialogs.Notice(null, "Open Tlamatini", "Windows could not open the configured Tlamatini link."));
        Modal("tray-help", () => ServiceDialogs.Notice(null, "Open the XT tray", "Run Tlachihuani.Tray.exe to show the tray icon. Add --show to open the service overview."));
        JsonObject Preview(int count) => new()
        {
            ["job_id"] = new string('b', 32),
            ["files"] = new JsonArray(Enumerable.Range(1, count).Select(n => (JsonNode)new JsonObject
                { ["path"] = $"fixture/volume-{n:D2}/a-long-test-filename-for-review-only.epub", ["bytes"] = 192003 }).ToArray())
        };
        Modal("erasure-review", () => ServiceDialogs.ReviewFiles(owner, Preview(2)));
        Modal("erasure-review-many", () => ServiceDialogs.ReviewFiles(owner, Preview(32)));
        owner.AllowExit = true; owner.Close();
    }
}
