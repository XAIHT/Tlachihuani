// Tlachihuani — Angela López Mendoza · XAIHT
using System.Diagnostics;
using System.Runtime.InteropServices;

// What a visible check does on the desktop: bring a window to the front, check its layout, and have
// Tlamatini's Shoter photograph the screen. Tlachihuani.HandGI.Checks compiles this same file, so
// HandGI's windows are held to the same rules as XT's.
internal static class DesktopChecks
{
    // A whole-desktop photo by Tlamatini's Shoter agent (tests/integration/rc_shoter_photo.py), when a
    // visible run asks for photos: TLACHIHUANI_CHECKS_PHOTOS (folder), _PYTHON and _SHOTER (script).
    // If Shoter fails, the check fails: there is no other camera.
    internal static void Photo(string name)
    {
        string? folder = Environment.GetEnvironmentVariable("TLACHIHUANI_CHECKS_PHOTOS");
        string? python = Environment.GetEnvironmentVariable("TLACHIHUANI_CHECKS_PYTHON");
        string? script = Environment.GetEnvironmentVariable("TLACHIHUANI_CHECKS_SHOTER");
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(python) || string.IsNullOrWhiteSpace(script)) return;
        Directory.CreateDirectory(folder);
        var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add(script); start.ArgumentList.Add(folder); start.ArgumentList.Add(name + ".png");
        using var capture = Process.Start(start) ?? throw new InvalidOperationException("Shoter did not start.");
        capture.WaitForExit();
        if (capture.ExitCode != 0) throw new InvalidOperationException($"Shoter could not take the photo {name} (exit {capture.ExitCode}).");
        Console.WriteLine("PHOTO " + Path.Combine(folder, name + ".png"));
    }

    internal static void Pump(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); }
    }

    internal static IEnumerable<Control> All(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in All(child)) yield return nested;
        }
    }

    // Checks a window and everything in it: nothing scrolls, nothing sticks out of its parent or covers
    // a neighbour, and every text fits. mayScroll names the lists built to scroll up and down (at a
    // window's smallest size); nothing ever scrolls sideways. choices also checks check boxes, whose box
    // needs room beside the words, and drop-down lists, where every choice must fit, not only the one shown.
    internal static int Audit(Control parent, string view, Func<Control, bool>? mayScroll = null, bool choices = false)
    {
        var children = parent.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
        int count = 0;
        var area = parent.ClientRectangle;
        if (parent is ScrollableControl panel && (panel.VerticalScroll.Visible || panel.HorizontalScroll.Visible))
        {
            if (panel.HorizontalScroll.Visible || mayScroll?.Invoke(panel) != true)
                throw new Exception($"{view}: unexpected scrolling in {parent.GetType().Name}.");
            // Its rows lie in the scrolled area, as wide as the list itself.
            area = new Rectangle(area.X, panel.DisplayRectangle.Y, area.Width, panel.DisplayRectangle.Height);
        }
        foreach (var child in children)
        {
            string id = $"{view}: {child.GetType().Name} {child.Text}";
            if (!area.Contains(child.Bounds) || child.Width <= 0 || child.Height <= 0)
                throw new Exception($"{id}: {child.Bounds} outside {area}.");
            foreach (var sibling in children.Where(other => other != child))
                if (Rectangle.Intersect(child.Bounds, sibling.Bounds) is { Width: > 0, Height: > 0 })
                    throw new Exception($"{id} overlaps {sibling.GetType().Name} {sibling.Text}.");
            using var graphics = child.CreateGraphics();
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            var available = child.ClientSize;
            if (child is Label or ButtonBase)
            {
                int width = available.Width - (child is RadioButton ? child.LogicalToDeviceUnits(22) : 0);
                var needed = TextRenderer.MeasureText(graphics, child.Text, child.Font, Size.Empty, flags);
                if (child is Label && needed.Width > width)
                    needed = TextRenderer.MeasureText(graphics, child.Text, child.Font, new(width, 10000), TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                if (needed.Width > width || needed.Height > available.Height)
                    throw new Exception($"{id}: requires {needed}, available {width}x{available.Height}.");
            }
            if (choices && child is CheckBox box)
            {
                var wanted = box.GetPreferredSize(Size.Empty);
                if (wanted.Width > available.Width || wanted.Height > available.Height)
                    throw new Exception($"{id}: requires {wanted} with its box, available {available.Width}x{available.Height}.");
            }
            if (choices && child is ComboBox list)
            {
                int arrow = SystemInformation.GetVerticalScrollBarWidthForDpi(list.DeviceDpi) + list.LogicalToDeviceUnits(8);
                foreach (var item in list.Items)
                {
                    string text = list.GetItemText(item) ?? "";
                    int width = TextRenderer.MeasureText(graphics, text, list.Font, Size.Empty, flags).Width + arrow;
                    if (width > list.Width) throw new Exception($"{id}: the choice \"{text}\" requires {width}px, available {list.Width}.");
                }
            }
            if (child is PictureBox picture && (picture.Image == null || picture.SizeMode != PictureBoxSizeMode.Zoom))
                throw new Exception($"{id}: brand image missing or cropped.");
            if (child is TextBox field)
            {
                if (field.Height < field.PreferredHeight) throw new Exception($"{id}: input height {field.Height} < {field.PreferredHeight}.");
                var placeholder = TextRenderer.MeasureText(graphics, field.PlaceholderText, field.Font, Size.Empty, flags);
                if (placeholder.Width > available.Width) throw new Exception($"{id}: placeholder requires {placeholder.Width}px.");
            }
            if (child is RichTextBox rich && rich.TextLength > 0)
            {
                if (view == "erasure-review-many")
                {
                    rich.Select(rich.TextLength, 0);
                    rich.ScrollToCaret();
                }
                var end = rich.GetPositionFromCharIndex(rich.TextLength - 1);
                if (end.X < 0 || end.Y < 0 || end.X >= rich.ClientSize.Width || end.Y + rich.Font.Height > rich.ClientSize.Height)
                    throw new Exception($"{id}: last line outside the text viewport.");
                if (view == "erasure-review-many") { rich.Select(0, 0); rich.ScrollToCaret(); }
            }
            count += 1 + Audit(child, view, mayScroll, choices);
        }
        return count;
    }

    // Windows gives the foreground only to the program the user used last; a test started from a
    // console is not that program. The window under test joins the foreground window's input for
    // one call, the way assistive tools do, so the photo shows it and nothing covers it.
    internal static void TakeForeground(Form window)
    {
        var foreground = GetForegroundWindow();
        if (foreground == window.Handle) return;
        uint other = GetWindowThreadProcessId(foreground, out _), mine = GetCurrentThreadId();
        bool attached = other != 0 && other != mine && AttachThreadInput(mine, other, true);
        try { BringWindowToTop(window.Handle); SetForegroundWindow(window.Handle); window.Activate(); }
        finally { if (attached) AttachThreadInput(mine, other, false); }
    }

    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool join);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
