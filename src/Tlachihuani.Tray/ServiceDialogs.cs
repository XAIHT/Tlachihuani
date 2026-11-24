// Tlachihuani — Angela López Mendoza · XAIHT
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Tlachihuani.Tray;

internal sealed class ServiceDialogs : StudioForm
{
    private ServiceDialogs(Form? owner, string caption, string section, string title, Size size)
        : base(owner?.Icon, caption, section, title, size, size)
    {
        MaximizeBox = false;
        if (owner?.Visible != true) { StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = true; }
    }

    private StudioButton Choice(string caption, DialogResult result, ButtonTone tone = ButtonTone.Secondary)
    {
        var button = new StudioButton(caption, tone) { DialogResult = result };
        Commands.Controls.Add(button); return button;
    }

    private DialogResult Open(Form? owner) => owner?.Visible == true ? ShowDialog(owner) : ShowDialog();

    internal static void Notice(Form? owner, string title, string message)
    {
        using var dialog = new ServiceDialogs(owner, title, "Tlachihuani   /   Service notice", title, new(530, 236));
        dialog.BodyArea.Controls.Add(ReadText(message, "Message"));
        var ok = dialog.Choice("OK", DialogResult.OK, ButtonTone.Primary);
        dialog.AcceptButton = dialog.CancelButton = ok; dialog.Open(owner);
    }

    internal static void Failure(Form? owner, ServiceErrorReport report)
    {
        using var dialog = new ServiceErrorForm(owner, report);
        if (owner?.Visible == true) dialog.ShowDialog(owner); else dialog.ShowDialog();
    }

    internal static bool Confirm(Form owner, string title, string message, string action)
    {
        using var dialog = new ServiceDialogs(owner, "Tlachihuani · Confirm", "Tlachihuani   /   Confirmation", title, new(540, 240));
        dialog.BodyArea.Controls.Add(ReadText(message.Trim(), "Confirmation details"));
        dialog.Choice(action, DialogResult.Yes, ButtonTone.Danger);
        var cancel = dialog.Choice("Cancel", DialogResult.Cancel);
        dialog.AcceptButton = dialog.CancelButton = cancel; dialog.ActiveControl = cancel;
        return dialog.Open(owner) == DialogResult.Yes;
    }

    private static RichTextBox ReadText(string text, string name) => new()
    {
        Text = text, AccessibleName = name, ReadOnly = true, DetectUrls = false, WordWrap = true,
        ScrollBars = RichTextBoxScrollBars.Vertical, BorderStyle = BorderStyle.None,
        BackColor = Studio.Surface, ForeColor = Studio.Body, Font = Studio.Regular, Dock = DockStyle.Fill, Margin = Padding.Empty
    };

    internal static (string Account, string Scope)? RequestCredential(Form owner)
    {
        using var dialog = new ServiceDialogs(owner, "Torrenter · API access", "Torrenter   /   Access", "Issue API credential", new(560, 386));
        var form = Studio.Grid([-1], [23, 34, 9, 23, 102, -1]);
        var account = new TextBox { PlaceholderText = "Use my Windows profile" };
        var choices = Studio.Grid([-1], [-1, -1, -1]);
        choices.AccessibleName = "Permissions";
        var permissions = new[] { ("Read only", "read"), ("Read, submit and control transfers", "read submit control"),
            ("Read, submit, control, remove and erase", "read submit control remove erase") };
        var options = permissions.Select(p => new RadioButton { Text = p.Item1, Tag = p.Item2, Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat, ForeColor = Studio.Body, Margin = Padding.Empty }).ToArray();
        for (int i = 0; i < options.Length; i++) choices.Controls.Add(options[i], 0, i);
        options[0].Checked = true;
        form.Controls.Add(Studio.Label("Application account", Studio.Small), 0, 0); form.Controls.Add(Studio.Field(account, "Application account"), 0, 1);
        form.Controls.Add(Studio.Label("Permissions", Studio.Small), 0, 3);
        form.Controls.Add(Studio.Card(choices, new Padding(10, 3, 10, 3)), 0, 4);
        form.Controls.Add(Studio.Label("The credential expires in one hour.", Studio.Small, Studio.Muted), 0, 5);
        dialog.BodyArea.Controls.Add(form);
        var issue = dialog.Choice("Issue token", DialogResult.OK, ButtonTone.Primary);
        dialog.AcceptButton = issue; dialog.CancelButton = dialog.Choice("Cancel", DialogResult.Cancel);
        dialog.ActiveControl = account;
        return dialog.Open(owner) == DialogResult.OK ? (account.Text.Trim(), (string)options.Single(o => o.Checked).Tag!) : null;
    }

    // Services → xaiht.org → Connect…: the website and the computer key that
    // xaiht.org → Tlamatini-RC → Add this computer shows once.
    internal static XaihtLinkSettings? RequestXaihtLink(Form owner, string server)
    {
        using var dialog = new ServiceDialogs(owner, "Tlachihuani · xaiht.org link", "Tlachihuani   /   xaiht.org", "Connect to xaiht.org", new(580, 352));
        var form = Studio.Grid([-1], [23, 34, 9, 23, 34, -1]);
        var website = new TextBox { Text = server };
        var key = new TextBox { UseSystemPasswordChar = true, PlaceholderText = "xrc_…" };
        form.Controls.Add(Studio.Label("Website", Studio.Small), 0, 0); form.Controls.Add(Studio.Field(website, "Website"), 0, 1);
        form.Controls.Add(Studio.Label("Computer key", Studio.Small), 0, 3); form.Controls.Add(Studio.Field(key, "Computer key"), 0, 4);
        var help = Studio.Label("Sign in at xaiht.org, open Tlamatini-RC and choose Add this computer. Paste the key it shows here. "
            + "The key is kept for your Windows account only.", Studio.Small, Studio.Muted);
        help.AutoEllipsis = false; help.TextAlign = ContentAlignment.TopLeft; help.Padding = new Padding(0, 6, 0, 0);
        form.Controls.Add(help, 0, 5);
        dialog.BodyArea.Controls.Add(form);
        var save = dialog.Choice("Connect", DialogResult.None, ButtonTone.Primary);
        var cancel = dialog.Choice("Cancel", DialogResult.Cancel);
        XaihtLinkSettings? result = null;
        save.Click += (_, _) =>
        {
            var normalized = XaihtLinkStore.NormalizeServer(website.Text);
            var value = key.Text.Trim();
            if (normalized is null) { dialog.FooterNote.Text = "Use an https:// website address, such as https://xaiht.org."; website.Focus(); return; }
            if (!XaihtLinkStore.LooksLikeKey(value)) { dialog.FooterNote.Text = "That is not a xaiht.org computer key (it starts with xrc_)."; key.Focus(); return; }
            result = new XaihtLinkSettings(normalized, value);
            dialog.DialogResult = DialogResult.OK;
        };
        dialog.AcceptButton = save; dialog.CancelButton = cancel; dialog.ActiveControl = key;
        return dialog.Open(owner) == DialogResult.OK ? result : null;
    }

    internal static void ShowCredential(Form owner, string token)
        => ShowSecret(owner, token, false);

    internal static void ShowRcInvitation(Form owner, string token)
        => ShowSecret(owner, token, true);

    private static void ShowSecret(Form owner, string token, bool invitation)
    {
        using var dialog = new ServiceDialogs(owner,
            invitation ? "Remote-Controller · Invitation" : "Torrenter · Copy credential",
            invitation ? "Remote-Controller   /   Access" : "Torrenter   /   Access",
            invitation ? "One-use invitation" : "API credential", new(566, 257));
        var form = Studio.Grid([-1], [24, 34, -1]);
        var field = new TextBox { Text = token, ReadOnly = true, UseSystemPasswordChar = true };
        form.Controls.Add(Studio.Label(invitation ? "Enrollment invitation" : "Access token", Studio.Small), 0, 0);
        form.Controls.Add(Studio.Field(field, invitation ? "One-use enrollment invitation" : "Issued access token"), 0, 1);
        form.Controls.Add(Studio.Label(invitation
            ? "Expires in five minutes. Paste into the RC client, then compare its fingerprint before approving."
            : "Copy this token before closing the window.", Studio.Small, Studio.Muted), 0, 2);
        dialog.BodyArea.Controls.Add(form);
        var copy = dialog.Command(invitation ? "Copy invitation" : "Copy token", () =>
        {
            try { Clipboard.SetText(token); dialog.FooterNote.Text = "Copied"; }
            catch (ExternalException) { dialog.FooterNote.Text = "Clipboard busy"; }
        }, ButtonTone.Primary);
        var reveal = new StudioButton(invitation ? "Show invitation" : "Show token");
        reveal.Click += (_, _) =>
        {
            field.UseSystemPasswordChar = !field.UseSystemPasswordChar;
            reveal.Text = (field.UseSystemPasswordChar ? "Show " : "Hide ") + (invitation ? "invitation" : "token");
        };
        dialog.Commands.Controls.AddRange([copy, reveal]); dialog.AcceptButton = copy; dialog.CancelButton = dialog.Choice("Close", DialogResult.Cancel);
        dialog.Open(owner);
    }

    internal static string[]? RcDevicePermissions(Form owner, string device, string[] current)
    {
        using var dialog = new ServiceDialogs(owner, "Remote-Controller · Permissions",
            "Remote-Controller   /   Approved browser", "Change browser permissions", new(620, 520));
        var layout = Studio.Grid([-1], [62, -1, 78]);
        layout.Controls.Add(ReadText(device, "Selected browser and account"), 0, 0);
        var choices = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true,
            BackColor = Studio.Pane, ForeColor = Studio.Text, Font = Studio.Regular,
            BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Browser permissions" };
        var options = new[] { new RcPermission("monitor", "View status"), new RcPermission("chat", "Conversation"),
            new RcPermission("workspace", "Full mobile Tlamatini workspace"), new RcPermission("launch", "Start Tlamatini"),
            new RcPermission("stop", "Stop Tlamatini (all accounts in this edition)"), new RcPermission("schedules", "Manage schedules") };
        foreach (var option in options) choices.Items.Add(option, current.Contains(option.Id, StringComparer.Ordinal));
        // Preserve previously granted scopes that this six-choice editor does not expose.
        var retained = current.Except(options.Select(x => x.Id), StringComparer.Ordinal).ToArray();
        layout.Controls.Add(choices, 0, 1);
        layout.Controls.Add(ReadText("Start and Stop are separate permissions. Saving ends this browser's current connection. Reconnect with its existing approval; no new invitation is needed.", "Permission change details"), 0, 2);
        dialog.BodyArea.Controls.Add(layout);
        var save = dialog.Choice("Save permissions", DialogResult.OK, ButtonTone.Primary);
        var cancel = dialog.Choice("Cancel", DialogResult.Cancel);
        dialog.AcceptButton = dialog.CancelButton = cancel; dialog.ActiveControl = cancel;
        save.Enabled = choices.CheckedItems.Count + retained.Length > 0;
        choices.ItemCheck += (_, e) => save.Enabled = choices.CheckedItems.Count + retained.Length
            + (e.NewValue == CheckState.Checked ? 1 : 0) - (e.CurrentValue == CheckState.Checked ? 1 : 0) > 0;
        return dialog.Open(owner) == DialogResult.OK
            ? choices.CheckedItems.Cast<RcPermission>().Select(x => x.Id).Concat(retained).ToArray() : null;
    }
    private sealed record RcPermission(string Id, string Label) { public override string ToString() => Label; }

    internal static bool ReviewFiles(Form owner, JsonObject preview)
    {
        var files = preview["files"]!.AsArray();
        using var dialog = new ServiceDialogs(owner, "Review exact files for erasure", "Torrenter   /   File erasure", "Review files for erasure", new(686, 392));
        var layout = Studio.Grid([-1], [26, 35, 10, -1]);
        layout.Controls.Add(Studio.Label($"{files.Count} {(files.Count == 1 ? "file" : "files")} in this request", Studio.Strong, Studio.Text), 0, 0);
        var job = new TextBox { Text = (string?)preview["job_id"], ReadOnly = true };
        var identity = Studio.Grid([55, -1], [-1]);
        identity.Controls.Add(Studio.Label("Job ID", Studio.Small, Studio.Muted), 0, 0);
        identity.Controls.Add(Studio.Field(job, "Job ID"), 1, 0); layout.Controls.Add(identity, 0, 1);
        var list = ReadText(string.Join(Environment.NewLine + Environment.NewLine,
            files.Select(f => $"{f!["bytes"]} bytes   ·   {f["path"]}")), "Exact files and sizes");
        list.BackColor = Studio.Pane; list.Font = Studio.Small;
        layout.Controls.Add(Studio.Card(list, new Padding(10)), 0, 3); dialog.BodyArea.Controls.Add(layout);
        dialog.Choice("Authorize erasure", DialogResult.Yes, ButtonTone.Danger);
        var cancel = dialog.Choice("Cancel", DialogResult.Cancel);
        dialog.AcceptButton = dialog.CancelButton = cancel; dialog.ActiveControl = cancel;
        return dialog.Open(owner) == DialogResult.Yes;
    }
}
