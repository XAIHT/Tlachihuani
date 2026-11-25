// Tlachihuani — Angela López Mendoza · XAIHT
namespace Tlachihuani.Tray;

internal sealed partial class ServicesForm
{
    private readonly Label hostState = Studio.Label("Checking Jobs…", Studio.Small);
    private readonly Label engineState = Studio.Label("Engine · checking", Studio.Small);
    private readonly Label webState = Studio.Label("Web · checking", Studio.Small);
    private readonly Label impactLine = Studio.Label("", Studio.Small, Studio.Muted);
    private readonly Label linkState = Studio.Label("Not connected", Studio.Small, Studio.Muted);
    private readonly Label linkServer = Studio.Label("—", Studio.Small);
    private readonly Label linkContact = Studio.Label("—", Studio.Small);
    private readonly Label linkMessages = Studio.Label("—", Studio.Small);
    private readonly StudioButton runtimeTab = new("Runtime", ButtonTone.Tab), accessTab = new("Access", ButtonTone.Tab),
        linkTab = new("xaiht.org", ButtonTone.Tab);
    private readonly Dictionary<string, StudioButton> buttons = new();
    private readonly Control runtime, access, link;
    private StudioButton connect = null!, disconnect = null!, websiteAccounts = null!;

    public ServicesForm(Icon icon) : base(icon, "Tlachihuani · Services", "Tlachihuani   /   Service control", "Services", new(778, 500), new(668, 466))
    {
        HeaderCommands.Controls.Add(Command("Remote-Controller…", () => RemoteControllerLauncher.Open(this)));
        HeaderCommands.Controls.Add(Command("Flow Pills…", () => FlowPillsLauncher.Open(this)));
        HeaderCommands.Controls.Add(Command("See log", () => { using var viewer = new LogViewerForm(Icon); viewer.ShowDialog(this); }));
        var layout = Studio.Grid([-1], [38, 8, -1]);
        var switcher = Studio.Actions(runtimeTab, accessTab, linkTab);
        switcher.AccessibleName = "Service views"; layout.Controls.Add(switcher, 0, 0);
        var pages = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        runtime = BuildRuntime(); access = BuildAccess(); link = BuildLink();
        pages.Controls.Add(runtime); pages.Controls.Add(access); pages.Controls.Add(link);
        layout.Controls.Add(pages, 0, 2); BodyArea.Controls.Add(layout);
        var tabs = new[] { runtimeTab, accessTab, linkTab };
        for (int i = 0; i < tabs.Length; i++)
        {
            int view = i;
            tabs[i].Click += (_, _) => ShowView(view);
            tabs[i].KeyDown += (_, e) =>
            {
                if (e.KeyCode is not (Keys.Left or Keys.Right)) return;
                int next = (view + (e.KeyCode == Keys.Right ? 1 : tabs.Length - 1)) % tabs.Length;
                ShowView(next); tabs[next].Focus(); e.Handled = true;
            };
        }
        ShowView(0);
        var close = Command("Close", Close); Commands.Controls.Add(close);
        Commands.Controls.Add(Command("Refresh", () => _ = Run())); CancelButton = close;
        UpdateActions();
        timer.Tick += (_, _) => PaintLink();
        timer.Tick += async (_, _) => await Run();
        Shown += async (_, _) => { PaintLink(); timer.Start(); await Run(); };
        FormClosed += (_, _) => { timer.Stop(); closing.Cancel(); };
    }

    private void ShowView(int view)
    {
        runtime.Visible = view == 0; access.Visible = view == 1; link.Visible = view == 2;
        runtimeTab.SelectTab(view == 0); accessTab.SelectTab(view == 1); linkTab.SelectTab(view == 2);
        runtimeTab.AccessibleDescription = view == 0 ? "Selected view" : "Show service runtime";
        accessTab.AccessibleDescription = view == 1 ? "Selected view" : "Show access controls";
        linkTab.AccessibleDescription = view == 2 ? "Selected view" : "Show the xaiht.org link";
        if (view == 2) PaintLink();
    }
    private StudioButton ActionButton(string caption, Func<Task> action, ButtonTone tone = ButtonTone.Secondary)
    {
        var button = Command(caption, () => _ = Run(action, caption), tone); buttons.Add(caption, button); return button;
    }
    private static Control CommandRow(string title, Label detail, params Control[] actions)
    {
        var labels = Studio.Grid([-1], [-1, 24, 23, -1]);
        labels.Controls.Add(Studio.Label(title, Studio.Strong, Studio.Text), 0, 1); labels.Controls.Add(detail, 0, 2);
        var row = Studio.Grid([-1, actions.Sum(x => x.Width + x.Margin.Horizontal)], [-1]);
        row.Controls.Add(labels, 0, 0);
        var strip = Studio.Actions(actions); strip.Dock = DockStyle.None; strip.Anchor = AnchorStyles.Right;
        strip.AutoSize = true; strip.Margin = Padding.Empty; row.Controls.Add(strip, 1, 0);
        return row;
    }
    private Control BuildRuntime()
    {
        var page = Studio.Grid([-1], [68, 10, -1]);
        var jobs = CommandRow("Jobs host", hostState, ActionButton("Start Jobs", () => HostAction("start")),
            ActionButton("Stop Jobs", () => HostAction("stop")));
        page.Controls.Add(Studio.Card(jobs, new Padding(14, 8, 6, 8)), 0, 0);
        var provider = Studio.Grid([-1], [24, -1, 1, -1, 24]);
        provider.Controls.Add(Studio.Label("TORRENTER", Studio.Caption, Studio.AccentHover), 0, 0);
        provider.Controls.Add(CommandRow("Torrent engine", engineState, ActionButton("Start provider", () => Provider("start")),
            ActionButton("Stop provider", () => Provider("stop")), ActionButton("Restart provider", () => Provider("restart"))), 0, 1);
        provider.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Studio.Hairline, Margin = Padding.Empty }, 0, 2);
        provider.Controls.Add(CommandRow("REST workspace", webState, ActionButton("Restart web", () => Call(new() { ["op"] = "restart-web" })),
            ActionButton("Open workspace", () => OpenWorkspace(signIn: true), ButtonTone.Primary)), 0, 3);
        provider.Controls.Add(impactLine, 0, 4);
        page.Controls.Add(Studio.Card(provider, new Padding(14, 6, 6, 6)), 0, 2);
        return page;
    }
    private Control BuildAccess()
    {
        var page = Studio.Grid([-1], [-1, 8, -1, 8, -1]);
        page.Controls.Add(Studio.Card(CommandRow("Tlamatini account", Studio.Label("The workspace uses your current login.", Studio.Small, Studio.Muted),
            ActionButton("Open downloads", () => OpenWorkspace(signIn: false))), new Padding(14, 6, 6, 6)), 0, 0);
        page.Controls.Add(Studio.Card(CommandRow("API credentials", Studio.Label("Account access and permissions", Studio.Small, Studio.Muted),
            ActionButton("Issue API credential", Issue), ActionButton("Revoke access", Revoke, ButtonTone.Danger)), new Padding(14, 6, 6, 6)), 0, 2);
        page.Controls.Add(Studio.Card(CommandRow("File erasure", Studio.Label("Review the exact files before approving removal.", Studio.Small, Studio.Muted),
            ActionButton("Review erasure", ReviewDeletion)), new Padding(14, 6, 6, 6)), 0, 4);
        return page;
    }
    // xaiht.org → Tlamatini-RC → Add this computer gives a key; Connect saves it here.
    private Control BuildLink()
    {
        var page = Studio.Grid([-1], [68, 10, -1]);
        connect = Command("Connect…", ConnectLink, ButtonTone.Primary);
        disconnect = Command("Disconnect", DisconnectLink, ButtonTone.Danger);
        websiteAccounts = Command("Accounts…", () =>
        {
            if (XaihtLink.Shared.Identity is not { } identity) return;
            using var dialog = new XaihtAccountsForm(this, identity); dialog.ShowDialog(this);
        });
        page.Controls.Add(Studio.Card(CommandRow("xaiht.org link", linkState, connect, websiteAccounts, disconnect), new Padding(14, 8, 6, 8)), 0, 0);
        var details = Studio.Grid([118, -1], [26, 26, 26, -1]);
        void Detail(int row, string title, Label value)
        {
            details.Controls.Add(Studio.Label(title, Studio.Caption, Studio.Muted), 0, row); details.Controls.Add(value, 1, row);
        }
        Detail(0, "WEBSITE", linkServer); Detail(1, "LAST CONTACT", linkContact); Detail(2, "MESSAGES", linkMessages);
        var note = Studio.Label("XT asks xaiht.org for messages over HTTPS, so this computer opens no port and works on any network. "
            + "Choose Accounts to allow prompts and answers for specific running Tlamatini accounts. Prompts and answers are not written to this log. Diagnostic messages remain inert.", Studio.Small, Studio.Muted);
        note.AutoEllipsis = false; note.TextAlign = ContentAlignment.TopLeft;
        details.Controls.Add(note, 0, 3); details.SetColumnSpan(note, 2);
        page.Controls.Add(Studio.Card(details, new Padding(14, 8, 14, 8)), 0, 2);
        return page;
    }
    private void ConnectLink()
    {
        XaihtLinkSettings? current = null;
        try { current = XaihtLinkStore.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException
            or InvalidDataException or System.Security.Cryptography.CryptographicException) { }
        var chosen = ServiceDialogs.RequestXaihtLink(this, current?.Server ?? XaihtLinkStore.DefaultServer);
        if (chosen is null) return;
        try { XaihtLinkStore.Save(chosen); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { ServiceDialogs.Notice(this, "xaiht.org link", "The key could not be saved: " + ex.Message); return; }
        TlachihuaniLog.Info("xaiht.org", $"Key saved for {chosen.Server} by the Windows user.");
        XaihtLink.Shared.Reconfigure(); PaintLink();
    }
    private void DisconnectLink()
    {
        if (!ServiceDialogs.Confirm(this, "Disconnect from xaiht.org?", "This computer stops asking xaiht.org for messages and forgets its key. "
            + "Remove the computer on xaiht.org too if you will not connect it again.", "Disconnect")) return;
        try { XaihtLinkStore.Delete(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { ServiceDialogs.Notice(this, "xaiht.org link", "The key could not be removed: " + ex.Message); return; }
        TlachihuaniLog.Info("xaiht.org", "Disconnected from xaiht.org by the Windows user; the key was removed.");
        XaihtLink.Shared.Reconfigure(); PaintLink();
    }
    private void PaintLink()
    {
        var s = XaihtLink.Shared.Status;
        bool saved = File.Exists(XaihtLinkStore.FilePath);
        linkState.Text = s.State switch
        {
            "online" => "Online" + (s.Computer is null ? "" : $"   /   {s.Computer}"),
            "connecting" => "Connecting…", "offline" => "Offline   /   retrying", "rejected" => "Key not accepted",
            "error" => "Key unreadable", _ => saved ? "Waiting for XT" : "Not connected"
        };
        linkState.ForeColor = s.State == "online" ? Studio.AccentHover : s.State is "rejected" or "error" ? Studio.Danger : Studio.Muted;
        linkServer.Text = s.Server ?? (saved ? "saved" : "—");
        linkContact.Text = s.LastContact is { } contact ? $"{contact.LocalDateTime:HH:mm:ss}   ·   {s.Detail}" : s.Detail;
        linkMessages.Text = s.LastMessage is { } last ? $"{s.Received} received   ·   last at {last.LocalDateTime:HH:mm:ss}" : $"{s.Received} received since XT started";
        disconnect.Enabled = saved;
        websiteAccounts.Enabled = s.State == "online" && XaihtLink.Shared.Identity is not null;
    }
    private void PaintState()
    {
        hostState.Text = $"Tlachihuani.Jobs   /   {StatusForm.Pretty(host)}";
        hostState.ForeColor = host == "running" ? Studio.AccentHover : Studio.Muted;
        engineState.Text = $"Engine · {(engine ? "ready" : "offline")}"; engineState.ForeColor = engine ? Studio.AccentHover : Studio.Muted;
        webState.Text = $"Web · {(web ? "ready" : "offline")}   /   127.0.0.1:12064"; webState.ForeColor = web ? Studio.AccentHover : Studio.Muted;
        impactLine.Text = impact.Length > 0 ? impact : host == "stopped" ? "Start Jobs to bring its providers online." :
            host == "not_installed" ? "Install the Jobs host to manage Torrenter." : "Transfer counts unavailable.";
    }
}
