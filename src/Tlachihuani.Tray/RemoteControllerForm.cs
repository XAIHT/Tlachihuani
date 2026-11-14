// Tlachihuani — Angela López Mendoza · XAIHT
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tlachihuani.RC;
using Tlachihuani.Windows;

namespace Tlachihuani.Tray;

internal sealed class RemoteControllerForm : StudioForm
{
    private readonly CancellationTokenSource closing = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 3000 };
    private readonly SemaphoreSlim gate = new(1);
    private readonly Label scmLine = TextLine("Checking Windows service…"), healthLine = TextLine("Waiting for a current observation…"), endpointLine = TextLine(""), impactLine = TextLine("");
    private readonly StudioButton start, stop, restart, open, save, invite, approve, decline, revoke;
    private readonly ComboBox accounts = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 460 };
    private readonly Label accountHint = TextLine("Choose the same account name you use to sign in to Tlamatini.");
    private readonly CheckedListBox scopes = new() { Height = 150, Width = 560, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle };
    private readonly ListBox pending = new() { Width = 580, Height = 100 }, devices = new() { Width = 580, Height = 120 };
    private readonly NumericUpDown port = new() { Minimum = 1024, Maximum = 65535, Value = 12481, Width = 120 };
    private readonly CheckBox include = new() { Text = "Include Remote-Controller in Start Services", AutoSize = true };
    private readonly TextBox activity = new() { Multiline = true, ReadOnly = true, Width = 580, Height = 280, ScrollBars = ScrollBars.Vertical };
    private bool busy, polling;
    private string observed = "unknown";
    private JsonObject? status;
    private bool configLoaded;

    internal RemoteControllerForm(Icon icon) : base(icon, "Tlachihuani · Remote-Controller", "Tlachihuani   /   Independent service plane", "Tlachihuani-RC Server", new(820, 670), new(680, 560))
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, Font = Studio.Regular };
        var runtime = Page(tabs, "Runtime");
        runtime.Controls.Add(TextLine("Connect on this PC\n1. Start the server below.  2. Enable your Tlamatini account.\n3. Invite and approve this browser in Access.  4. Open RC client and connect.\nConnections from phones and other computers are not available yet."));
        runtime.Controls.AddRange([scmLine, healthLine, endpointLine, impactLine]);
        start = Command("Start server", () => _ = Change("start"), ButtonTone.Primary);
        stop = Command("Stop server…", () => _ = Change("stop"));
        restart = Command("Restart…", () => _ = Change("restart"));
        open = Command("Open RC client", OpenClient);
        runtime.Controls.Add(Actions(start, stop, restart, open));
        runtime.Controls.Add(TextLine("Stopping RC disconnects its clients and suspends its scheduler.\nTlamatini and Torrenter keep their own lifetimes."));
        runtime.Controls.Add(TextLine("Manual startup. Opening XT does not start this service."));

        var access = Page(tabs, "Access");
        access.Controls.Add(TextLine("Enable your account in Tlamatini → Panels → Tlachihuani / Remote-Controller."));
        access.Controls.Add(TextLine("Tlamatini account"));
        accounts.AccessibleName = "Tlamatini account";
        access.Controls.Add(accounts);
        access.Controls.Add(accountHint);
        foreach (var scope in new[] { new Permission("monitor", "View status"), new Permission("chat", "Conversation"), new Permission("workspace", "Full mobile Tlamatini workspace"), new Permission("launch", "Start Tlamatini"), new Permission("stop", "Stop Tlamatini (all accounts in this edition)"), new Permission("schedules", "Manage schedules") })
            scopes.Items.Add(scope, scope.Id is "monitor" or "chat");
        access.Controls.Add(scopes);
        access.Controls.Add(TextLine("Full mobile workspace includes this account's chat, tools, files and settings.\nStarting and stopping Tlamatini require their separate permissions."));
        invite = Command("Create invitation…", () => _ = Mutate(CreateInvitation), ButtonTone.Primary);
        access.Controls.Add(invite);
        access.Controls.Add(TextLine("First enrollment only. Compare the device fingerprint locally before approval.\nNo recurring pairing code is required."));
        access.Controls.Add(TextLine("Pending device requests")); access.Controls.Add(pending);
        approve = Command("Review and approve…", () => _ = Mutate(Approve));
        decline = Command("Decline", () => _ = Mutate(Decline));
        access.Controls.Add(Actions(approve, decline));

        var connections = Page(tabs, "Connections");
        connections.Controls.Add(TextLine("Trusted devices and their account-scoped permissions")); connections.Controls.Add(devices);
        revoke = Command("Revoke selected device…", () => _ = Mutate(Revoke), ButtonTone.Danger);
        connections.Controls.Add(revoke);
        connections.Controls.Add(TextLine("Revocation ends this device's sessions and pending requests.\nA new local approval is required before it can connect again."));

        var network = Page(tabs, "Network");
        network.Controls.Add(TextLine("Connection profile: this computer only\nProtected connections from other devices are not available in this version."));
        network.Controls.Add(TextLine("Loopback HTTP port")); network.Controls.Add(port); network.Controls.Add(include);
        save = Command("Save settings…", () => _ = Mutate(SaveConfiguration)); network.Controls.Add(save);
        network.Controls.Add(TextLine("Listener changes take effect after an explicit restart.\nNo firewall or router changes are made by this panel. Torrenter's port 12064 is reserved."));

        var events = Page(tabs, "Activity"); events.Controls.Add(TextLine("Local observations and control results. No credentials or conversation text.")); events.Controls.Add(activity);
        BodyArea.Controls.Add(tabs);
        var close = Command("Close", Close); CancelButton = close;
        Commands.Controls.AddRange([close, Command("Refresh", () => _ = RefreshAsync())]);
        foreach (var control in new Control[] { accounts, scopes, pending, devices, port, include, activity })
        { control.BackColor = Studio.Pane; control.ForeColor = Studio.Text; control.Font = Studio.Regular; }
        pending.SelectedIndexChanged += (_, _) => UpdateActions(); devices.SelectedIndexChanged += (_, _) => UpdateActions(); accounts.SelectedIndexChanged += (_, _) => UpdateActions();
        timer.Tick += async (_, _) => await RefreshAsync();
        Shown += async (_, _) => { timer.Start(); await RefreshAsync(); };
        FormClosed += (_, _) => { timer.Stop(); closing.Cancel(); };
        UpdateActions();
    }
    private static Label TextLine(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(590, 0), ForeColor = Studio.Body, Font = Studio.Regular, Margin = new Padding(0, 0, 0, 14), UseMnemonic = false };
    private static FlowLayoutPanel Page(TabControl tabs, string title)
    {
        var page = new TabPage(title) { BackColor = Studio.Surface, Padding = new Padding(16) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Studio.Surface };
        void Fit()
        {
            if (!flow.IsHandleCreated || flow.ClientSize.Width < 100) return;
            var available = Math.Max(80, flow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
            foreach (Control child in flow.Controls)
            {
                if (child is Label or CheckBox or FlowLayoutPanel) child.MaximumSize = new Size(available, 0);
                if (child is ListBox or ComboBox or TextBox) child.Width = Math.Min(flow.LogicalToDeviceUnits(580), available);
            }
        }
        flow.SizeChanged += (_, _) => Fit();
        flow.VisibleChanged += (_, _) => Fit();
        page.Controls.Add(flow); tabs.TabPages.Add(page); return flow;
    }
    private static FlowLayoutPanel Actions(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(590, 0), WrapContents = true, Margin = new Padding(0, 0, 0, 14) };
        panel.Controls.AddRange(controls); return panel;
    }
    private async Task<JsonObject> Call(JsonObject request)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(closing.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        return await RcEndpoint.CallInstalledAsync(request, deadline.Token);
    }
    private async Task RefreshAsync()
    {
        if (busy || polling || closing.IsCancellationRequested || !gate.Wait(0)) return;
        polling = true;
        try
        {
            observed = await Task.Run(() => ScmBackend.ForRemoteController().Query().State, closing.Token);
            status = null;
            if (observed == "running") status = await Call(new() { ["op"] = "status" });
            if (closing.IsCancellationRequested) return;
            scmLine.Text = "Windows service: " + StatusForm.Pretty(observed);
            healthLine.Text = status is null ? observed == "stopped" ? "Remote access is stopped." : observed == "not_installed" ? "Install Remote-Controller to enable its controls." : "Application health is unavailable." : "RC health: " + (string?)status["health"] + ((string?)status["error"] is string error ? " · " + error : "");
            endpointLine.Text = status is null ? "" : "Client address: " + (string?)status["origin"];
            impactLine.Text = status is null ? "" : $"{status["sessions"]} sessions · {status["pending_operations"]} pending operations · {status["schedules"]} enabled schedules";
            FooterNote.Text = "Observed " + DateTime.Now.ToString("HH:mm:ss");
            if (status is not null)
            {
                if (!configLoaded)
                {
                    var config = (await Call(new() { ["op"] = "configuration" })).Deserialize<RcConfiguration>(RcProtocol.Json)!;
                    port.Value = config.Port; include.Checked = config.IncludeInStartServices; configLoaded = true;
                }
                Fill(accounts, (await Call(new() { ["op"] = "accounts" }))["accounts"]!.AsArray(), AccountLabel);
                Fill(pending, (await Call(new() { ["op"] = "pending-enrollments" }))["enrollments"]!.AsArray(), x => (string?)x["name"] ?? "Device");
                Fill(devices, (await Call(new() { ["op"] = "devices" }))["devices"]!.AsArray(), x => $"{x["name"]} · {((bool?)x["revoked"] == true ? "Revoked" : string.Join(", ", x["scopes"]!.AsArray().Select(s => (string?)s)))}");
            }
        }
        catch (OperationCanceledException) when (closing.IsCancellationRequested) { }
        catch (Exception ex) when (!closing.IsCancellationRequested && ex is not OutOfMemoryException)
        {
            status = null;
            scmLine.Text = "Windows service: " + StatusForm.Pretty(observed);
            healthLine.Text = "Health unavailable. Last observation may be stale.";
            FooterNote.Text = "Refresh failed · " + ex.GetType().Name;
        }
        finally { polling = false; gate.Release(); if (!closing.IsCancellationRequested) UpdateActions(); }
    }
    private static void Fill(ListControl list, JsonArray rows, Func<JsonNode, string> label)
    {
        // A status poll must not collapse the user's open account dropdown.
        if (list is ComboBox { DroppedDown: true }) return;
        var old = list is ComboBox combo ? combo.SelectedItem as Item : ((ListBox)list).SelectedItem as Item;
        var items = rows.Select(row => new Item((string?)row!["id"] ?? "", label(row), row.AsObject())).ToArray();
        if (list is ComboBox) Array.Sort(items, (left, right) =>
        {
            var state = AccountRank(left.Row).CompareTo(AccountRank(right.Row));
            return state != 0 ? state : StringComparer.CurrentCultureIgnoreCase.Compare(left.Label, right.Label);
        });
        var current = list is ComboBox existingCombo ? existingCombo.Items.Cast<Item>().ToArray() : ((ListBox)list).Items.Cast<Item>().ToArray();
        if (current.Length == items.Length && current.Zip(items).All(pair => pair.First.Id == pair.Second.Id
            && pair.First.Label == pair.Second.Label && JsonNode.DeepEquals(pair.First.Row, pair.Second.Row))) return;
        if (list is ComboBox c) { c.Items.Clear(); c.Items.AddRange(items); c.SelectedIndex = Array.FindIndex(items, x => x.Id == old?.Id); if (c.SelectedIndex < 0 && items.Length > 0) c.SelectedIndex = 0; }
        else { var box = (ListBox)list; box.Items.Clear(); box.Items.AddRange(items); box.SelectedIndex = Array.FindIndex(items, x => x.Id == old?.Id); }
    }
    private static int AccountRank(JsonObject row) => (string?)row["application_state"] switch { "ready" => 0, "loading" => 1, _ => 2 };
    private static string AccountLabel(JsonNode row)
    {
        var edition = (string?)row["edition"] == "spanish" ? "Spanish" : "English";
        var state = (string?)row["application_state"] switch { "ready" => "Ready", "loading" => "Connected", _ => "App not connected" };
        return $"{row["display_name"]} · {edition} · {state}";
    }
    private void UpdateActions()
    {
        bool idle = !busy, ready = status is not null && observed == "running";
        start.Enabled = idle && observed == "stopped";
        stop.Enabled = restart.Enabled = idle && observed is "running" or "paused";
        open.Enabled = idle && (bool?)status?["listener"] == true;
        save.Enabled = idle && ready;
        invite.Enabled = idle && ready && accounts.SelectedItem is Item;
        accounts.Enabled = idle && ready && accounts.Items.Count > 0;
        accountHint.Text = !ready ? "Start the RC server in Runtime to load your accounts."
            : accounts.Items.Count == 0 ? "No accounts enabled yet. Open Tlamatini, sign in, then enable your account in Panels → Tlachihuani / Remote-Controller. Return here and click Refresh."
            : accounts.SelectedItem is Item selected && AccountRank(selected.Row) == 2
                ? "This account is not connected to its Tlamatini app. Open that edition and enable the account there. If a name appears twice, choose the Ready or Connected entry."
                : "Choose the account you use to sign in to Tlamatini. Ready and Connected accounts are available; the first message may take longer while Tlamatini prepares the conversation.";
        approve.Enabled = decline.Enabled = idle && ready && pending.SelectedItem is Item;
        revoke.Enabled = idle && ready && devices.SelectedItem is Item item && (bool?)item.Row["revoked"] != true;
    }
    private async Task Mutate(Func<Task> action)
    {
        if (busy || closing.IsCancellationRequested) return;
        busy = true; UpdateActions();
        try { await gate.WaitAsync(closing.Token); try { await action(); } finally { gate.Release(); } }
        catch (OperationCanceledException) when (closing.IsCancellationRequested) { }
        catch (Exception ex) when (!closing.IsCancellationRequested && ex is not OutOfMemoryException) { ServiceDialogs.Notice(this, "Remote-Controller", ex.Message); Record("Action failed: " + ex.GetType().Name); }
        finally { busy = false; if (!closing.IsCancellationRequested) await RefreshAsync(); }
    }
    private Task Change(string action) => Mutate(async () =>
    {
        if (action != "start" && !ServiceDialogs.Confirm(this, action == "stop" ? "Stop Remote-Controller?" : "Restart Remote-Controller?", impactLine.Text + "\nRemote clients will disconnect and RC schedules will pause. Tlamatini and Torrenter keep running.", action == "stop" ? "Stop server" : "Restart server")) return;
        await RcLifecycle.ExecuteAsync(action, closing.Token);
        Record("Windows confirmed RC " + action + ".");
    });
    private void OpenClient()
    {
        var value = (string?)status?["origin"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || !url.IsLoopback || url.Scheme != "http" || url.UserInfo.Length != 0) return;
        try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { ServiceDialogs.Notice(this, "RC client", ex.Message); }
    }
    private async Task CreateInvitation()
    {
        if (accounts.SelectedItem is not Item account) return;
        var selected = scopes.CheckedItems.Cast<Permission>().Select(x => x.Id).ToArray();
        var result = await Call(new() { ["op"] = "invite", ["binding_id"] = account.Id, ["scopes"] = JsonSerializer.SerializeToNode(selected, RcProtocol.Json) });
        ServiceDialogs.ShowRcInvitation(this, (string?)result["invitation"] ?? throw new InvalidDataException("Invitation missing."));
        Record("One-use enrollment invitation created; expires in five minutes.");
    }
    private async Task Approve()
    {
        if (pending.SelectedItem is not Item item) return;
        var fingerprint = (string?)item.Row["fingerprint"] ?? "";
        if (!ServiceDialogs.Confirm(this, "Approve this exact device?", item.Label + "\n\nCompare the entire fingerprint in the RC client:\n" + fingerprint + "\n\nPermissions: " + string.Join(", ", item.Row["scopes"]!.AsArray().Select(x => (string?)x)), "Approve device")) return;
        await Call(new() { ["op"] = "approve", ["enrollment_id"] = item.Id, ["fingerprint"] = fingerprint });
        Record("Device approved for its selected account and permissions.");
    }
    private async Task Decline() { if (pending.SelectedItem is Item item) { await Call(new() { ["op"] = "decline", ["enrollment_id"] = item.Id }); Record("Device request declined."); } }
    private async Task Revoke()
    {
        if (devices.SelectedItem is not Item item || !ServiceDialogs.Confirm(this, "Revoke device access?", item.Label + "\nIts sessions and unexecuted requests will end. New local enrollment is required to reconnect.", "Revoke device")) return;
        await Call(new() { ["op"] = "revoke", ["device_id"] = item.Id }); Record("Device revoked.");
    }
    private async Task SaveConfiguration()
    {
        var config = new RcConfiguration { Port = (int)port.Value, PublicOrigin = $"http://127.0.0.1:{port.Value}", IncludeInStartServices = include.Checked };
        config.Validate();
        if (!ServiceDialogs.Confirm(this, "Save RC settings?", $"Loopback port {config.Port}. Include in Start Services: {config.IncludeInStartServices}.\nChanges apply after you restart Remote-Controller.", "Save settings")) return;
        await Call(new() { ["op"] = "configure", ["configuration"] = JsonSerializer.SerializeToNode(config, RcProtocol.Json) }); Record("Settings saved. Explicit restart required.");
    }
    private void Record(string message) { activity.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine); }
    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); closing.Dispose(); } base.Dispose(disposing); }
    private sealed record Item(string Id, string Label, JsonObject Row) { public override string ToString() => Label; }
    private sealed record Permission(string Id, string Label) { public override string ToString() => Label; }
}
