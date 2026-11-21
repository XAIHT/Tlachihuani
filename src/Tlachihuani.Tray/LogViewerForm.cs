// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (its four-per-second tail would otherwise trace itself into the file it shows)
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Tlachihuani.Tray;

// Services → See log. Opens with the newest 2 MB of Tlachihuani.log and then follows the
// file by itself four times a second: only the new bytes are read and appended. While the
// caret is at the end it scrolls with the new lines; click anywhere to stay where you are
// (new lines keep arriving below), Ctrl+End to follow again. A rotated file is finished
// from Tlachihuani.1.log, then the new file is followed. There is no Refresh button.
internal sealed class LogViewerForm : StudioForm
{
    private const int InitialBytes = 2 * 1024 * 1024;
    private const int StepBytes = 4 * 1024 * 1024;
    private const int MaxChars = 6_000_000;
    private const int KeepChars = 3_000_000;
    private const int WM_SETREDRAW = 0x000B, EM_GETFIRSTVISIBLELINE = 0x00CE, EM_LINESCROLL = 0x00B6;

    private readonly Font mono = new(MonospaceFont(), 9.5f);
    private readonly TextBox text = new()
    {
        Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both, MaxLength = 0,
        BorderStyle = BorderStyle.None, BackColor = Studio.Pane, ForeColor = Studio.Body,
        Dock = DockStyle.Fill, Margin = Padding.Empty,
        AccessibleName = "Tlachihuani.log contents", HideSelection = false
    };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly string path = TlachihuaniLog.FilePath;
    private long position;
    private long created;
    private long skipped;
    private bool started;

    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    internal LogViewerForm(Icon? icon) : base(icon, "Tlachihuani · Log", "Tlachihuani   /   Diagnostics", "Tlachihuani.log", new(920, 600), new(640, 420))
    {
        text.Font = mono;
        // The shown text and the footer change four times a second; tracing them would feed the log itself.
        UiTrace.Ignore(text);
        UiTrace.Ignore(FooterNote);
        BodyArea.Controls.Add(Studio.Card(text, new Padding(10, 8, 4, 4)));
        var close = Command("Close", Close);
        Commands.Controls.Add(close);
        Commands.Controls.Add(Command("Copy all", CopyAll));
        Commands.Controls.Add(Command("Open folder", OpenFolder));
        CancelButton = close;
        timer.Tick += (_, _) => Tail();
        Shown += (_, _) => { OpenTail(); timer.Start(); };
        FormClosed += (_, _) => timer.Stop();
    }

    private static string MonospaceFont()
    {
        using var sample = new Font("Cascadia Mono", 9);
        return sample.Name == "Cascadia Mono" ? "Cascadia Mono" : "Consolas";
    }

    private bool Following => text.SelectionLength == 0 && text.SelectionStart >= text.TextLength;

    private void OpenTail()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) { text.Text = "Tlachihuani.log has no entries yet. New lines appear here as they are written."; started = false; return; }
            created = info.CreationTimeUtc.Ticks;
            position = Math.Max(0, info.Length - InitialBytes);
            var first = Read(path, ref position, InitialBytes, dropPartialFirstLine: position > 0);
            text.Text = first;
            text.SelectionStart = text.TextLength;
            text.ScrollToCaret();
            started = true;
            Footer(info.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FooterNote.Text = "Tlachihuani.log could not be read: " + ex.Message;
        }
    }

    private void Tail()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return;
            if (!started) { text.Clear(); OpenTail(); return; }
            var added = new StringBuilder();
            if (info.CreationTimeUtc.Ticks != created || info.Length < position)
            {
                // Rotated: the rest of the old file is now Tlachihuani.1.log.
                var previous = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".1.log");
                if (File.Exists(previous) && new FileInfo(previous).CreationTimeUtc.Ticks == created)
                    added.Append(Read(previous, ref position, StepBytes, false));
                added.Append("──────── Tlachihuani.log rotated: the previous part is Tlachihuani.1.log ────────\r\n");
                created = info.CreationTimeUtc.Ticks;
                position = 0;
            }
            long waiting = info.Length - position;
            if (waiting > 2L * StepBytes)
            {
                long jump = waiting - StepBytes;
                skipped += jump;
                position += jump;
                added.Append($"──────── {jump / 1048576.0:0.0} MB written faster than this window can show; they are in the file ────────\r\n");
                added.Append(Read(path, ref position, StepBytes, true));
            }
            else if (waiting > 0) added.Append(Read(path, ref position, StepBytes, false));
            if (added.Length > 0) Append(added.ToString());
            Footer(info.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FooterNote.Text = "Tlachihuani.log could not be read: " + ex.Message;
        }
    }

    // Reads whole lines from offset (at most limit bytes) and moves offset past them.
    // Reused for every read (no new multi-megabyte array four times a second).
    private readonly byte[] readBuffer = new byte[StepBytes];

    private string Read(string file, ref long offset, int limit, bool dropPartialFirstLine)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long length = stream.Length;
        if (length <= offset) return "";
        stream.Seek(offset, SeekOrigin.Begin);
        int want = (int)Math.Min(limit, length - offset);
        var buffer = readBuffer;
        int read = 0;
        while (read < want) { int n = stream.Read(buffer, read, want - read); if (n == 0) break; read += n; }
        int end = read == 0 ? -1 : Array.LastIndexOf(buffer, (byte)'\n', read - 1);
        if (end < 0) return "";
        int begin = 0;
        if (dropPartialFirstLine) begin = Array.IndexOf(buffer, (byte)'\n', 0, end + 1) + 1;
        offset += end + 1;
        var chunk = Encoding.UTF8.GetString(buffer, begin, end + 1 - begin);
        return chunk.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    private void Append(string chunk)
    {
        if (Following)
        {
            if (text.TextLength + chunk.Length > MaxChars)
            {
                var all = text.Text + chunk;
                int cut = all.IndexOf('\n', all.Length - KeepChars);
                text.Text = cut >= 0 ? all[(cut + 1)..] : all[^KeepChars..];
                text.SelectionStart = text.TextLength;
                text.ScrollToCaret();
            }
            else text.AppendText(chunk);
            return;
        }
        // Not following: add the lines below without moving what the reader is looking at.
        int start = text.SelectionStart, length = text.SelectionLength;
        int top = (int)SendMessage(text.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
        SendMessage(text.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try
        {
            text.AppendText(chunk);
            text.Select(start, length);
            int now = (int)SendMessage(text.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
            SendMessage(text.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)(top - now));
        }
        finally
        {
            SendMessage(text.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            text.Invalidate();
        }
    }

    private void Footer(long size)
    {
        var note = $"{path}   ·   {Bytes(size)}   ·   {(Following ? "following new lines" : "staying here — Ctrl+End follows again")}"
            + (skipped > 0 ? $"   ·   {Bytes(skipped)} not shown (in the file)" : "");
        if (FooterNote.Text != note) FooterNote.Text = note;
    }

    private static string Bytes(long bytes) => bytes < 1024 ? $"{bytes} bytes" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.0} KB"
        : bytes < 1024L * 1024 * 1024 ? $"{bytes / 1048576.0:0.0} MB" : $"{bytes / 1073741824.0:0.00} GB";

    private void CopyAll()
    {
        try { if (text.TextLength > 0) Clipboard.SetText(text.Text); FooterNote.Text = "Copied the shown log."; }
        catch (ExternalException) { FooterNote.Text = "Clipboard busy"; }
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(TlachihuaniLog.Folder);
            var start = File.Exists(path)
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                : new ProcessStartInfo("explorer.exe", $"\"{TlachihuaniLog.Folder}\"");
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            FooterNote.Text = "Windows could not open the log folder.";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
        if (disposing) mono.Dispose();
    }
}
