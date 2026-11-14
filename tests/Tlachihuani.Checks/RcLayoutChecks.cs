// Tlachihuani — Angela López Mendoza · XAIHT
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Tlachihuani.Tray;

internal static class RcLayoutChecks
{
    internal static int Run(string output, string captureScript, string python)
    {
        Exception? failure = null;
        var results = new List<object>();
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                using var resource = typeof(StatusForm).Assembly.GetManifestResourceStream("Tlachihuani.XT.ico")!;
                using var icon = new Icon(resource);
                using var form = new RemoteControllerForm(icon) { Text = "RC panel layout acceptance" };
                form.Show(); Pump(1200);
                var normal = form.Size;
                var tabs = All(form).OfType<TabControl>().Single();
                foreach (var minimum in new[] { false, true })
                {
                    form.Size = minimum ? form.MinimumSize : normal;
                    foreach (TabPage page in tabs.TabPages)
                    {
                        tabs.SelectedTab = page; form.Activate(); Pump(300);
                        string name = page.Text + (minimum ? " minimum" : " normal");
                        if (!form.Visible || GetForegroundWindow() != form.Handle) throw new Exception("RC layout window is not foreground.");
                        if (!Screen.FromControl(form).WorkingArea.Contains(form.Bounds)) throw new Exception("RC panel exceeds the desktop.");
                        foreach (var flow in All(page).OfType<FlowLayoutPanel>())
                            if (flow.HorizontalScroll.Visible) throw new Exception(name + ": horizontal scrolling clips controls.");
                        foreach (var control in All(page).Where(c => c.Visible))
                        {
                            if (control is Label && control.Text.Length == 0) continue;
                            if (control.Width <= 0 || control.Height <= 0) throw new Exception(name + ": empty control " + control.GetType().Name + " " + control.Text);
                            if (control is Button b && string.IsNullOrWhiteSpace(b.Text) && string.IsNullOrWhiteSpace(b.AccessibleName)) throw new Exception(name + ": unnamed button.");
                            if (control.Parent is FlowLayoutPanel parent && control.Right > parent.ClientSize.Width - (parent.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0)) throw new Exception(name + ": control extends past the available width: " + control.Text);
                        }
                        results.Add(new { name, passed = true, dpi = form.DeviceDpi, width = form.Width, height = form.Height });
                        Console.WriteLine("PASS RC UI " + name + " at " + form.DeviceDpi + " DPI");
                        if (!minimum && page.Text is "Runtime" or "Access" or "Network")
                        {
                            Capture("rc-panel-" + page.Text.ToLowerInvariant());
                        }
                    }
                }
                form.Size = normal;
                Exception? dialogFailure = null;
                using var inspect = new System.Windows.Forms.Timer { Interval = 600 };
                inspect.Tick += (_, _) =>
                {
                    inspect.Stop();
                    var dialog = Application.OpenForms.Cast<Form>().SingleOrDefault(f => f.Owner == form && f.Text == "Remote-Controller · Invitation");
                    try
                    {
                        if (dialog is null || GetForegroundWindow() != dialog.Handle) throw new Exception("RC invitation dialog is not foreground.");
                        var controls = All(dialog).ToArray();
                        if (controls.Any(c => c.Text.Contains("Torrenter", StringComparison.Ordinal))) throw new Exception("RC invitation uses another service's identity.");
                        if (!controls.OfType<TextBox>().Single().UseSystemPasswordChar) throw new Exception("Invitation should initially be concealed.");
                        if (!controls.Any(c => c.Text == "Copy invitation") || !controls.Any(c => c.Text.Contains("five minutes", StringComparison.Ordinal))) throw new Exception("Invitation copy action or expiry is missing.");
                        Capture("rc-panel-invitation");
                        results.Add(new { name = "RC invitation identity, concealment and expiry", passed = true, dpi = dialog.DeviceDpi });
                        Console.WriteLine("PASS RC UI invitation identity, concealment and expiry");
                    }
                    catch (Exception ex) { dialogFailure = ex; }
                    finally { dialog?.Close(); }
                };
                inspect.Start();
                ServiceDialogs.ShowRcInvitation(form, new string('0', 64));
                if (dialogFailure is not null) throw dialogFailure;
                form.Close();

                void Capture(string label)
                {
                    var start = new ProcessStartInfo(python) { UseShellExecute = false };
                    start.ArgumentList.Add(Path.GetFullPath(captureScript)); start.ArgumentList.Add(label);
                    using var capture = Process.Start(start) ?? throw new Exception("Shoter did not start.");
                    capture.WaitForExit(); if (capture.ExitCode != 0) throw new Exception("Shoter capture failed.");
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) { results.Add(new { name = "RC UI failure", passed = false, error = failure.ToString() }); Console.Error.WriteLine(failure); }
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        return failure is null ? 0 : 1;
    }
    private static IEnumerable<Control> All(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var nested in All(child)) yield return nested; }
    }
    private static void Pump(int milliseconds)
    {
        var clock = Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); }
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
