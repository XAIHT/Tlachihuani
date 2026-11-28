// Tlachihuani — Angela López Mendoza · XAIHT
namespace Tlachihuani.Tray;

// Runtime holds one section per service, all built alike (ServiceLine): a light, the name, one
// status sentence and the buttons, in the same fonts, colors and places. Each service starts and
// stops on its own. HandGI's section also opens its settings, in plain words, with no setup jargon.
internal sealed partial class ServicesForm
{
    private const int ButtonWidth = 100, Inner = 44, Pad = 5, OneRow = Inner + 2 * Pad, TwoRows = 2 * Inner + 1 + 2 * Pad, Gap = 6;
    private readonly ToolTip tips = new() { InitialDelay = 400, ReshowDelay = 150, AutoPopDelay = 15000 };
    private readonly ServiceLine jobsLine, engineLine, webLine, rcLine, flowLine, handLine, signsLine, linkLine;
    private readonly Label linkServer = Studio.Label("—", Studio.Small);
    private readonly Label linkContact = Studio.Label("—", Studio.Small);
    private readonly Label linkMessages = Studio.Label("—", Studio.Small);
    private readonly StudioButton runtimeTab = new("Runtime", ButtonTone.Tab), accessTab = new("Access", ButtonTone.Tab),
        linkTab = new("xaiht.org", ButtonTone.Tab);
    private readonly Dictionary<string, StudioButton> buttons = new();
    private readonly Control runtime, access, link;
    private StudioButton connect = null!, disconnect = null!, websiteAccounts = null!;

    public ServicesForm(Icon icon) : base(icon, "Tlachihuani · Services", "Tlachihuani   /   Service control", "Services", new(860, 612), new(760, 612))
    {
        jobsLine = new("Jobs host", "Checking…", tips);
        engineLine = new("Torrent engine", "Checking…", tips);
        webLine = new("REST workspace", "Checking…", tips);
        rcLine = new("Remote-Controller", "Checking…", tips);
        flowLine = new("Flow Pills", "Checking…", tips);
        handLine = new("HandGI", "Checking…", tips);
        signsLine = new("Hand signs", "Choose what each sign does and which camera to use", tips, lamp: false);
        linkLine = new("xaiht.org link", "Not connected", tips);
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
        runtimeTab.AccessibleDescription = view == 0 ? "Selected view" : "Show the services";
        accessTab.AccessibleDescription = view == 1 ? "Selected view" : "Show access controls";
        linkTab.AccessibleDescription = view == 2 ? "Selected view" : "Show the xaiht.org link";
        if (view == 2) PaintLink();
    }

    // A button that runs one action through Run (one action at a time, then a fresh status).
    private StudioButton Act(string key, string caption, string purpose, Func<Task> action, ButtonTone tone = ButtonTone.Secondary)
        => Register(key, Command(caption, () => _ = Run(action, purpose), tone), purpose);

    // A button that only opens a window; it never waits and is never busy.
    private StudioButton Launch(string key, string caption, string purpose, Action action)
        => Register(key, Command(caption, action), purpose);

    private StudioButton Register(string key, StudioButton button, string purpose)
    {
        button.Width = Math.Max(button.Width, ButtonWidth);
        button.AccessibleName = purpose;
        tips.SetToolTip(button, purpose);
        buttons.Add(key, button);
        return button;
    }

    private static Panel Section(Control content) => Studio.Card(content, new Padding(14, Pad, 8, Pad));

    private Control BuildRuntime()
    {
        var page = Studio.Grid([-1], [OneRow, Gap, OneRow, Gap, OneRow, Gap, OneRow, Gap, OneRow, Gap, TwoRows, -1]);
        page.Controls.Add(Section(jobsLine.Build(
            Act("jobs.start", "Start", "Start the Jobs host", () => HostAction("start")),
            Act("jobs.stop", "Stop", "Stop the Jobs host", () => HostAction("stop")))), 0, 0);
        page.Controls.Add(Section(engineLine.Build(
            Act("engine.start", "Start", "Start the Torrent engine", StartEngine),
            Act("engine.stop", "Stop", "Stop the Torrent engine", () => Provider("stop")),
            Act("engine.restart", "Restart", "Restart the Torrent engine", () => Provider("restart")))), 0, 2);
        page.Controls.Add(Section(webLine.Build(
            Act("web.restart", "Restart", "Restart the REST workspace", RestartWeb),
            Act("web.open", "Open", "Open the REST workspace", () => OpenWorkspace(signIn: true)))), 0, 4);
        page.Controls.Add(Section(rcLine.Build(
            Act("rc.start", "Start", "Start Remote-Controller", () => RemoteController("start")),
            Act("rc.stop", "Stop", "Stop Remote-Controller", () => RemoteController("stop")),
            Launch("rc.settings", "Settings…", "Open Remote-Controller settings", () => RemoteControllerLauncher.Open(this)))), 0, 6);
        page.Controls.Add(Section(flowLine.Build(
            Launch("flow.open", "Open", "Open Flow Pills", OpenFlowPills),
            Act("flow.stop", "Stop", "Stop Flow Pills and its flows", StopFlowPills))), 0, 8);
        // Two equal lines around a hairline. They share what the card really has, so the DPI's
        // rounding can never push the second line out (fixed 44 + 1 + 44 did, by 2 px at 150%).
        var hand = Studio.Grid([-1], [-1, 1, -1]);
        hand.Controls.Add(handLine.Build(
            Act("handgi.start", "Start", "Start HandGI", StartHandGI),
            Act("handgi.stop", "Stop", "Stop HandGI", StopHandGI),
            Act("handgi.toggle", "Pause", "Pause hand control", ToggleHandControl)), 0, 0);
        hand.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Studio.Hairline, Margin = Padding.Empty }, 0, 1);
        hand.Controls.Add(signsLine.Build(
            Act("signs.list", "Signs…", "Choose what each hand sign does", () => ShowHandGI("signs")),
            Act("signs.camera", "Camera…", "Choose the camera HandGI uses", () => ShowHandGI("camera")),
            Act("signs.try", "Try it…", "Try your hand signs", () => ShowHandGI("try"))), 0, 2);
        page.Controls.Add(Section(hand), 0, 10);
        return page;
    }

    private Control BuildAccess()
    {
        var page = Studio.Grid([-1], [OneRow, Gap, OneRow, Gap, OneRow, -1]);
        page.Controls.Add(Section(new ServiceLine("Tlamatini account", "The workspace uses your current login.", tips, lamp: false).Build(
            Act("access.downloads", "Open downloads", "Open your downloads", () => OpenWorkspace(signIn: false)))), 0, 0);
        page.Controls.Add(Section(new ServiceLine("API credentials", "Account access and permissions", tips, lamp: false).Build(
            Act("access.issue", "Issue API credential", "Issue an API credential", Issue),
            Act("access.revoke", "Revoke access", "Revoke account access", Revoke, ButtonTone.Danger))), 0, 2);
        page.Controls.Add(Section(new ServiceLine("File erasure", "Review the exact files before approving removal.", tips, lamp: false).Build(
            Act("access.erasure", "Review erasure", "Review file erasure", ReviewDeletion))), 0, 4);
        return page;
    }

    // xaiht.org → Tlamatini-RC → Add this computer gives a key; Connect saves it here.
    private Control BuildLink()
    {
        var page = Studio.Grid([-1], [OneRow, Gap + 4, -1]);
        connect = Command("Connect…", ConnectLink, ButtonTone.Primary);
        disconnect = Command("Disconnect", DisconnectLink, ButtonTone.Danger);
        websiteAccounts = Command("Accounts…", () =>
        {
            if (XaihtLink.Shared.Identity is not { } identity) return;
            using var dialog = new XaihtAccountsForm(this, identity); dialog.ShowDialog(this);
        });
        foreach (var button in new[] { connect, websiteAccounts, disconnect }) button.Width = Math.Max(button.Width, ButtonWidth);
        page.Controls.Add(Section(linkLine.Build(connect, websiteAccounts, disconnect)), 0, 0);
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
        linkLine.Show(s.State switch
        {
            "online" => (Mood.On, "Online" + (s.Computer is null ? "" : $"   ·   {s.Computer}")),
            "connecting" => (Mood.Busy, "Connecting…"),
            "offline" => (Mood.Busy, "Offline   ·   retrying"),
            "rejected" => (Mood.Problem, "Key not accepted"),
            "error" => (Mood.Problem, "Key unreadable"),
            _ => (Mood.Off, saved ? "Waiting for XT" : "Not connected")
        });
        linkServer.Text = s.Server ?? (saved ? "saved" : "—");
        linkContact.Text = s.LastContact is { } contact ? $"{contact.LocalDateTime:HH:mm:ss}   ·   {s.Detail}" : s.Detail;
        linkMessages.Text = s.LastMessage is { } last ? $"{s.Received} received   ·   last at {last.LocalDateTime:HH:mm:ss}" : $"{s.Received} received since XT started";
        disconnect.Enabled = saved;
        websiteAccounts.Enabled = s.State == "online" && XaihtLink.Shared.Identity is not null;
    }

    // Every line says how its service is, in the same words and colors.
    private void PaintState()
    {
        jobsLine.Show(host switch
        {
            "running" => (Mood.On, "Running   ·   Tlachihuani.Jobs"),
            "stopped" => (Mood.Off, "Stopped"),
            "not_installed" => (Mood.Off, "Not installed"),
            "start_pending" => (Mood.Busy, "Starting…"),
            "stop_pending" => (Mood.Busy, "Stopping…"),
            _ => (Mood.Problem, jobsProblem.Length > 0 ? "Status unavailable   ·   " + jobsProblem : "Status unavailable")
        });
        engineLine.Show(host switch
        {
            "not_installed" => (Mood.Off, "Not installed   ·   comes with the Jobs host"),
            "running" when !catalogAvailable => (Mood.Problem, "Not answering" + (jobsProblem.Length > 0 ? "   ·   " + jobsProblem : "")),
            "running" when !enabled => (Mood.Off, "Stopped"),
            "running" when engine => (Mood.On, "Ready   ·   " + Transfers()),
            "running" => (Mood.Busy, "Getting ready…"),
            "start_pending" => (Mood.Busy, "Waiting for the Jobs host…"),
            "stopped" => (Mood.Off, "Stopped   ·   Start also starts the Jobs host"),
            _ => (Mood.Off, "Stopped")
        });
        webLine.Show(web ? (Mood.On, "Ready   ·   127.0.0.1:12064")
            : host == "running" && enabled ? (Mood.Busy, "Getting ready…")
            : host == "not_installed" ? (Mood.Off, "Not installed   ·   comes with the Jobs host")
            : (Mood.Off, "Off   ·   runs with the Torrent engine"));
        rcLine.Show(rcLook);
        flowLine.Show(flow.Running ? (Mood.On, "Open   ·   " + Flows(flow.Runs, " running", "no flows running")) : (Mood.Off, "Closed"));
        var h = hand;
        handLine.Show(h is null ? (Mood.Busy, "Checking…") : h.Word switch
        {
            "not_installed" => (Mood.Off, "Not installed"),
            "stopped" => (Mood.Off, "Stopped"),
            "starting" => (Mood.Busy, "Starting…"),
            "stopping" => (Mood.Busy, "Stopping…"),
            "watching" when h.HandControlOn => (Mood.On, "On   ·   responding to your hand signs"),
            "watching" => (Mood.Busy, "Paused   ·   hand signs are ignored"),
            "camera_off" => (Mood.Busy, "Camera off   ·   hand signs are ignored"),
            "problem" => (Mood.Problem, "Needs attention   ·   " + h.Message),
            "unreachable" => (Mood.Problem, "Not answering   ·   Stop ends it"),
            _ => (Mood.Busy, h.Message.Length > 0 ? h.Message : StatusForm.Pretty(h.Word))
        });
        signsLine.Show(Mood.Plain, h is null ? "Checking…"
            : h.Word == "not_installed" ? "Install HandGI to use hand signs"
            : h.Answering ? (h.SignsInUse == 1 ? "1 sign in use" : $"{h.SignsInUse} signs in use") + (h.Camera.Length > 0 ? "   ·   " + h.Camera : "")
            : "Choose what each sign does and which camera to use");
    }

    private string Transfers() => records == 0 ? "no transfers"
        : active == 0 ? $"none active of {records} transfers" : $"{active} active of {records} transfers";

    private static string Flows(int count, string suffix, string none) => count switch
    {
        0 => none, 1 => "1 flow" + suffix, _ => $"{count} flows" + suffix
    };
}
