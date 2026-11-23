// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (the log viewer must not log its own polling and navigation)
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Tlachihuani.Tray;

// A bounded view of a file. Pausing freezes the page, selection and viewport, not
// the producer. New bytes stay on disk; there is no paused-reader append queue.
internal sealed class LogViewerForm : StudioForm
{
    private readonly Font mono = new(MonospaceFont(), 9.5f);
    private readonly LogTextBox text = new()
    {
        Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both, MaxLength = 0,
        BorderStyle = BorderStyle.None, BackColor = Studio.Pane, ForeColor = Studio.Body,
        Dock = DockStyle.Fill, Margin = Padding.Empty,
        AccessibleName = "Tlachihuani.log contents", HideSelection = false
    };
    private readonly ComboBox files = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Log file", BackColor = Studio.Pane, ForeColor = Studio.Text };
    private readonly TextBox query = new() { MaxLength = LogFileReader.MaxQueryChars, PlaceholderText = "Find in all log files (ignores case)", AccessibleName = "Find in logs" };
    private readonly CheckBox allFiles = new() { Text = "Search all log files", AccessibleName = "Search all log files", Checked = true, AutoSize = true, Dock = DockStyle.Fill, ForeColor = Studio.Text, Margin = Padding.Empty };
    private readonly ProgressBar searchProgress = new() { Dock = DockStyle.Fill, AccessibleName = "Log search progress", Visible = false, Margin = new Padding(0, 7, 0, 7) };
    private readonly Label state = Studio.Label("", Studio.Small, Studio.Muted);
    private readonly ToolTip detail = new() { AutoPopDelay = 20000 };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 750 };
    private readonly StudioButton follow, older, newer, oldest, findOlder, findNewer, cancelSearch;
    private readonly string path;
    private LogFileReader? reader;
    private LogPage? page;
    private CancellationTokenSource? operation;
    private bool following = true, busy, closing;
    private int generation, searchSequence;
    private long observedLength = -1;
    private long? match;
    private string lastQuery = "";

    internal LogViewerForm(Icon? icon, string? logPath = null) : base(icon, "Tlachihuani · Log", "Tlachihuani   /   Diagnostics", "Tlachihuani.log", new(1080, 680), new(900, 480))
    {
        path = logPath ?? TlachihuaniLog.FilePath;
        text.Font = mono;
        text.Browse += Pause;
        follow = Command("Pause live", ToggleFollow, ButtonTone.Primary);
        HeaderCommands.Controls.Add(follow);
        older = Command("Older", () => Navigate(-1));
        newer = Command("Newer", () => Navigate(1));
        oldest = Command("Beginning", () => Navigate(0));
        findOlder = Command("Find older", () => Find(true));
        findNewer = Command("Find newer", () => Find(false));
        cancelSearch = Command("Cancel search", () => operation?.Cancel());
        var layout = Studio.Grid([-1], [38, 30, 42, 28, -1]);
        var fileRow = Studio.Grid([-1, 310], [-1]);
        fileRow.Controls.Add(files, 0, 0);
        fileRow.Controls.Add(Studio.Actions(oldest, older, newer), 1, 0);
        var scopeRow = Studio.Grid([-1, 220], [-1]);
        scopeRow.Controls.Add(allFiles, 0, 0);
        scopeRow.Controls.Add(searchProgress, 1, 0);
        var searchRow = Studio.Grid([-1, 350], [-1]);
        searchRow.Controls.Add(Studio.Field(query, "Find in logs"), 0, 0);
        searchRow.Controls.Add(Studio.Actions(findOlder, findNewer, cancelSearch), 1, 0);
        layout.Controls.Add(fileRow, 0, 0);
        layout.Controls.Add(scopeRow, 0, 1);
        layout.Controls.Add(searchRow, 0, 2);
        layout.Controls.Add(state, 0, 3);
        layout.Controls.Add(Studio.Card(text, new Padding(10, 8, 4, 4)), 0, 4);
        BodyArea.Controls.Add(layout);
        var close = Command("Close", Close);
        Commands.Controls.Add(close);
        Commands.Controls.Add(Command("Copy page", CopyPage));
        Commands.Controls.Add(Command("Open folder", OpenFolder));
        Commands.AutoSize = true;
        Commands.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        if (FooterNote.Parent is TableLayoutPanel footer)
        {
            footer.ColumnStyles[0].Width = 100;
            footer.ColumnStyles[1].SizeType = SizeType.AutoSize;
        }
        CancelButton = close;
        // Even EnabledChanged on a navigation button would write to the same log
        // on every live tick. Observing the log must not generate more log traffic.
        UiTrace.Ignore(this);
        state.TextChanged += (_, _) => detail.SetToolTip(state, state.Text);
        files.FormattingEnabled = true;
        files.Format += (_, e) => { if (e.ListItem is string item) e.Value = Path.GetFileName(item); };
        files.DropDown += (_, _) => RefreshFiles();
        files.SelectionChangeCommitted += (_, _) => OpenSelected();
        query.Enter += (_, _) => Pause();
        query.TextChanged += (_, _) => { match = null; lastQuery = ""; UpdateControls(); };
        allFiles.CheckedChanged += (_, _) =>
        {
            Pause(); match = null; lastQuery = "";
            query.PlaceholderText = allFiles.Checked ? "Find in all log files (ignores case)" : "Find in this file (ignores case)";
            Status(allFiles.Checked ? "Search covers the current log and every retained numbered log." : "Search covers only the selected log file.");
        };
        timer.Tick += (_, _) => { if (following && !busy) ReadLive(); };
        Shown += (_, _) => { RefreshFiles(); ReadLive(); timer.Start(); };
        UpdateControls();
    }

    private static string MonospaceFont()
    {
        using var sample = new Font("Cascadia Mono", 9);
        return sample.Name == "Cascadia Mono" ? "Cascadia Mono" : "Consolas";
    }

    private void Pause()
    {
        if (!following) return;
        following = false;
        generation++;
        UpdateControls();
        Status();
    }

    private void ToggleFollow()
    {
        if (following) { Pause(); return; }
        if (busy) return;
        following = true;
        generation++;
        match = null;
        observedLength = -1;
        files.SelectedItem = path;
        ReadLive();
    }

    private sealed record View(LogFileReader Reader, LogPage Page, long Size, long? Match = null, string? Notice = null);

    private void ReadLive() => Run(cancellation =>
    {
        cancellation.ThrowIfCancellationRequested();
        var current = new LogFileReader(path);
        try
        {
            long size = current.Length;
            if (reader?.Identity == current.Identity && observedLength == size) { current.Dispose(); return null; }
            return new(current, current.Before(size), size);
        }
        catch { current.Dispose(); throw; }
    });

    private void RefreshFiles()
    {
        var selected = files.SelectedItem as string;
        files.Items.Clear();
        files.Items.Add(path);
        foreach (var archived in LogHistorySearch.Paths(path).Skip(1))
            if (File.Exists(archived)) files.Items.Add(archived);
        files.SelectedItem = selected;
        if (files.SelectedIndex < 0) files.SelectedIndex = 0;
    }

    private void OpenSelected()
    {
        if (busy || files.SelectedItem is not string selected) return;
        Pause();
        Run(cancellation =>
        {
            cancellation.ThrowIfCancellationRequested();
            var chosen = new LogFileReader(selected);
            try { long size = chosen.Length; return new(chosen, chosen.Before(size), size); }
            catch { chosen.Dispose(); throw; }
        });
    }

    private void Navigate(int direction)
    {
        if (busy || reader is null || page is null) return;
        Pause();
        var current = reader;
        var shown = page;
        Run(cancellation =>
        {
            cancellation.ThrowIfCancellationRequested();
            long size = current.Length;
            if (size < shown.End) throw new IOException("This file was shortened. Choose the file again or Follow live.");
            var next = direction < 0 ? current.Before(shown.Start) : current.After(direction == 0 ? 0 : shown.End);
            return next.End <= next.Start ? new(current, shown, size, Notice: "No newer text in this opened file.") : new(current, next, size);
        }, scrollToEnd: direction < 0);
    }

    private void Find(bool backwards)
    {
        if (busy || reader is null || page is null || query.Text.Length == 0) return;
        Pause();
        var current = reader;
        var shown = page;
        string search = query.Text;
        bool acrossFiles = allFiles.Checked;
        bool continuing = match.HasValue && lastQuery == search;
        long from = continuing ? match!.Value + (backwards ? 0 : 1)
            : backwards ? shown.End : shown.Start;
        lastQuery = search;
        int sequence = ++searchSequence;
        var progress = new Progress<LogSearchProgress>(p =>
        {
            if (closing || !busy || sequence != searchSequence) return;
            searchProgress.Maximum = Math.Max(1, p.Total);
            searchProgress.Value = Math.Min(p.Completed, searchProgress.Maximum);
            state.Text = $"Searching all logs · {p.Completed}/{p.Total} files · {Bytes(p.BytesRead)} scanned · Cancel search to stop.";
        });
        Run(cancellation =>
        {
            if (acrossFiles)
            {
                var result = LogHistorySearch.Find(path, search, backwards, current,
                    continuing ? from : null, cancellation, progress);
                string caution = result.Unavailable > 0 ? $" · Search incomplete: changed/unavailable files: {string.Join(", ", result.SkippedFiles)}" : "";
                if (result.Reader is { } foundReader && result.Offset is long hit)
                {
                    try { return new(foundReader, foundReader.Containing(hit), foundReader.Length, hit,
                        $"Match in {Path.GetFileName(foundReader.Path)} · {result.Searched} files searched{caution}"); }
                    catch { if (!ReferenceEquals(foundReader, current)) foundReader.Dispose(); throw; }
                }
                string direction = continuing ? (backwards ? "older " : "newer ") : "";
                return new(current, shown, current.Length, match, $"No {direction}match in {result.Searched} searched log files{caution}");
            }
            var found = current.Find(search, from, backwards, cancellation);
            return found is long offset ? new(current, current.Containing(offset), current.Length, offset)
                : new(current, shown, current.Length, match, "No " + (backwards ? "older" : "newer") + " match in this file. Enable Search all log files to include archives.");
        }, searching: true);
    }

    // One disk operation and one displayed page. Navigation does not queue work;
    // closing cannot apply stale results or dispose a handle used by a worker.
    private async void Run(Func<CancellationToken, View?> read, bool scrollToEnd = false, bool searching = false)
    {
        if (busy || closing) return;
        busy = true;
        int version = generation;
        using var cancel = new CancellationTokenSource();
        operation = cancel;
        UpdateControls();
        cancelSearch.Enabled = searching;
        searchProgress.Visible = searching && allFiles.Checked;
        searchProgress.Value = 0;
        if (searching) state.Text = allFiles.Checked ? "Opening retained logs for parallel search…" : "Searching this file… You can cancel; the displayed page stays available.";
        try
        {
            var result = await Task.Run(() => read(cancel.Token), cancel.Token);
            if (result is null) return;
            if (closing || version != generation || cancel.IsCancellationRequested)
            {
                if (!ReferenceEquals(result.Reader, reader)) result.Reader.Dispose();
                return;
            }
            if (!ReferenceEquals(reader, result.Reader))
            {
                reader?.Dispose(); reader = result.Reader;
                RefreshFiles();
                if (!files.Items.Contains(reader.Path)) files.Items.Add(reader.Path);
                files.SelectedItem = reader.Path;
            }
            observedLength = result.Size;
            match = result.Match;
            bool samePage = ReferenceEquals(page, result.Page);
            page = result.Page;
            if (!samePage)
            {
                text.Text = page.Text;
                if (match is long hit)
                {
                    text.Select(page.CharacterAt(hit), query.Text.Length);
                    text.ScrollToCaret();
                }
                else
                {
                    text.Select(following || scrollToEnd ? text.TextLength : 0, 0);
                    text.ScrollToCaret();
                }
            }
            Status(result.Notice);
        }
        catch (OperationCanceledException) { if (!closing) Status("Search cancelled. Your page is unchanged."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!closing) state.Text = "The log could not be read: " + ex.Message;
        }
        finally
        {
            busy = false;
            searchSequence++;
            operation = null;
            if (closing) { reader?.Dispose(); reader = null; }
            else { searchProgress.Visible = false; UpdateControls(); }
        }
    }

    private void Status(string? notice = null)
    {
        state.Text = notice ?? (following ? "Following new lines · scroll, select text or Pause live to explore."
            : "Paused · new entries stay on disk. Browse pages, search, or Follow live.");
        if (page is null || reader is null) return;
        FooterNote.Text = $"{Path.GetFileName(reader.Path)} · {Bytes(page.Start)}–{Bytes(page.End)} of {Bytes(observedLength)}"
            + (page.PartialStart || page.PartialEnd ? " · continued line" : "");
    }

    private void UpdateControls()
    {
        follow.Text = following ? "Pause live" : "Follow live";
        follow.Enabled = following || !busy;
        files.Enabled = !busy;
        allFiles.Enabled = !busy;
        older.Enabled = oldest.Enabled = !busy && page is { Start: > 0 };
        newer.Enabled = !busy && page is not null;
        findOlder.Enabled = findNewer.Enabled = !busy && reader is not null && query.Text.Length > 0;
        query.ReadOnly = busy;
        cancelSearch.Enabled = false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F)) { Pause(); query.Focus(); query.SelectAll(); return true; }
        if (keyData == Keys.F3) { Find(false); return true; }
        if (keyData == (Keys.Shift | Keys.F3)) { Find(true); return true; }
        if (keyData == (Keys.Control | Keys.End)) { if (!following) ToggleFollow(); return true; }
        if (keyData == (Keys.Control | Keys.Home)) { Navigate(0); return true; }
        if (keyData == (Keys.Control | Keys.PageUp)) { Navigate(-1); return true; }
        if (keyData == (Keys.Control | Keys.PageDown)) { Navigate(1); return true; }
        if (keyData == Keys.Escape && busy) { operation?.Cancel(); return true; }
        if (keyData == Keys.Enter && query.Focused) { Find(true); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static string Bytes(long value) => value < 1024 ? $"{value} B" : value < 1048576 ? $"{value / 1024.0:0.0} KB" : $"{value / 1048576.0:0.00} MB";

    private void CopyPage()
    {
        Pause();
        try { if (text.TextLength > 0) Clipboard.SetText(text.Text); state.Text = "Copied this page. Select text and Ctrl+C to copy a smaller part."; }
        catch (ExternalException) { state.Text = "Clipboard busy"; }
    }

    private void OpenFolder()
    {
        try
        {
            var folder = Path.GetDirectoryName(path)!;
            Process.Start(new ProcessStartInfo("explorer.exe", File.Exists(path) ? $"/select,\"{path}\"" : $"\"{folder}\""))?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        { state.Text = "Windows could not open the log folder."; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            closing = true;
            generation++;
            timer.Dispose();
            detail.Dispose();
            operation?.Cancel();
            if (!busy) { reader?.Dispose(); reader = null; }
        }
        base.Dispose(disposing);
        if (disposing) mono.Dispose();
    }

    private sealed class LogTextBox : TextBox
    {
        internal event Action? Browse;
        protected override void WndProc(ref Message m)
        {
            // Scrolling a native textbox does not move its caret. Pause on intent.
            if (m.Msg is 0x020A or 0x020E or 0x0115 or 0x0114 or 0x0201 or 0x00A1) Browse?.Invoke();
            base.WndProc(ref m);
        }
        protected override void OnKeyDown(KeyEventArgs e) { Browse?.Invoke(); base.OnKeyDown(e); }
    }
}
