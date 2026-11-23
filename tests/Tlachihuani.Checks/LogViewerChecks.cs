// Tlachihuani — Angela López Mendoza · XAIHT
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Tlachihuani.Tray;

internal static class LogViewerChecks
{
    private static int passed;
    private static readonly UTF8Encoding Utf8 = new(false);
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static void Pass(string name) { passed++; Console.WriteLine("PASS " + name); }

    // Opens just the rebuilt viewer, without launching XT or connecting to services.
    internal static int Preview(string file)
    {
        int exit = 1;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using var form = new LogViewerForm(null, file);
                form.ShowInTaskbar = true;
                form.Text = "Tlachihuani · Log viewer preview";
                Application.Run(form);
                exit = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        return exit;
    }

    internal static int Run(string directory)
    {
        int exit = 1;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Directory.CreateDirectory(directory);
                ReaderChecks(directory);
                HistoryChecks(directory);
                using var form = new LogViewerForm(null, Path.Combine(directory, "viewer.log"));
                form.ShowInTaskbar = true;
                form.Text = "Tlachihuani · Log viewer verification (synthetic data)";
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        var foregroundWait = Stopwatch.StartNew();
                        while (GetForegroundWindow() != form.Handle && foregroundWait.Elapsed < TimeSpan.FromSeconds(45)) await Task.Delay(150);
                        await Task.Delay(250);
                        Require(form.Visible && !IsIconic(form.Handle) && GetForegroundWindow() == form.Handle,
                            "The verification window must be visibly in front. No UI workload was started.");
                        await ViewerChecks(form, directory);
                        exit = 0;
                        Console.WriteLine($"RESULT: {passed} log viewer checks passed.");
                    }
                    catch (Exception ex) { Console.WriteLine("FAIL " + ex); File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString(), Utf8); }
                    finally { form.Close(); }
                };
                Application.Run(form);
            }
            catch (Exception ex) { Console.WriteLine("FAIL " + ex); File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString(), Utf8); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        return exit;
    }

    private static void ReaderChecks(string directory)
    {
        string file = Path.Combine(directory, "reader.log");
        var content = string.Concat(Enumerable.Range(0, 40000).Select(i => $"{i:000000} Hola, Ángela 🚀 — JSON {{\"value\":{i}}}\r\n"));
        File.WriteAllText(file, content, Utf8);
        using (var reader = new LogFileReader(file))
        {
            var result = new StringBuilder();
            long offset = 0;
            while (offset < reader.Length)
            {
                var page = reader.After(offset);
                Require(page.End > offset && page.End - page.Start <= LogFileReader.PageBytes, "Forward paging stalled or exceeded its bound.");
                result.Append(page.Text); offset = page.End;
            }
            Require(result.ToString() == content, "Forward UTF-8 pages lost, duplicated or corrupted text.");
            result.Clear(); offset = reader.Length;
            while (offset > 0)
            {
                var page = reader.Before(offset);
                Require(page.Start < offset && page.End - page.Start <= LogFileReader.PageBytes, "Backward paging stalled or exceeded its bound.");
                result.Insert(0, page.Text); offset = page.Start;
            }
            Require(result.ToString() == content, "Backward UTF-8 pages lost, duplicated or corrupted text.");
        }
        Pass("Forward and backward pages preserve complete UTF-8 content without gaps");

        string needle = "RC-TEST-001 Ángela 🚀";
        string prefix = new('x', LogFileReader.SearchBytes - 3);
        content = prefix + needle + new string('y', LogFileReader.SearchBytes + 19) + needle;
        File.WriteAllText(file, content, Utf8);
        using (var reader = new LogFileReader(file))
        {
            long first = Utf8.GetByteCount(prefix), last = Utf8.GetByteCount(content[..content.LastIndexOf(needle, StringComparison.Ordinal)]);
            Require(reader.Find(needle.ToLowerInvariant(), 0, false, default) == first, "Forward boundary match missing.");
            Require(reader.Find(needle, first + 1, false, default) == last, "Find next repeated its prior result.");
            Require(reader.Find(needle, reader.Length, true, default) == last, "Backward EOF match missing.");
            Require(reader.Find(needle, last, true, default) == first, "Find previous repeated/skipped a boundary result.");
            var page = reader.Containing(last);
            Require(page.Text.Substring(page.CharacterAt(last), needle.Length) == needle, "Search selection lost byte/character correspondence.");
            Require(reader.Find("ABSENT", 0, false, default) is null, "A missing match was invented.");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { reader.Find("ABSENT", 0, false, cancellation.Token); throw new InvalidOperationException("Search ignored cancellation."); }
            catch (OperationCanceledException) { }
        }
        Pass("Case-insensitive directional search crosses chunks, locates Unicode and cancels");

        File.WriteAllText(file, "aaaaaa", Utf8);
        using (var reader = new LogFileReader(file))
            Require(reader.Find("aaaa", 2, true, default) == 1, "Backward search skipped an overlapping occurrence.");
        var incomplete = Utf8.GetBytes("complete 🚀");
        File.WriteAllBytes(file, incomplete[..^2]);
        using (var reader = new LogFileReader(file))
        {
            Require(reader.Before(reader.Length).Text == "complete ", "An incomplete live UTF-8 character was displayed as corruption.");
            using (var append = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) append.Write(incomplete.AsSpan(incomplete.Length - 2));
            Require(reader.Before(reader.Length).Text == "complete 🚀", "A completed live UTF-8 character was lost.");
        }
        File.WriteAllText(file, "", Utf8);
        using (var reader = new LogFileReader(file))
            Require(reader.After(0).Text == "" && reader.Before(0).Text == "" && reader.Find("anything", 0, false, default) is null, "Empty file handling failed.");
        Pass("Overlapping matches, incomplete live characters and empty files are handled");

        content = string.Concat(Enumerable.Repeat("á🚀", LogFileReader.PageBytes)) + "end";
        File.WriteAllText(file, content, Utf8);
        using (var reader = new LogFileReader(file))
        {
            long offset = 0; var result = new StringBuilder();
            while (offset < reader.Length)
            {
                var page = reader.After(offset);
                Require(page.End > offset && page.Text.Length <= LogFileReader.PageBytes, "A giant line stalled/unbounded the reader.");
                result.Append(page.Text); offset = page.End;
            }
            Require(result.ToString() == content, "Giant line fragments corrupt UTF-8.");
            result.Clear(); offset = reader.Length;
            while (offset > 0)
            {
                var fragment = reader.Before(offset);
                Require(fragment.Start < offset && fragment.End - fragment.Start <= LogFileReader.PageBytes, "Backward giant-line paging stalled or exceeded its bound.");
                result.Insert(0, fragment.Text); offset = fragment.Start;
            }
            Require(result.ToString() == content, "Backward giant-line fragments corrupt UTF-8.");
            var snapshot = reader.Before(reader.Length);
            string archive = Path.Combine(directory, "reader.1.log");
            File.Move(file, archive);
            File.WriteAllText(file, "NEW FILE\r\n", Utf8);
            using var next = new LogFileReader(file);
            Require(next.Identity != reader.Identity, "Rotation reused the file identity.");
            Require(reader.Before(reader.Length).Text == snapshot.Text, "An open historical handle switched to the new file.");
            File.Delete(archive);
            Require(reader.Before(reader.Length).Text == snapshot.Text, "Retention deletion changed the opened historical file.");
        }
        Pass("Giant lines stay bounded and rotation preserves the opened historical file");

        string viewer = Path.Combine(directory, "viewer.log");
        using var writer = new StreamWriter(viewer, false, Utf8);
        writer.WriteLine("RC-TEST-001: synthetic fixture, not a production message.");
        string row = new string('x', 200) + "\r\n";
        for (int i = 0; i < 40000; i++) writer.Write(row);
    }

    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;

    private sealed class ProgressProbe(Action<LogSearchProgress> report) : IProgress<LogSearchProgress>
    { public void Report(LogSearchProgress value) => report(value); }

    private static void HistoryChecks(string directory)
    {
        string folder = Path.Combine(directory, "history");
        Directory.CreateDirectory(folder);
        string current = Path.Combine(folder, "history.log");
        string Archive(int number) => Path.Combine(folder, $"history.{number}.log");
        File.WriteAllText(current, "CURRENT WITHOUT THE SEARCH TERM\r\n", Utf8);
        File.WriteAllText(Archive(1), "first ÁNGELA 🚀\r\nlast Ángela 🚀\r\n", Utf8);
        File.WriteAllText(Archive(10), "middle Ángela 🚀\r\n", Utf8);
        File.WriteAllText(Archive(40), "oldest Ángela 🚀\r\n", Utf8);
        foreach (bool backwards in new[] { true, false })
        {
            LogFileReader? anchor = null;
            long? from = null;
            try
            {
                int[] expected = backwards ? [1, 1, 10, 40] : [40, 10, 1, 1];
                foreach (int number in expected)
                {
                    var result = LogHistorySearch.Find(current, "ángela 🚀", backwards, anchor, from, default);
                    Require(result.Unavailable == 0 && result.Reader?.Path == Archive(number) && result.Offset is not null,
                        "Parallel search lost chronology or failed to cross file boundaries.");
                    anchor?.Dispose(); anchor = result.Reader;
                    from = result.Offset + (backwards ? 0 : 1);
                }
                var exhausted = LogHistorySearch.Find(current, "ángela 🚀", backwards, anchor, from, default);
                Require(exhausted.Reader is null && exhausted.Unavailable == 0, "Search wrapped around or invented an extra match.");
            }
            finally { anchor?.Dispose(); }
        }
        Pass("All-file search finds Unicode in distant archives and traverses matches in both chronological directions");

        using (var locked = new FileStream(Archive(10), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = LogHistorySearch.Find(current, "ABSENT", true, null, null, default);
            Require(result.Reader is null && result.Unavailable == 1 && result.SkippedFiles.Single() == "history.10.log",
                "Unreadable archive was not named or was reported as a complete no-match search.");
        }
        bool rotated = false;
        var rotation = new ProgressProbe(_ =>
        {
            if (rotated) return;
            rotated = true;
            File.Move(Archive(1), Archive(2));
            File.WriteAllText(Archive(1), "REPLACEMENT WITHOUT THE SEARCH TERM\r\n", Utf8);
        });
        var stable = LogHistorySearch.Find(current, "first Ángela 🚀", true, null, null, default, rotation);
        using (var opened = stable.Reader)
            Require(opened is not null && stable.Offset is long hit && opened.Containing(hit).Text.Contains("first ÁNGELA", StringComparison.Ordinal),
                "Rotation swapped a captured file during parallel search.");
        Pass("Unreadable files produce an incomplete result; captured file identities survive rotation");

        File.WriteAllText(Archive(3), "ORPHAN-RECORD\r\n", Utf8);
        using (var orphan = new LogFileReader(Archive(3)))
        {
            File.Delete(Archive(3));
            var found = LogHistorySearch.Find(current, "ORPHAN-RECORD", true, orphan, null, default);
            Require(ReferenceEquals(found.Reader, orphan) && found.Offset == 0, "A new all-file query omitted the still-open expired archive.");
            // A fresh query while an old file is displayed still searches newer
            // files as well. The anchor restricts direction only after a match.
            found = LogHistorySearch.Find(current, "CURRENT WITHOUT", true, orphan, null, default);
            using var currentResult = found.Reader;
            Require(currentResult?.Path == current && found.Offset == 0, "A new query was restricted by the displayed archive.");
        }
        Pass("New queries include a still-open expired archive and search the entire collection independently of the displayed file");

        string block = new('z', 1024 * 1024);
        foreach (int number in new[] { 1, 2, 10, 40 })
        {
            using var writer = new StreamWriter(Archive(number), false, Utf8);
            for (int i = 0; i < 8; i++) writer.Write(block);
        }
        LogSearchProgress? finalProgress = null;
        var scan = LogHistorySearch.Find(current, "ABSENT", true, null, null, default, new ProgressProbe(p => finalProgress = p));
        Require(scan.Reader is null && scan.Unavailable == 0 && scan.PeakWorkers is >= 2 and <= LogHistorySearch.MaxWorkers,
            "History search did not use bounded parallel workers.");
        Require(finalProgress?.Completed == 5 && finalProgress.Total == 5 && finalProgress.BytesRead >= 32L * 1024 * 1024,
            "Progress failed to account for all files and scanned bytes.");
        Console.WriteLine($"Parallel history scan: {scan.PeakWorkers} concurrent workers; {finalProgress!.BytesRead} bytes; {finalProgress.Completed}/{finalProgress.Total} files.");
        using var cancel = new CancellationTokenSource();
        try
        {
            LogHistorySearch.Find(current, "ABSENT", true, null, null, cancel.Token,
                new ProgressProbe(p => { if (p.BytesRead > 0) cancel.Cancel(); }));
            throw new InvalidOperationException("In-progress parallel search ignored cancellation.");
        }
        catch (OperationCanceledException) { }
        foreach (string file in LogHistorySearch.Paths(current).Where(File.Exists))
        {
            using var exclusive = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        Pass("Parallel scan uses at most four workers, reports progress, cancels mid-scan and releases every search handle");
    }
    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static Button Button(Form form, string label) => Descendants(form).OfType<Button>().Single(b => b.Text == label);
    private static async Task Ready(Form form)
    {
        var watch = Stopwatch.StartNew();
        while (Field<bool>(form, "busy") && watch.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(25);
        Require(!Field<bool>(form, "busy"), "Disk operation did not finish.");
        await Task.Delay(200);
    }

    private static async Task ViewerChecks(LogViewerForm form, string directory)
    {
        await Ready(form);
        var text = Descendants(form).OfType<TextBox>().Single(t => t.AccessibleName == "Tlachihuani.log contents");
        Require(text.TextLength > 0 && text.TextLength <= LogFileReader.PageBytes, "Initial live page is empty or unbounded.");
        Require(text.SelectionStart == text.TextLength, "Live view did not start at the end.");
        Require(Field<bool>(form, "following"), "Programmatic scrolling unexpectedly paused live mode.");
        foreach (var button in Descendants(form).OfType<Button>().Where(b => b.Visible))
            Require(button.Parent!.ClientRectangle.Contains(button.Bounds), $"Button is clipped: {button.Text}");
        SendMessage(text.Handle, 0x0115 /* WM_VSCROLL */, (IntPtr)2 /* SB_PAGEUP */, IntPtr.Zero);
        await Ready(form);
        Require(!Field<bool>(form, "following"), "Scrolling while the caret is at EOF did not pause.");
        string snapshot = text.Text;
        text.Select(50, 20);
        int firstLine = (int)SendMessage(text.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero);
        long before = GC.GetTotalMemory(true);
        await Task.Run(() =>
        {
            var chunk = Utf8.GetBytes(new string('n', 8190) + "\r\n");
            using var stream = new FileStream(Path.Combine(directory, "viewer.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            for (int i = 0; i < 8192; i++) stream.Write(chunk);
        });
        await Task.Delay(1800); // More than two live timer ticks while 64 MB is appended.
        Require(text.Text == snapshot && text.SelectionStart == 50 && text.SelectionLength == 20, "Paused content/selection moved under writes.");
        Require((int)SendMessage(text.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero) == firstLine, "Paused viewport moved.");
        long memoryChange = GC.GetTotalMemory(true) - before;
        Console.WriteLine($"Paused reader: appended 67108864 bytes; retained managed-memory change {memoryChange} bytes; displayed {text.TextLength} characters.");
        Require(memoryChange < 8 * 1024 * 1024, "Paused viewer retained a growing backlog.");
        Capture(form, Path.Combine(directory, "paused.png"));
        Pass("Native scroll pauses even with caret at EOF; 64 MB of writes leave page, selection, viewport and memory bounded");

        Button(form, "Older").PerformClick(); await Ready(form);
        Require(Field<LogPage>(form, "page").End <= Field<LogPage>(form, "page").Start + LogFileReader.PageBytes, "Older page exceeded bound.");
        Button(form, "Beginning").PerformClick(); await Ready(form);
        Require(text.Text.StartsWith("RC-TEST-001:", StringComparison.Ordinal), "Beginning did not read history beyond the initial window.");
        var search = Descendants(form).OfType<TextBox>().Single(t => t.AccessibleName == "Find in logs");
        search.Text = "rc-test-001";
        Button(form, "Find newer").PerformClick(); await Ready(form);
        Require(text.SelectedText == "RC-TEST-001", "The UI did not highlight the historical match.");
        Capture(form, Path.Combine(directory, "historical-match.png"));
        Pass("Historical navigation and search reach text outside the initial page");

        string current = Path.Combine(directory, "viewer.log");
        string old = Path.Combine(directory, "viewer.1.log");
        File.Move(current, old);
        File.WriteAllText(current, "NEW CURRENT LOG\r\n", Utf8);
        snapshot = text.Text;
        await Task.Delay(1000);
        Require(text.Text == snapshot, "Rotation replaced the paused reader's page.");
        Button(form, "Follow live").PerformClick(); await Ready(form);
        Require(text.Text == "NEW CURRENT LOG\r\n" && Field<bool>(form, "following"), "Follow live did not switch to the current file.");
        File.AppendAllText(current, "NEW LINE\r\n", Utf8);
        await Task.Delay(1100); await Ready(form);
        Require(text.Text.EndsWith("NEW LINE\r\n", StringComparison.Ordinal), "Live updates did not resume.");
        Pass("Rotation leaves paused history intact; explicit Follow live switches and resumes updates");

        var scope = Descendants(form).OfType<CheckBox>().Single(c => c.Text == "Search all log files");
        Require(scope.Checked && scope.Visible, "All-file search is not available and enabled by default.");
        Button(form, "Find older").PerformClick(); await Ready(form);
        Require(text.SelectedText == "RC-TEST-001" && Field<LogFileReader>(form, "reader").Path == old,
            "All-file GUI search failed to open and highlight a match in the archive.");
        Require(Descendants(form).OfType<ComboBox>().Single().SelectedItem?.ToString() == old,
            "The file selector did not identify the matching archive.");
        Capture(form, Path.Combine(directory, "archive-match.png"));
        scope.Checked = false;
        Button(form, "Follow live").PerformClick(); await Ready(form);
        Button(form, "Find older").PerformClick(); await Ready(form);
        Require(Field<LogFileReader>(form, "reader").Path == current && Field<Label>(form, "state").Text.Contains("in this file", StringComparison.Ordinal),
            "Selected-file scope unexpectedly searched archives.");
        scope.Checked = true;
        search.Text = "NOT PRESENT IN ANY LOG";
        snapshot = text.Text;
        Button(form, "Find older").PerformClick();
        Require(Button(form, "Cancel search").Enabled, "Cancel search was not enabled during all-file work.");
        Button(form, "Cancel search").PerformClick(); await Ready(form);
        Require(text.Text == snapshot && Field<Label>(form, "state").Text.Contains("cancelled", StringComparison.Ordinal),
            "Cancelling from the GUI changed the page or hid the cancellation result.");
        Pass("Visible all-file option finds archived text, identifies its file, respects selected-file scope and cancels without moving the page");
    }

    private static void Capture(Form form, string path)
    {
        Require(form.Visible && GetForegroundWindow() == form.Handle && !IsIconic(form.Handle), "The verification window lost foreground visibility.");
        using var bitmap = new Bitmap(form.Width, form.Height);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(form.Location, Point.Empty, form.Size);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
