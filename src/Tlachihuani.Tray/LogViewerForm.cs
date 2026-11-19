// Tlachihuani — Angela López Mendoza · XAIHT
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Tlachihuani.Tray;

// Services → See log. Shows the newest 2 MB of Tlachihuani.log and follows new
// lines while the caret stays at the end, the way Tlamatini shows its own log.
internal sealed class LogViewerForm : StudioForm
{
    private const int TailBytes = 2 * 1024 * 1024;
    private readonly Font mono = new(MonospaceFont(), 9.5f);
    private readonly RichTextBox text = new()
    {
        ReadOnly = true, DetectUrls = false, WordWrap = false, ScrollBars = RichTextBoxScrollBars.Both,
        BorderStyle = BorderStyle.None, BackColor = Studio.Pane, ForeColor = Studio.Body,
        Dock = DockStyle.Fill, Margin = Padding.Empty,
        AccessibleName = "Tlachihuani.log contents", HideSelection = false
    };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1500 };
    private long seenSize = -1;
    private DateTime seenWrite;

    internal LogViewerForm(Icon? icon) : base(icon, "Tlachihuani · Log", "Tlachihuani   /   Diagnostics", "Tlachihuani.log", new(920, 600), new(640, 420))
    {
        text.Font = mono;
        BodyArea.Controls.Add(Studio.Card(text, new Padding(10, 8, 4, 4)));
        var close = Command("Close", Close);
        Commands.Controls.Add(close);
        Commands.Controls.Add(Command("Refresh", () => Reload(force: true)));
        Commands.Controls.Add(Command("Copy all", CopyAll));
        Commands.Controls.Add(Command("Open folder", OpenFolder));
        CancelButton = close;
        timer.Tick += (_, _) => Reload(force: false);
        Shown += (_, _) => { Reload(force: true); timer.Start(); };
        FormClosed += (_, _) => timer.Stop();
    }

    private static string MonospaceFont()
    {
        using var sample = new Font("Cascadia Mono", 9);
        return sample.Name == "Cascadia Mono" ? "Cascadia Mono" : "Consolas";
    }

    private void Reload(bool force)
    {
        try
        {
            var file = new FileInfo(TlachihuaniLog.FilePath);
            long size = file.Exists ? file.Length : 0;
            var written = file.Exists ? file.LastWriteTimeUtc : DateTime.MinValue;
            if (!force && size == seenSize && written == seenWrite) return;
            bool follow = seenSize < 0 || text.SelectionStart >= text.TextLength;
            int keep = text.SelectionStart;
            var (content, total, truncated) = TlachihuaniLog.ReadTail(TailBytes);
            text.Text = content.Length == 0 ? "Tlachihuani.log has no entries yet." : content;
            seenSize = size; seenWrite = written;
            if (follow) { text.SelectionStart = text.TextLength; text.ScrollToCaret(); }
            else { text.SelectionStart = Math.Min(keep, text.TextLength); text.ScrollToCaret(); }
            FooterNote.Text = $"{TlachihuaniLog.FilePath}   ·   {Bytes(total)}{(truncated ? "   ·   newest 2 MB shown" : "")}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FooterNote.Text = "Tlachihuani.log could not be read: " + ex.Message;
        }
    }

    private static string Bytes(long bytes) => bytes < 1024 ? $"{bytes} bytes" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.0} KB" : $"{bytes / 1048576.0:0.0} MB";

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
            var start = File.Exists(TlachihuaniLog.FilePath)
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{TlachihuaniLog.FilePath}\"")
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
