// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (the window refreshes its view of every run several times a second; UiTrace writes the clicks)
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tlachihuani.Contracts;
using Tlachihuani.FlowPills;

namespace Tlachihuani.Tray;

// The Flow Pills Service window. Tlamatini's agentic flows (.flw) and flow pills (.fpmt) are
// opened, drawn, run independently and followed live. Left: the open flows with their nodes and
// parameters, and the inspector. Middle: the drawing of the selected flow, colored by its run.
// Bottom: every run, and the logs of the selected run, each one tailed on its own. Closing the
// window while flows run asks whether they keep running (XT's tray menu brings it back) or stop.
internal sealed class FlowPillsForm : StudioForm
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly Font Mono = new("Consolas", 9.5f);
    private readonly FlowPillsSettings settings = FlowPillsSettings.Load();
    private readonly FlowRegistry registry;
    private readonly EventWaitHandle showSignal, stopSignal;
    private readonly Action<int> runsChanged;
    private readonly IReadOnlyList<string> initialFiles;
    private int publishedRuns;
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 400 };
    private readonly List<FlowDocument> flows = [];
    private readonly Dictionary<FlowDocument, (StudioButton Tab, FlowCanvas Canvas, TreeNode Tree)> views = [];
    private readonly Dictionary<FlowDocument, FlowRun> followedByFlow = [];
    private readonly Dictionary<FlowRun, ListViewItem> rows = [];
    private readonly SplitContainer mainSplit = Split(Orientation.Vertical), leftSplit = Split(Orientation.Horizontal),
        centerSplit = Split(Orientation.Horizontal), bottomSplit = Split(Orientation.Vertical);
    private readonly TreeView explorer = new();
    private readonly TextBox inspector = Reader();
    private readonly FlowLayoutPanel tabs = new();
    private readonly Panel stage = new();
    private readonly ListView runList = new();
    private readonly ComboBox logChoice = new();
    private readonly CheckBox follow = new();
    private readonly TextBox logView = Reader();
    private readonly Panel inputBar = new();
    private readonly Label inputQuestion = new();
    private readonly TextBox inputText = new();
    private readonly StudioButton send, yes, no, openButton, runButton, pauseButton, stopButton, fitButton;
    private FlowDocument? active;
    private FlowRun? followed;
    private LogTail? shownLog;
    private long shownVersion = -1, canvasVersion = -1;
    private InputRequest? shownQuestion;
    private TlamatiniInstall? install;
    private bool syncing, stopping, exiting, inBackground, scaled;
    private DateTime stopDeadline, lastFooter;
    private TreeTag? inspected;

    // stopSignal: the Services panel asks to stop every flow and close. runsChanged: how many flows
    // run or wait, for the process record the panel reads.
    internal FlowPillsForm(Icon icon, EventWaitHandle showSignal, EventWaitHandle stopSignal, IReadOnlyList<string> files, Action<int> runsChanged)
        : base(icon, "Tlachihuani · Flow Pills", "Tlachihuani   /   Flow Pills Service", "Flow Pills", new(1320, 820), new(1000, 640))
    {
        this.showSignal = showSignal;
        this.stopSignal = stopSignal;
        this.runsChanged = runsChanged;
        initialFiles = files;
        registry = new FlowRegistry(settings);
        ShowInTaskbar = true;
        MinimizeBox = true;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        fitButton = Command("Fit", () => ActiveCanvas()?.Fit());
        stopButton = Command("Stop now", StopFollowed, ButtonTone.Danger);
        pauseButton = Command("Resume", PauseOrResume);
        runButton = Command("Run", RunActive, ButtonTone.Primary);
        openButton = Command("Open…", OpenFiles);
        stopButton.Text = "Stop";
        pauseButton.Text = "Pause";
        HeaderCommands.Controls.AddRange([fitButton, stopButton, pauseButton, runButton, openButton]);

        send = new StudioButton("Send", ButtonTone.Primary);
        yes = new StudioButton("Yes", ButtonTone.Primary);
        no = new StudioButton("No");
        send.Click += (_, _) => Answer(inputText.Text);
        yes.Click += (_, _) => Answer("yes");
        no.Click += (_, _) => Answer("no");

        BuildExplorer();
        BuildRuns();
        leftSplit.Panel1.Controls.Add(Section("FLOWS", explorer));
        leftSplit.Panel2.Controls.Add(Section("INSPECTOR", inspector));
        mainSplit.Panel1.Controls.Add(leftSplit);
        var drawing = new Panel { Dock = DockStyle.Fill, BackColor = Studio.Surface };
        stage.Dock = DockStyle.Fill;
        stage.BackColor = Studio.Pane;
        tabs.Dock = DockStyle.Top;
        tabs.Height = 44;
        tabs.WrapContents = false;
        tabs.AutoScroll = true;
        tabs.Padding = new Padding(0, 4, 0, 4);
        tabs.BackColor = Studio.Surface;
        drawing.Controls.Add(stage);
        drawing.Controls.Add(tabs);
        centerSplit.Panel1.Controls.Add(drawing);
        bottomSplit.Panel1.Controls.Add(Section("RUNS", runList));
        bottomSplit.Panel2.Controls.Add(BuildLogs());
        centerSplit.Panel2.Controls.Add(bottomSplit);
        mainSplit.Panel2.Controls.Add(centerSplit);
        BodyArea.Controls.Add(mainSplit);

        var close = Command("Close", Close);
        Commands.Controls.AddRange([close, Command("Runs folder", () => OpenFolder(FlowPillsPaths.Runs))]);
        FooterNote.Text = "Open a Tlamatini flow (.flw) or flow pill (.fpmt) to begin.";
        // These change on their own every few seconds; UiTrace still writes every click and choice.
        UiTrace.Ignore(FooterNote);
        UiTrace.Ignore(logView);
        UiTrace.Ignore(inspector);
        foreach (var control in new Control[] { explorer, inspector, runList, logView }) DarkScrollbars(control);
        AcceptDrops(this);
        refresh.Tick += (_, _) => Tick();
        UpdateButtons();
    }

    // ---- layout ---------------------------------------------------------------------------

    private static SplitContainer Split(Orientation orientation) => new()
    {
        Dock = DockStyle.Fill, Orientation = orientation, SplitterWidth = 7, BackColor = Studio.Surface, Margin = Padding.Empty,
    };

    private static TextBox Reader() => new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BorderStyle = BorderStyle.None,
        BackColor = Studio.Pane, ForeColor = Studio.Body, Font = Mono, Dock = DockStyle.Fill, MaxLength = 0,
    };

    private static Control Section(string title, Control body, Control? header = null)
    {
        body.Dock = DockStyle.Fill;
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Studio.Surface, Padding = new Padding(0, 0, 0, 2) };
        panel.Controls.Add(Studio.Card(body, new Padding(1)));
        var caption = header ?? Studio.Label(title, Studio.Caption, Studio.AccentHover);
        caption.Dock = DockStyle.Top;
        caption.Height = header is null ? 24 : 40;
        panel.Controls.Add(caption);
        return panel;
    }

    private void BuildExplorer()
    {
        explorer.BorderStyle = BorderStyle.None;
        explorer.BackColor = Studio.Pane;
        explorer.ForeColor = Studio.Text;
        explorer.Font = Studio.Regular;
        explorer.HideSelection = false;
        explorer.FullRowSelect = true;
        explorer.ShowLines = false;
        explorer.Indent = 16;
        explorer.AccessibleName = "Open flows, their nodes and parameters";
        explorer.AfterSelect += (_, e) => TreeSelected(e.Node);
        explorer.NodeMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.Node?.Tag is not TreeTag tag) return;
            explorer.SelectedNode = e.Node;
            (tag.Key is null ? FlowMenu(tag.Flow) : ValueMenu(tag)).Show(explorer, e.Location);
        };
    }

    private void BuildRuns()
    {
        runList.View = View.Details;
        runList.FullRowSelect = true;
        runList.HideSelection = false;
        runList.MultiSelect = false;
        runList.BorderStyle = BorderStyle.None;
        runList.BackColor = Studio.Pane;
        runList.ForeColor = Studio.Text;
        runList.Font = Studio.Small;
        runList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        runList.OwnerDraw = true;
        runList.AccessibleName = "Runs";
        foreach (var (title, width) in new[] { ("#", 36), ("Flow", 130), ("State", 84), ("Status", 320), ("Elapsed", 72), ("Busy", 44), ("Peak", 64), ("Kind", 44) })
            runList.Columns.Add(title, width);
        runList.DrawColumnHeader += (_, e) =>
        {
            using (var back = new SolidBrush(Studio.Chrome)) e.Graphics.FillRectangle(back, e.Bounds);
            using (var line = new Pen(Studio.Hairline)) e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Studio.Caption, Rectangle.Inflate(e.Bounds, -6, 0), Studio.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        runList.DrawItem += (_, _) => { };   // every cell is drawn below, the selected row in the window's colors
        runList.DrawSubItem += (_, e) =>
        {
            if (e.Item is not { } item || e.SubItem is not { } cell) return;
            var bounds = e.ColumnIndex == 0 ? new Rectangle(e.Bounds.X, e.Bounds.Y, runList.Columns[0].Width, e.Bounds.Height) : e.Bounds;
            bool selected = item.Selected;
            using (var back = new SolidBrush(selected ? Studio.Hover : Studio.Pane)) e.Graphics.FillRectangle(back, bounds);
            TextRenderer.DrawText(e.Graphics, cell.Text, runList.Font, Rectangle.Inflate(bounds, -LogicalToDeviceUnits(4), 0), selected ? Studio.Text : item.ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        };
        runList.SelectedIndexChanged += (_, _) =>
        {
            if (!syncing && runList.SelectedItems.Count == 1 && runList.SelectedItems[0].Tag is FlowRun run) FollowRun(run, activate: true);
        };
        runList.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && runList.GetItemAt(e.X, e.Y) is { Tag: FlowRun run } item)
            {
                item.Selected = true;
                RunMenu(run).Show(runList, e.Location);
            }
        };
    }

    private Control BuildLogs()
    {
        logChoice.DropDownStyle = ComboBoxStyle.DropDownList;
        logChoice.FlatStyle = FlatStyle.Flat;
        logChoice.BackColor = Studio.Pane;
        logChoice.ForeColor = Studio.Text;
        logChoice.Font = Studio.Regular;
        logChoice.Width = 260;
        logChoice.Margin = new Padding(10, 7, 0, 0);
        logChoice.AccessibleName = "Log to follow";
        logChoice.SelectedIndexChanged += (_, _) => { shownVersion = -1; RefreshLog(); };
        follow.Text = "Follow the end";
        follow.Checked = true;
        follow.AutoSize = true;
        follow.ForeColor = Studio.Body;
        follow.Font = Studio.Small;
        follow.Margin = new Padding(12, 11, 0, 0);
        follow.CheckedChanged += (_, _) => { if (follow.Checked) { shownVersion = -1; RefreshLog(); } };
        var openLog = Command("Open file", () => { if (SelectedTail() is { } tail && File.Exists(tail.FilePath)) OpenFolder(tail.FilePath, select: true); });
        openLog.Margin = new Padding(12, 3, 0, 0);
        var caption = Studio.Label("LOGS", Studio.Caption, Studio.AccentHover);
        caption.Dock = DockStyle.None;
        caption.AutoSize = true;
        caption.Margin = new Padding(0, 12, 0, 0);
        var header = Studio.Actions(caption, logChoice, follow, openLog);
        header.BackColor = Studio.Surface;

        inputBar.Dock = DockStyle.Top;
        inputBar.Height = 100;
        inputBar.Visible = false;
        inputBar.BackColor = Studio.Raised;
        inputBar.Padding = new Padding(12, 8, 12, 8);
        inputQuestion.Dock = DockStyle.Fill;
        inputQuestion.ForeColor = Studio.Text;
        inputQuestion.Font = Studio.Regular;
        inputQuestion.UseMnemonic = false;
        inputQuestion.AutoEllipsis = true;
        inputText.BorderStyle = BorderStyle.FixedSingle;
        inputText.BackColor = Studio.Pane;
        inputText.ForeColor = Studio.Text;
        inputText.Font = Studio.Regular;
        inputText.Width = 420;
        inputText.Margin = new Padding(0, 6, 8, 0);
        inputText.AccessibleName = "Your reply";
        inputText.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Answer(inputText.Text); } };
        var answers = Studio.Actions(inputText, send, yes, no);
        answers.Dock = DockStyle.Bottom;
        answers.Height = 38;
        inputBar.Controls.Add(inputQuestion);
        inputBar.Controls.Add(answers);

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Studio.Surface };
        body.Controls.Add(Studio.Card(logView, new Padding(1)));
        body.Controls.Add(inputBar);
        return Section("LOGS", body, header);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        scaled = true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        explorer.ItemHeight = LogicalToDeviceUnits(24);
        // List columns are not scaled with the window; give them their size on this display.
        foreach (ColumnHeader column in runList.Columns) column.Width = LogicalToDeviceUnits(column.Width);
        Place(mainSplit, 0.24, 230, 560);
        Place(leftSplit, 0.52, 130, 120);
        Place(centerSplit, 0.60, 220, 190);
        Place(bottomSplit, 0.44, 320, 320);
        if (initialFiles.Count > 0) LoadFiles(initialFiles);
        LoadFiles(FlowPillsInbox.Take());
        refresh.Start();
        Tick();
    }

    private void Place(SplitContainer split, double fraction, int first, int second)
    {
        first = LogicalToDeviceUnits(first);
        second = LogicalToDeviceUnits(second);
        int length = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
        if (length <= first + second + split.SplitterWidth) return;
        split.Panel1MinSize = first;
        split.Panel2MinSize = second;
        split.SplitterDistance = Math.Clamp((int)(length * fraction), first, length - second - split.SplitterWidth);
    }

    // ---- flows ----------------------------------------------------------------------------

    private sealed record TreeTag(FlowDocument Flow, string? NodeId, string? Key);
    private sealed record LogItem(LogTail Tail) { public override string ToString() => Tail.Name; }

    private void OpenFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open Tlamatini flows",
            Filter = "Tlamatini flows (*.flw;*.fpmt)|*.flw;*.fpmt|Agentic flows (*.flw)|*.flw|Flow pills (*.fpmt)|*.fpmt|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) LoadFiles(dialog.FileNames);
    }

    private void LoadFiles(IEnumerable<string> paths)
    {
        var problems = new List<string>();
        FlowDocument? last = null;
        foreach (var path in paths)
        {
            try
            {
                string full = Path.GetFullPath(path);
                var open = flows.FirstOrDefault(flow => string.Equals(flow.FilePath, full, StringComparison.OrdinalIgnoreCase));
                if (open is not null) { last = open; continue; }
                var loaded = FlowDocument.Load(full);
                AddFlow(loaded);
                TraceLog.I($"Flow Pills opened {full}: {loaded.KindLabel}, {loaded.Nodes.Count} nodes, {loaded.Edges.Count} wires");
                last = loaded;
            }
            catch (FlowPillsException ex) { problems.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
            { problems.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
        }
        if (last is not null) ShowFlow(last);
        if (problems.Count > 0)
        {
            TraceLog.W("Flow Pills could not open: " + string.Join(" | ", problems));
            ServiceDialogs.Notice(this, "Open flows", string.Join("\n\n", problems));
        }
    }

    private void AddFlow(FlowDocument flow)
    {
        flows.Add(flow);
        var canvas = new FlowCanvas { Dock = DockStyle.Fill, Visible = false };
        canvas.NodeClicked += id => CanvasClicked(flow, id);
        stage.Controls.Add(canvas);
        var tab = new StudioButton(Short(flow.Name, 36) + (flow.Kind == FlowKind.Prompts ? "  · pill" : "  · flow"), ButtonTone.Tab) { Margin = new Padding(0, 0, 6, 0) };
        if (scaled)
        {
            // The window was scaled to the display when it loaded; a tab added later is scaled the same way.
            tab.Size = LogicalToDeviceUnits(tab.Size);
            tab.Margin = new Padding(0, 0, LogicalToDeviceUnits(6), 0);
        }
        tab.Click += (_, _) => ShowFlow(flow);
        tab.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) FlowMenu(flow).Show(tab, e.Location);
            else if (e.Button == MouseButtons.Middle) CloseFlow(flow);
        };
        tabs.Controls.Add(tab);
        var tree = TreeFor(flow);
        explorer.Nodes.Add(tree);
        views[flow] = (tab, canvas, tree);
        canvas.Display(flow);
        AcceptDrops(canvas);
        AcceptDrops(tab);
    }

    private static TreeNode TreeFor(FlowDocument flow)
    {
        var root = new TreeNode($"{flow.Name}   ({(flow.Kind == FlowKind.Agents ? ".flw" : ".fpmt")} · {flow.Nodes.Count})") { Tag = new TreeTag(flow, null, null) };
        foreach (var node in flow.Nodes)
        {
            string title = node.IsComment ? "Comment: " + Short(StringOf(node.Config["text"]), 48) : $"{node.Label}   · {node.Type.Replace('_', ' ')}";
            var item = new TreeNode(title) { Tag = new TreeTag(flow, node.Id, null) };
            foreach (var (key, value) in node.Config)
                if (key != "runs") item.Nodes.Add(new TreeNode($"{key}: {Short(ValueText(value), 64)}") { Tag = new TreeTag(flow, node.Id, key) });
            root.Nodes.Add(item);
        }
        root.Expand();
        return root;
    }

    private void ShowFlow(FlowDocument flow)
    {
        if (!views.TryGetValue(flow, out var view)) return;
        active = flow;
        foreach (var (document, other) in views)
        {
            other.Tab.SelectTab(document == flow);
            other.Canvas.Visible = document == flow;
        }
        tabs.ScrollControlIntoView(view.Tab);
        if (followed?.Document != flow && followedByFlow.TryGetValue(flow, out var run)) FollowRun(run, activate: false);
        if (!syncing)
        {
            syncing = true;
            if (explorer.SelectedNode?.Tag is not TreeTag tag || tag.Flow != flow) explorer.SelectedNode = view.Tree;
            syncing = false;
        }
        ShowInspector(explorer.SelectedNode?.Tag as TreeTag ?? new TreeTag(flow, null, null));
        canvasVersion = -1;
        UpdateButtons();
    }

    private void CloseFlow(FlowDocument flow)
    {
        int running = registry.Runs.Count(run => run.Document == flow && !run.IsFinished);
        if (running > 0)
        {
            ServiceDialogs.Notice(this, "Close flow", $"{flow.Name} has {running} run(s) that have not ended. Stop them first.");
            return;
        }
        if (!views.Remove(flow, out var view)) return;
        flows.Remove(flow);
        followedByFlow.Remove(flow);
        tabs.Controls.Remove(view.Tab);
        stage.Controls.Remove(view.Canvas);
        explorer.Nodes.Remove(view.Tree);
        view.Tab.Dispose();
        view.Canvas.Dispose();
        if (active == flow)
        {
            active = null;
            if (flows.Count > 0) ShowFlow(flows[^1]);
            else { inspector.Clear(); inspected = null; UpdateButtons(); }
        }
    }

    private void ReloadFlow(FlowDocument flow)
    {
        string path = flow.FilePath;
        CloseFlow(flow);
        if (!views.ContainsKey(flow)) LoadFiles([path]);
    }

    private void TreeSelected(TreeNode? node)
    {
        if (node?.Tag is not TreeTag tag) return;
        if (!syncing)
        {
            syncing = true;
            if (active != tag.Flow) ShowFlow(tag.Flow);
            if (tag.NodeId is { } id && views.TryGetValue(tag.Flow, out var view))
            {
                view.Canvas.Highlight(id, reveal: true);
                ChooseLogFor(tag.Flow, id);
            }
            syncing = false;
        }
        ShowInspector(tag);
    }

    private void CanvasClicked(FlowDocument flow, string id)
    {
        if (syncing || !views.TryGetValue(flow, out var view)) return;
        syncing = true;
        foreach (TreeNode item in view.Tree.Nodes)
            if (item.Tag is TreeTag tag && tag.NodeId == id) { explorer.SelectedNode = item; break; }
        syncing = false;
        view.Canvas.Highlight(id, reveal: false);
        ShowInspector(new TreeTag(flow, id, null));
        ChooseLogFor(flow, id);
    }

    // The inspector shows live facts (runs, a node's state), so the refresh timer redraws it.
    private void ShowInspector(TreeTag tag, bool refresh = false)
    {
        inspected = tag;
        var flow = tag.Flow;
        var lines = new List<string>();
        if (tag.NodeId is null)
        {
            lines.Add($"File       {flow.FilePath}");
            lines.Add($"Kind       {flow.KindLabel}");
            lines.Add($"Nodes      {flow.Nodes.Count}    Wires {flow.Edges.Count}");
            if (flow.Kind == FlowKind.Prompts) lines.Add($"Start      {flow.Start}    Step limit {flow.MaxSteps}");
            else lines.Add("Agents     " + string.Join(", ", flow.Nodes.GroupBy(node => node.Type).OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group.Count() > 1 ? $"{group.Key} ×{group.Count()}" : group.Key)));
            var runs = registry.Runs.Where(run => run.Document == flow).ToList();
            lines.Add($"Runs       {runs.Count} ({runs.Count(run => run.IsActive)} active)");
            lines.Add("");
            lines.Add(flow.Kind == FlowKind.Agents
                ? "Run compiles this flow with Tlamatini's own agents into a pool of its own and starts its Starter(s), as Tlamatini's Agentic Control Panel does."
                : "Run plays this flow pill step by step with Tlamatini's models, as Tlamatini's Prompt Flow Panel does.");
        }
        else if (flow.Find(tag.NodeId) is { } node)
        {
            if (tag.Key is { } key)
            {
                lines.Add($"{node.Label} · {key}");
                lines.Add("");
                lines.Add(node.Config[key] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : JsonText(node.Config[key]));
            }
            else
            {
                lines.Add($"Node       {node.Label}");
                lines.Add($"Type       {node.Type}");
                lines.Add($"Id         {node.Id}");
                if (node.PoolName.Length > 0) lines.Add($"Pool       {node.PoolName}");
                if (node.Purpose.Length > 0) lines.Add($"Purpose    {node.Purpose}");
                if (followed is { } current && current.Document == flow) lines.Add($"State      {current.NodeStateOf(node.Id)} (run {current.Id})");
                lines.Add("");
                lines.Add(JsonText(node.Config));
            }
        }
        string text = string.Join("\r\n", lines).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        if (inspector.Text == text) return;
        int first = refresh ? (int)SendMessage(inspector.Handle, EmGetFirstVisibleLine, 0, 0) : 0;   // a refresh keeps the reader's place
        inspector.Text = text;
        if (first > 0) SendMessage(inspector.Handle, EmLineScroll, 0, first);
    }

    // ---- runs -----------------------------------------------------------------------------

    private void RunActive()
    {
        if (active is null || stopping) return;
        try { install = TlamatiniInstall.Discover(settings); }
        catch (FlowPillsException ex)
        {
            ServiceDialogs.Notice(this, "Run flow", ex.Message);
            return;
        }
        FlowRun run;
        try { run = registry.Submit(active, install); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            ServiceDialogs.Notice(this, "Run flow", "The run could not be prepared: " + ex.Message);
            return;
        }
        followedByFlow[active] = run;
        FollowRun(run, activate: false);
        UpdateRow(run);
    }

    private void PauseOrResume()
    {
        if (followed is not { } run) return;
        if (run.CanResume) run.Resume();
        else if (run.CanPause) run.Pause();
    }

    private void StopFollowed()
    {
        if (followed is { CanStop: true } run) run.Stop();
    }

    private void FollowRun(FlowRun run, bool activate)
    {
        followed = run;
        followedByFlow[run.Document] = run;
        if (!views.ContainsKey(run.Document)) AddFlow(run.Document);
        views[run.Document].Canvas.Follow(run);
        if (activate && active != run.Document) ShowFlow(run.Document);
        if (rows.TryGetValue(run, out var item) && !item.Selected)
        {
            syncing = true;
            item.Selected = true;
            item.EnsureVisible();
            syncing = false;
        }
        shownVersion = -1;
        canvasVersion = -1;
        RefreshLogChoices(force: true);
        UpdateButtons();
    }

    private void UpdateRow(FlowRun run)
    {
        if (!rows.TryGetValue(run, out var item))
        {
            item = new ListViewItem(Enumerable.Repeat("", 8).ToArray()) { Tag = run, UseItemStyleForSubItems = true };
            rows[run] = item;
            runList.Items.Insert(0, item);
        }
        var pill = run as PromptFlowRun;
        bool asking = pill?.PendingInput is not null;
        string elapsed = run.Started is null ? "" : run.Elapsed.ToString(run.Elapsed.TotalDays >= 1 ? @"d\.hh\:mm\:ss" : @"hh\:mm\:ss", CultureInfo.InvariantCulture);
        string[] values =
        [
            run.Number.ToString(CultureInfo.InvariantCulture), run.Document.Name, run.State.ToString(),
            asking ? "Waiting for your reply · " + run.Status : run.Status, elapsed,
            run.IsActive ? run.RunningNodes.ToString(CultureInfo.InvariantCulture) : "",
            run.PeakMemory > 0 ? FlowCapacity.Size(run.PeakMemory) : "", run.Document.Kind == FlowKind.Agents ? "flow" : "pill",
        ];
        for (int i = 0; i < values.Length; i++)
            if (item.SubItems[i].Text != values[i]) item.SubItems[i].Text = values[i];
        var color = asking ? ColorTranslator.FromHtml("#f59e0b") : run.State switch
        {
            RunState.Running or RunState.Preparing => Studio.AccentHover,
            RunState.Paused => ColorTranslator.FromHtml("#facc15"),
            RunState.Failed => Studio.Danger,
            RunState.Completed or RunState.Stopped => Studio.Muted,
            _ => Studio.Body,
        };
        if (item.ForeColor != color) item.ForeColor = color;
    }

    private void Tick()
    {
        if (showSignal.WaitOne(0))
        {
            inBackground = false;
            WindowActivation.Show(this);
            LoadFiles(FlowPillsInbox.Take());
        }
        if (stopSignal.WaitOne(0) && !stopping)
        {
            int count = registry.Active + registry.Waiting;
            TraceLog.I($"Flow Pills was asked to stop by the Services panel; stopping {count} flow(s) and closing");
            stopping = true;
            stopDeadline = DateTime.UtcNow.AddSeconds(30);
            registry.StopAll();
        }
        int runs = registry.Active + registry.Waiting;
        if (runs != publishedRuns)
        {
            publishedRuns = runs;
            runsChanged(runs);
        }
        var all = registry.Runs;
        foreach (var run in all) UpdateRow(run);
        foreach (var gone in rows.Keys.Where(run => !all.Contains(run)).ToList())
        {
            runList.Items.Remove(rows[gone]);
            rows.Remove(gone);
        }
        if (ActiveCanvas() is { } canvas && canvas.Followed is { } shown && shown.Version != canvasVersion)
        {
            canvasVersion = shown.Version;
            canvas.Invalidate();
        }
        RefreshLogChoices(force: false);
        RefreshLog();
        RefreshInput();
        UpdateButtons();
        if (inspected is { } tag && views.ContainsKey(tag.Flow)) ShowInspector(tag, refresh: true);
        if ((DateTime.UtcNow - lastFooter).TotalSeconds >= 2 || stopping)
        {
            lastFooter = DateTime.UtcNow;
            FooterNote.Text = stopping ? $"Stopping {registry.Active + registry.Waiting} flow(s)… Flow Pills closes when they have ended."
                : registry.Capacity.Describe(registry.Active, registry.Waiting) + (install is null ? "" : "   ·   Tlamatini agents: " + install.AgentsRoot);
        }
        bool idle = registry.Idle;
        if ((stopping && (idle || DateTime.UtcNow > stopDeadline)) || (inBackground && idle))
        {
            exiting = true;
            Close();
        }
    }

    // ---- logs and questions ---------------------------------------------------------------

    private LogTail? SelectedTail() => (logChoice.SelectedItem as LogItem)?.Tail;

    private void RefreshLogChoices(bool force)
    {
        IReadOnlyList<LogTail> logs = followed?.Logs ?? Array.Empty<LogTail>();
        bool same = logs.Count == logChoice.Items.Count;
        for (int i = 0; same && i < logs.Count; i++) same = ((LogItem)logChoice.Items[i]!).Tail == logs[i];
        if (same && !force) return;
        var keep = SelectedTail();
        logChoice.BeginUpdate();
        logChoice.Items.Clear();
        foreach (var log in logs) logChoice.Items.Add(new LogItem(log));
        int index = keep is null ? 0 : Math.Max(0, logs.ToList().IndexOf(keep));
        if (logChoice.Items.Count > 0) logChoice.SelectedIndex = Math.Min(index, logChoice.Items.Count - 1);
        logChoice.EndUpdate();
        shownVersion = -1;
    }

    private void ChooseLogFor(FlowDocument flow, string nodeId)
    {
        if (followed?.Document != flow || flow.Find(nodeId) is not { } node) return;
        string wanted = flow.Kind == FlowKind.Agents ? node.PoolName : "Transcript";
        for (int i = 0; i < logChoice.Items.Count; i++)
            if (((LogItem)logChoice.Items[i]!).Tail.Name == wanted) { logChoice.SelectedIndex = i; return; }
    }

    private void RefreshLog()
    {
        var tail = SelectedTail();
        if (tail is null)
        {
            if (logView.TextLength > 0) logView.Clear();
            shownLog = null;
            return;
        }
        if (followed?.IsFinished == true) tail.Poll();   // a finished run's thread no longer reads its logs
        long version = tail.Version;
        if (tail == shownLog && version == shownVersion) return;
        var lines = tail.Lines(2000);
        string text = lines.Length == 0 ? (tail.Exists ? "" : "(this log has not been written yet)") : string.Join("\r\n", lines);
        int first = tail == shownLog ? (int)SendMessage(logView.Handle, EmGetFirstVisibleLine, 0, 0) : 0;
        SendMessage(logView.Handle, WmSetRedraw, 0, 0);
        logView.Text = text;
        SendMessage(logView.Handle, WmSetRedraw, 1, 0);
        // Scrolled once drawing is back on, then painted once: the newest line, or where the reader was.
        if (follow.Checked) SendMessage(logView.Handle, WmVScroll, SbBottom, 0);
        else if (first > 0) SendMessage(logView.Handle, EmLineScroll, 0, first);
        logView.Invalidate();
        shownLog = tail;
        shownVersion = version;
    }

    private void RefreshInput()
    {
        var question = (followed as PromptFlowRun)?.PendingInput;
        if (question == shownQuestion) return;
        shownQuestion = question;
        inputBar.Visible = question is not null;
        if (question is null) return;
        bool decision = question.Kind == "decision";
        inputQuestion.Text = (decision ? "This flow pill asks you Yes or No:  " : "This flow pill asks you:  ") + question.Message;
        inputText.Visible = send.Visible = !decision;
        yes.Visible = no.Visible = decision;
        inputText.Clear();
        // The answer is one key away: Enter sends the reply, or answers Yes (Tab reaches No).
        if (decision) yes.Focus();
        else inputText.Focus();
    }

    private void Answer(string text)
    {
        if (followed is PromptFlowRun pill && shownQuestion is { } question && pill.Reply(question.Id, text))
        {
            inputText.Clear();
            inputBar.Visible = false;
            return;
        }
        ServiceDialogs.Notice(this, "Reply", "That question is no longer waiting for an answer.");
    }

    // ---- commands, menus, helpers ---------------------------------------------------------

    private FlowCanvas? ActiveCanvas() => active is not null && views.TryGetValue(active, out var view) ? view.Canvas : null;

    private void UpdateButtons()
    {
        runButton.Enabled = active is not null && !stopping;
        var run = followed;
        pauseButton.Enabled = !stopping && run is not null && (run.CanPause || run.CanResume);
        string pauseText = run?.CanResume == true ? "Resume" : "Pause";
        if (pauseButton.Text != pauseText) pauseButton.Text = pauseText;
        stopButton.Enabled = run?.CanStop == true;
        string stopText = run?.State == RunState.Stopping ? "Stop now" : "Stop";
        if (stopButton.Text != stopText) stopButton.Text = stopText;
        fitButton.Enabled = active is not null;
    }

    private ContextMenuStrip FlowMenu(FlowDocument flow)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Run", null, (_, _) => { ShowFlow(flow); RunActive(); });
        menu.Items.Add("Reload from disk", null, (_, _) => ReloadFlow(flow));
        menu.Items.Add("Show in folder", null, (_, _) => OpenFolder(flow.FilePath, select: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Close", null, (_, _) => CloseFlow(flow));
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        return menu;
    }

    private ContextMenuStrip ValueMenu(TreeTag tag)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Copy value", null, (_, _) =>
        {
            var value = tag.Flow.Find(tag.NodeId ?? "")?.Config[tag.Key ?? ""];
            string text = value is JsonValue plain && plain.GetValueKind() == JsonValueKind.String ? plain.GetValue<string>() : JsonText(value);
            if (text.Length > 0) Clipboard.SetText(text);
        });
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        return menu;
    }

    private ContextMenuStrip RunMenu(FlowRun run)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(run.CanResume ? "Resume" : "Pause", null, (_, _) => { if (run.CanResume) run.Resume(); else run.Pause(); }).Enabled = run.CanPause || run.CanResume;
        menu.Items.Add(run.State == RunState.Stopping ? "Stop now" : "Stop", null, (_, _) => run.Stop()).Enabled = run.CanStop;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open run folder", null, (_, _) => OpenFolder(run.Folder));
        if (run is AgentFlowRun agents && agents.PoolFolder.Length > 0) menu.Items.Add("Open agents folder", null, (_, _) => OpenFolder(agents.PoolFolder));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Remove from the list", null, (_, _) =>
        {
            if (followed == run) followed = null;
            foreach (var flow in followedByFlow.Where(pair => pair.Value == run).Select(pair => pair.Key).ToList()) followedByFlow.Remove(flow);
            if (views.TryGetValue(run.Document, out var view) && view.Canvas.Followed == run) view.Canvas.Follow(null);
            registry.Remove(run);
            RefreshLogChoices(force: true);
        }).Enabled = run.IsFinished;
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        return menu;
    }

    // A folder (or a file, selected) in Explorer. Only this window's own paths are opened.
    private void OpenFolder(string path, bool select = false)
    {
        try
        {
            if (!select) Directory.CreateDirectory(path);
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            start.ArgumentList.Add(select ? "/select," + path : path);
            using var launched = Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        { ServiceDialogs.Notice(this, "Open folder", "Windows could not open " + path + ": " + ex.Message); }
    }

    private void AcceptDrops(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) =>
        {
            e.Effect = e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Any(IsFlowFile) ? DragDropEffects.Copy : DragDropEffects.None;
        };
        control.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) LoadFiles(files.Where(IsFlowFile));
        };
        foreach (Control child in control.Controls) AcceptDrops(child);
    }

    private static bool IsFlowFile(string path)
        => path.EndsWith(".flw", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".fpmt", StringComparison.OrdinalIgnoreCase);

    // Ctrl+O opens, F5 runs the flow in front, Shift+F5 stops the run followed, Ctrl+Tab and
    // Ctrl+Shift+Tab go through the open flows.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.O: OpenFiles(); return true;
            case Keys.F5: RunActive(); return true;
            case Keys.Shift | Keys.F5: StopFollowed(); return true;
            case (Keys.Control | Keys.Tab) or (Keys.Control | Keys.Shift | Keys.Tab) when flows.Count > 0:
                int at = active is null ? -1 : flows.IndexOf(active);
                int step = (keyData & Keys.Shift) != 0 ? -1 : 1;
                ShowFlow(flows[((at + step) % flows.Count + flows.Count) % flows.Count]);
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (stopping && !exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;   // every flow is already being stopped; the window closes when they have ended
            return;
        }
        int pending = registry.Active + registry.Waiting;
        if (!exiting && e.CloseReason == CloseReason.UserClosing && pending > 0)
        {
            e.Cancel = true;
            var answer = ServiceDialogs.Choose(this, "Close Flow Pills",
                $"{(pending == 1 ? "1 flow is" : $"{pending} flows are")} running or waiting.\n\nKeep running: the window closes and the flows go on. XT's tray menu, Flow Pills…, brings it back."
                + "\nStop and close: every flow is stopped the way Tlamatini's Stop does, then Flow Pills closes.",
                "Keep running", "Stop and close");
            if (answer == DialogResult.Yes)
            {
                inBackground = true;
                Hide();
                TraceLog.I($"Flow Pills window hidden; {pending} flow(s) keep running");
            }
            else if (answer == DialogResult.No)
            {
                stopping = true;
                stopDeadline = DateTime.UtcNow.AddSeconds(30);
                registry.StopAll();
                TraceLog.I($"Flow Pills stops {pending} flow(s) before closing");
                BeginInvoke(new Action(Tick));   // after this close is cancelled, never inside it
            }
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        refresh.Stop();
        registry.Dispose();
        TraceLog.I("Flow Pills window closed");
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) refresh.Dispose();
        base.Dispose(disposing);
    }

    private static string Short(string text, int length)
    {
        string line = text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return line.Length > length ? line[..length] + "…" : line;
    }

    private static string StringOf(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : "";

    private static string ValueText(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : node?.ToJsonString() ?? "null";

    private static string JsonText(JsonNode? node) => node?.ToJsonString(Indented) ?? "null";

    private static void DarkScrollbars(Control control)
    {
        void Apply() { try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { } }
        if (control.IsHandleCreated) Apply();
        control.HandleCreated += (_, _) => Apply();
    }

    private const int WmSetRedraw = 0x000B, WmVScroll = 0x0115, SbBottom = 7, EmGetFirstVisibleLine = 0x00CE, EmLineScroll = 0x00B6;
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, int wParam, int lParam);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr window, string? application, string? list);
}
