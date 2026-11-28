// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (repaints the camera picture 30 times a second; UiTrace writes every click and choice, and each change is written explicitly)
using HandGI.App.Controls;
using HandGI.Core.Capture;
using HandGI.Core.Engine;
using HandGI.Core.Gestures;
using HandGI.Core.Vision;
using Tlachihuani.Contracts;
using Tlachihuani.Tray;

namespace HandGI.App;

// HandGI's window for everyone: which sign does what, which camera HandGI watches, and a place to
// try the signs. How the recognition works is not set here; that is the expert view.
internal sealed class SettingsForm : StudioForm
{
    internal static readonly GestureAction[] Choices =
        [GestureAction.None, GestureAction.VolumeUp, GestureAction.VolumeDown, GestureAction.MuteToggle, GestureAction.VolumeLever];

    internal static string Describe(GestureAction action) => action switch
    {
        GestureAction.VolumeUp => "Turns the volume up",
        GestureAction.VolumeDown => "Turns the volume down",
        GestureAction.MuteToggle => "Mutes or unmutes",
        GestureAction.VolumeLever => "Volume lever",
        _ => "Does nothing",
    };

    private static readonly Font EmojiFont = new("Segoe UI Emoji", 15f);

    // One sign per row. The window's default size shows the seven ready-made signs and two of the
    // user's own without scrolling, every hint in full.
    private const int SignRowHeight = 46;

    private readonly IHandGIHost host;
    private readonly AppController app;
    private readonly PreviewView preview = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Label seeing = Studio.Label("", Studio.Strong, Studio.Text);
    private readonly StudioButton signsTab = new("Signs", ButtonTone.Tab), cameraTab = new("Camera", ButtonTone.Tab), tryTab = new("Try it", ButtonTone.Tab);
    // The signs, one row each, as tall as their rows together; signScroll scrolls them when the window
    // is too small to show them all.
    private readonly TableLayoutPanel signList = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty };
    private readonly Panel signScroll = new() { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
    private readonly ComboBox cameraChoice = Choice("Camera");
    private readonly CheckBox mirror = Option("Show the picture like a mirror");
    private readonly CheckBox showVolume = Option("Show the volume on the screen when a sign changes it");
    private readonly CheckBox startOn = Option("Turn hand control on as soon as HandGI starts");
    private readonly CheckBox withServices = Option("Start HandGI together with Tlachihuani's services");
    private readonly ListBox recent = new()
    {
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Studio.Pane, ForeColor = Studio.Body,
        Font = Studio.Regular, IntegralHeight = false, SelectionMode = SelectionMode.None, AccessibleName = "Last signs HandGI saw",
    };
    private readonly Label tryHint = Note("");
    private readonly Control signsPage, cameraPage, tryPage;
    private readonly StudioButton teach, handControl, cameraSwitch;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    private readonly FrameReport report = new();
    private long reportSeq, previewSeq;
    private int tick;
    private bool busy, loading, selfEdit, camerasAsked, listingCameras, fitQueued;
    // Each sign's name and hint, so FitSigns can give them the lines they need.
    private readonly List<(TableLayoutPanel Names, Label Name, Label Hint)> signText = new();
    private string notice = "";
    private DateTime noticeUntil;
    private (int Sign, string What, DateTime When) lastAction;

    internal SettingsForm(IHandGIHost host)
        : base(null, "HandGI", "HandGI   /   Hand gestures", "Your hand signs", new Size(1240, 730), new Size(940, 580))
    {
        this.host = host;
        app = host.App;
        ShowInTaskbar = true;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        UiTrace.Ignore(seeing);
        UiTrace.Ignore(preview);
        HeaderCommands.Controls.Add(Command("Expert view…", host.ShowExpert));

        teach = Command("Teach a new sign…", OpenTeach, ButtonTone.Primary);
        handControl = Sized(["Turn on hand control", "Pause hand control"], ToggleHandControl);
        cameraSwitch = Sized(["Turn the camera off", "Turn the camera on"], () => _ = SwitchCameraAsync());
        signsPage = BuildSigns();
        cameraPage = BuildCamera();
        tryPage = BuildTry();

        var body = Studio.Grid([-1, 16, 380], [-1]);
        var left = Studio.Grid([-1], [38, 8, -1]);
        var tabs = Studio.Actions(signsTab, cameraTab, tryTab);
        tabs.AccessibleName = "HandGI pages";
        left.Controls.Add(tabs, 0, 0);
        var pages = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        pages.Controls.Add(signsPage);
        pages.Controls.Add(cameraPage);
        pages.Controls.Add(tryPage);
        left.Controls.Add(pages, 0, 2);
        body.Controls.Add(left, 0, 0);
        var picture = Studio.Grid([-1], [-1, 42]);
        var frame = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.Black };
        frame.Controls.Add(preview);
        picture.Controls.Add(frame, 0, 0);
        seeing.TextAlign = ContentAlignment.MiddleCenter;
        picture.Controls.Add(seeing, 0, 1);
        body.Controls.Add(Studio.Card(picture, new Padding(8)), 2, 0);
        BodyArea.Controls.Add(body);

        var close = Command("Close", Close);
        Commands.Controls.Add(close);
        Commands.Controls.Add(handControl);
        CancelButton = close;

        var all = new[] { signsTab, cameraTab, tryTab };
        for (int i = 0; i < all.Length; i++)
        {
            int view = i;
            all[i].Click += (_, _) => ShowView(view);
        }
        ShowView(0);

        mirror.CheckedChanged += (_, _) => Option(mirror.Checked, value => { app.Settings.Mirror = value; app.Engine?.SetMirror(value); }, "the mirrored picture");
        showVolume.CheckedChanged += (_, _) => Option(showVolume.Checked, value => app.Settings.ShowOsd = value, "the volume display");
        startOn.CheckedChanged += (_, _) => Option(startOn.Checked, value => app.Settings.StartArmed = value, "hand control at start");
        withServices.CheckedChanged += (_, _) => Option(withServices.Checked, value => app.Settings.StartWithServices = value, "starting with Tlachihuani's services");
        cameraChoice.SelectionChangeCommitted += async (_, _) => await UseCameraAsync();
        signList.ClientSizeChanged += (_, _) => FitSignsLater();

        app.ProfileChanged += OnProfileChanged;
        timer.Tick += (_, _) => Tick();
        Shown += (_, _) =>
        {
            LoadOptions();
            RebuildSigns();
            RefreshState();
            timer.Start();
        };
        FormClosed += (_, _) =>
        {
            timer.Stop();
            app.ProfileChanged -= OnProfileChanged;
        };
        preview.Placeholder = "Turning the camera on…";
        preview.SetClassNames(app.Profile.Classes.Select(c => c.Name));
    }

    // A drop-down list in Tlachihuani's colors. A flat list that grows keeps its old arrow button where
    // its edge was, because only the strip it gains is repainted; so it is redrawn whole instead.
    internal static ComboBox Choice(string accessibleName, Padding? margin = null)
    {
        var box = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Studio.Pane, ForeColor = Studio.Text,
            Font = Studio.Regular, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = margin ?? new Padding(0, 0, 10, 0),
            AccessibleName = accessibleName,
        };
        box.SizeChanged += (_, _) => box.Invalidate();
        return box;
    }

    private static CheckBox Option(string text) => new()
    {
        Text = text, Dock = DockStyle.Fill, ForeColor = Studio.Body, Font = Studio.Regular, FlatStyle = FlatStyle.Flat,
        Margin = Padding.Empty, UseMnemonic = false,
    };

    private static Label Note(string text)
    {
        var label = Studio.Label(text, Studio.Small, Studio.Muted);
        label.AutoEllipsis = false;
        label.TextAlign = ContentAlignment.TopLeft;
        return label;
    }

    // A button sized for the longest of the texts it can show.
    private StudioButton Sized(string[] texts, Action action)
    {
        var button = Command(texts.OrderByDescending(t => t.Length).First(), action);
        button.Text = texts[0];
        return button;
    }

    private void ShowView(int view)
    {
        signsPage.Visible = view == 0;
        cameraPage.Visible = view == 1;
        tryPage.Visible = view == 2;
        signsTab.SelectTab(view == 0);
        cameraTab.SelectTab(view == 1);
        tryTab.SelectTab(view == 2);
        if (view == 1)
        {
            LoadCameras();
            // HandGI lists the cameras when it turns one on. Started with the camera off, nothing
            // has listed them yet, so this page does it once. Listing never turns a camera on.
            if (!camerasAsked && app.Cameras.Count == 0) _ = ListCamerasAsync();
        }
    }

    internal void ShowPage(string page) => ShowView(page switch { "camera" => 1, "try" => 2, _ => 0 });

    // ------------------------------------------------------------------ signs

    private Control BuildSigns()
    {
        var page = Studio.Grid([-1], [-1, 10, 36, 40]);
        signScroll.Controls.Add(signList);
        page.Controls.Add(Studio.Card(signScroll, new Padding(10, 6, 4, 6)), 0, 0);
        page.Controls.Add(Studio.Actions(teach), 0, 2);
        page.Controls.Add(Note("The ready-made signs work right away. A sign you teach is learned on this computer; "
            + "HandGI keeps no pictures of you, only where your fingers were."), 0, 3);
        return page;
    }

    private void RebuildSigns()
    {
        if (IsDisposed) return;
        var profile = app.Profile;
        signList.SuspendLayout();
        foreach (var old in signList.Controls.Cast<Control>().ToArray()) old.Dispose();
        signList.Controls.Clear();
        signList.RowStyles.Clear();
        signText.Clear();
        signList.RowCount = profile.Classes.Count;
        // The window scaled itself for its screen when it opened (StudioForm.OnLoad). These rows
        // come later, so each one is scaled here the same way; unscaled, at 150% "On" broke in
        // two and the action names and hints were cut off.
        var scale = new SizeF(DeviceDpi / 96f, DeviceDpi / 96f);
        for (int i = 0; i < profile.Classes.Count; i++)
        {
            signList.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(SignRowHeight)));
            var row = SignRow(i);
            row.Scale(scale);
            signList.Controls.Add(row, 0, i);
        }
        signList.ResumeLayout(true);
        FitSigns();
        teach.Enabled = !busy && profile.MineCount < HandGraphs.MaxOwnGestures;
        preview.SetClassNames(profile.Classes.Select(c => c.Name));
    }

    private Control SignRow(int index)
    {
        var profile = app.Profile;
        var sign = profile.Classes[index];
        bool mine = !sign.IsReadyMade;
        var row = Studio.Grid([42, -1, 196, 56, 96], [-1]);
        var emoji = Studio.Label(sign.Emoji, EmojiFont, Studio.Text);
        emoji.TextAlign = ContentAlignment.MiddleCenter;
        row.Controls.Add(emoji, 0, 0);
        var names = Studio.Grid([-1], [-1, 22, 19, -1]);
        var name = Studio.Label(sign.Name, Studio.Strong, Studio.Text);
        string about = mine ? (profile.IsTrained ? "Taught by you" : "Taught by you · not learned yet") : sign.Hint;
        var hint = Studio.Label(about, Studio.Small, Studio.Muted);
        // Never cut short: FitSigns gives them the lines they need.
        name.AutoEllipsis = hint.AutoEllipsis = false;
        names.Controls.Add(name, 0, 1);
        names.Controls.Add(hint, 0, 2);
        signText.Add((names, name, hint));
        row.Controls.Add(names, 1, 0);
        var action = Choice($"What {sign.Name} does");
        foreach (var choice in Choices) action.Items.Add(Describe(choice));
        action.SelectedIndex = Math.Max(0, Array.IndexOf(Choices, sign.Action));
        action.SelectionChangeCommitted += (_, _) => SetAction(index, Choices[action.SelectedIndex]);
        row.Controls.Add(action, 2, 0);
        var on = Option("On");
        on.Checked = sign.Enabled;
        on.AccessibleName = $"{sign.Name} on";
        on.CheckedChanged += (_, _) => SetEnabled(index, on.Checked);
        row.Controls.Add(on, 3, 0);
        if (mine)
        {
            var remove = new StudioButton("Remove", ButtonTone.Danger) { AccessibleName = $"Remove {sign.Name}", Anchor = AnchorStyles.Left };
            remove.Click += async (_, _) => await RemoveAsync(index);
            row.Controls.Add(remove, 4, 0);
        }
        return row;
    }

    // A sign's name and hint are shown in full. When the list is too narrow for them on one line they
    // wrap and the sign's row grows; the list then scrolls, as it may when the window is small.
    private void FitSigns()
    {
        if (signText.Count == 0 || signText.Count != signList.RowStyles.Count) return;
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;
        int nameLine = LogicalToDeviceUnits(22), hintLine = LogicalToDeviceUnits(19);
        int rowHeight = LogicalToDeviceUnits(SignRowHeight), spare = rowHeight - nameLine - hintLine;
        // Twice at most: the scroll bar that taller rows bring makes the list narrower.
        for (int pass = 0, width = -1; pass < 3 && signList.ClientSize.Width != width; pass++)
        {
            width = signList.ClientSize.Width;
            signList.PerformLayout();
            using var graphics = signList.CreateGraphics();
            signList.SuspendLayout();
            for (int i = 0; i < signText.Count; i++)
            {
                var (names, name, hint) = signText[i];
                int nameHeight = Math.Max(nameLine, TextRenderer.MeasureText(graphics, name.Text, name.Font, new Size(Math.Max(1, name.Width), 10000), flags).Height);
                int hintHeight = Math.Max(hintLine, TextRenderer.MeasureText(graphics, hint.Text, hint.Font, new Size(Math.Max(1, hint.Width), 10000), flags).Height);
                names.RowStyles[1].Height = nameHeight;
                names.RowStyles[2].Height = hintHeight;
                signList.RowStyles[i].Height = Math.Max(rowHeight, nameHeight + hintHeight + spare);
            }
            signList.ResumeLayout(true);
        }
    }

    // Once Windows has finished resizing the list.
    private void FitSignsLater()
    {
        if (fitQueued || IsDisposed || !IsHandleCreated) return;
        fitQueued = true;
        BeginInvoke(() =>
        {
            fitQueued = false;
            if (!IsDisposed) FitSigns();
        });
    }

    private void SetAction(int index, GestureAction action)
    {
        var sign = app.Profile.Classes[index];
        sign.Action = action;
        Save($"\"{sign.Name}\" now: {Describe(action).ToLowerInvariant()}");
    }

    private void SetEnabled(int index, bool enabled)
    {
        var sign = app.Profile.Classes[index];
        sign.Enabled = enabled;
        Save($"\"{sign.Name}\" switched {(enabled ? "on" : "off")}");
    }

    private void Save(string change)
    {
        selfEdit = true;
        try
        {
            app.ProfileEdited();
            app.SaveProfile();
            TraceLog.I("HandGI sign: " + change);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TraceLog.E("HandGI could not save the signs", ex);
            ShowNotice("The change works now, but it could not be saved: " + ex.Message);
        }
        finally { selfEdit = false; }
    }

    private async Task RemoveAsync(int index)
    {
        if (busy) return;
        var profile = app.Profile;
        var sign = profile.Classes[index];
        if (!ConfirmForm.Ask(this, $"Remove \"{sign.Name}\"?", "HandGI forgets this sign and how you showed it. You can teach it again later.", "Remove")) return;
        profile.RemoveMine(index);
        Save($"\"{sign.Name}\" removed");
        RebuildSigns();
        if (profile.MineCount > 0) await LearnAgainAsync();
    }

    // Removing a sign means HandGI learns the remaining ones of the user again.
    private async Task LearnAgainAsync()
    {
        busy = true;
        teach.Enabled = false;
        ShowNotice("HandGI is updating your other signs…", 120);
        try
        {
            var outcome = await SignLearning.LearnAsync(app, new Progress<double>(p => ShowNotice($"HandGI is updating your other signs… {p:P0}", 120)));
            ShowNotice(outcome.Message);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TraceLog.E("HandGI could not update the user's signs", ex);
            ShowNotice("HandGI could not update your signs: " + ex.Message);
        }
        finally
        {
            busy = false;
            RebuildSigns();
        }
    }

    private void OpenTeach()
    {
        if (busy) return;
        if (app.Profile.MineCount >= HandGraphs.MaxOwnGestures)
        {
            ShowNotice($"HandGI learns up to {HandGraphs.MaxOwnGestures} signs of your own. Remove one first.");
            return;
        }
        using (var form = new TeachForm(host)) form.ShowDialog(this);
        RebuildSigns();
        RefreshState();
    }

    private void OnProfileChanged()
    {
        if (selfEdit || IsDisposed || !IsHandleCreated) return;
        BeginInvoke(RebuildSigns);
    }

    // ------------------------------------------------------------------ camera

    private Control BuildCamera()
    {
        var page = Studio.Grid([-1], [28, 40, 14, 30, 30, 30, 30, 14, 40, -1]);
        page.Controls.Add(Studio.Label("The camera HandGI watches", Studio.Strong, Studio.Text), 0, 0);
        page.Controls.Add(cameraChoice, 0, 1);
        page.Controls.Add(mirror, 0, 3);
        page.Controls.Add(showVolume, 0, 4);
        page.Controls.Add(startOn, 0, 5);
        page.Controls.Add(withServices, 0, 6);
        page.Controls.Add(Studio.Actions(cameraSwitch, Command("Look for cameras again", () => _ = RefreshCamerasAsync())), 0, 8);
        page.Controls.Add(Note("HandGI works only on this computer. It never saves or sends pictures of you."), 0, 9);
        return page;
    }

    private void LoadOptions()
    {
        loading = true;
        mirror.Checked = app.Settings.Mirror;
        showVolume.Checked = app.Settings.ShowOsd;
        startOn.Checked = app.Settings.StartArmed;
        withServices.Checked = app.Settings.StartWithServices;
        loading = false;
    }

    private void Option(bool value, Action<bool> apply, string what)
    {
        if (loading) return;
        apply(value);
        app.Settings.Save(app.Log);
        TraceLog.I($"HandGI setting: {what} {(value ? "on" : "off")}");
    }

    private sealed record CameraItem(CameraInfo Camera)
    {
        public override string ToString() => Camera.DisplayName;
    }

    private void LoadCameras()
    {
        loading = true;
        cameraChoice.Items.Clear();
        var current = app.SavedCamera();
        foreach (var camera in app.Cameras)
        {
            int at = cameraChoice.Items.Add(new CameraItem(camera));
            if (current is not null && camera.GroupId == current.GroupId && camera.SourceId == current.SourceId) cameraChoice.SelectedIndex = at;
        }
        cameraChoice.Enabled = cameraChoice.Items.Count > 0;
        if (cameraChoice.Items.Count == 0)
        {
            cameraChoice.Items.Add(listingCameras ? "Looking for cameras…" : "No camera found");
            cameraChoice.SelectedIndex = 0;
        }
        loading = false;
    }

    private async Task UseCameraAsync()
    {
        if (loading || busy || cameraChoice.SelectedItem is not CameraItem item) return;
        busy = true;
        ShowNotice($"Switching to {item.Camera.DisplayName}…", 30);
        try
        {
            var formats = await CameraCatalog.ListFormatsAsync(item.Camera);
            TraceLog.I($"HandGI switches to the camera {item.Camera.DisplayName}");
            await app.StartAsync(item.Camera, CameraFormat.Recommend(formats));
            ShowNotice($"HandGI now watches {item.Camera.DisplayName}.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TraceLog.E("HandGI could not switch cameras", ex);
            ShowNotice("That camera could not be turned on: " + ex.Message);
        }
        finally
        {
            busy = false;
            RefreshState();
        }
    }

    private async Task SwitchCameraAsync()
    {
        if (busy) return;
        busy = true;
        try
        {
            if (app.Engine?.IsRunning == true) await host.StopCameraAsync();
            else await host.StartCameraAsync();
        }
        finally
        {
            busy = false;
            RefreshState();
        }
    }

    private async Task RefreshCamerasAsync()
    {
        await app.RefreshCamerasAsync();
        LoadCameras();
        ShowNotice(app.Cameras.Count == 1 ? "1 camera found." : $"{app.Cameras.Count} cameras found.");
    }

    private async Task ListCamerasAsync()
    {
        camerasAsked = listingCameras = true;
        LoadCameras();
        try { await app.RefreshCamerasAsync(); }
        finally { listingCameras = false; }
        if (!IsDisposed) LoadCameras();
    }

    // ------------------------------------------------------------------ try

    private Control BuildTry()
    {
        var page = Studio.Grid([-1], [28, 46, 10, 24, -1, 10, 56]);
        page.Controls.Add(Studio.Label("Try your signs", Studio.Strong, Studio.Text), 0, 0);
        page.Controls.Add(Note("Show a sign to the camera and hold it a moment. Under the picture HandGI says what it sees; "
            + "here it lists what it did."), 0, 1);
        page.Controls.Add(Studio.Label("Last signs", Studio.Small, Studio.Muted), 0, 3);
        page.Controls.Add(Studio.Card(recent, new Padding(10, 6, 6, 6)), 0, 4);
        page.Controls.Add(tryHint, 0, 6);
        return page;
    }

    internal void OnGesture(GestureEvent gesture)
    {
        if (IsDisposed || gesture.Kind == GestureEventKind.Ended) return;
        var classes = app.Profile.Classes;
        string emoji = gesture.ClassIndex >= 0 && gesture.ClassIndex < classes.Count ? classes[gesture.ClassIndex].Emoji : "✋";
        string what = gesture.Kind == GestureEventKind.Started ? "seen"
            : gesture.DryRun ? $"would do: {gesture.Description} (hand control paused)" : gesture.Description;
        if (gesture.Kind == GestureEventKind.Action)
        {
            // Holding a sign repeats its action; list it once.
            if (lastAction.Sign == gesture.ClassIndex && lastAction.What == what && (gesture.When - lastAction.When).TotalSeconds < 2) return;
            lastAction = (gesture.ClassIndex, what, gesture.When);
        }
        recent.Items.Insert(0, $"{gesture.When:HH:mm:ss}    {emoji}  {gesture.ClassName}  ·  {what}");
        while (recent.Items.Count > 14) recent.Items.RemoveAt(recent.Items.Count - 1);
    }

    // ------------------------------------------------------------------ live state

    internal void OnEngineState(EngineState state, string message) => RefreshState();

    private void Tick()
    {
        var engine = app.Engine;
        if (engine is not null && engine.IsRunning)
        {
            engine.TryCopyPreview(ref previewSeq, preview.UpdateFrame);
            if (engine.TryGetReport(report, ref reportSeq))
            {
                preview.UpdateReport(report);
                seeing.Text = Seeing(report);
            }
        }
        if (++tick % 15 == 0) RefreshState();
    }

    private string Seeing(FrameReport frame)
    {
        if (!frame.HasHand) return "Show your hand to the camera";
        var classes = app.Profile.Classes;
        if (frame.ActiveClass < 0 || frame.ActiveClass >= classes.Count) return "I see your hand";
        var sign = classes[frame.ActiveClass];
        return $"{sign.Emoji}  {sign.Name}  ·  {Describe(sign.Action)}";
    }

    internal void RefreshState()
    {
        if (IsDisposed) return;
        var engine = app.Engine;
        bool running = engine?.IsRunning == true;
        bool on = host.HandControlOn;
        handControl.Text = on ? "Pause hand control" : "Turn on hand control";
        handControl.Enabled = engine is not null;
        cameraSwitch.Text = running ? "Turn the camera off" : "Turn the camera on";
        cameraSwitch.Enabled = !busy && !host.Starting;
        string status = host.Starting ? "Turning the camera on…"
            : running ? $"Watching with {engine!.ActiveCameraText()}   ·   hand control {(on ? "on" : "paused")}   ·   Ctrl+Alt+H switches it"
            : host.ProblemText.Length > 0 ? host.ProblemText
            : "The camera is off.";
        FooterNote.Text = notice.Length > 0 && DateTime.UtcNow < noticeUntil ? notice : status;
        tryHint.Text = on
            ? "Hand control is on: your signs change the volume."
            : "Hand control is paused: HandGI shows what it sees and changes nothing. Turn it on with the button below, or press Ctrl+Alt+H.";
        if (!running)
        {
            preview.Placeholder = host.Starting ? "Turning the camera on…" : host.ProblemText.Length > 0 ? host.ProblemText : "The camera is off.";
            preview.ClearFrame();
            seeing.Text = "";
        }
    }

    private void ShowNotice(string text, int seconds = 8)
    {
        notice = text;
        noticeUntil = DateTime.UtcNow.AddSeconds(seconds);
        if (!IsDisposed) FooterNote.Text = text;
    }

    private void ToggleHandControl()
    {
        host.SetHandControl(!host.HandControlOn, "HandGI window");
        RefreshState();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}
