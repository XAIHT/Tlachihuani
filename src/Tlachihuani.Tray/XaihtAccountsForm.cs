// Tlachihuani — Angela López Mendoza · XAIHT
using System.Text.Json;
using System.Text.Json.Nodes;
using Tlachihuani.RC;
using Tlachihuani.Windows;

namespace Tlachihuani.Tray;

internal sealed class XaihtAccountsForm : StudioForm
{
    private readonly XaihtIdentity identity;
    private readonly Func<JsonObject, CancellationToken, Task<JsonObject>> native;
    private readonly CancellationTokenSource closing = new();
    private readonly CheckedListBox accounts = new() { Dock = DockStyle.Fill, CheckOnClick = true,
        BorderStyle = BorderStyle.None, BackColor = Studio.Surface, ForeColor = Studio.Body,
        Font = Studio.Regular, AccessibleName = "Accounts allowed through xaiht.org", IntegralHeight = false };
    private readonly Label message = Studio.Label("Loading local accounts…", Studio.Small, Studio.Muted);
    private readonly StudioButton save;
    private sealed record Choice(string Id, string Label) { public override string ToString() => Label; }

    internal XaihtAccountsForm(Form owner, XaihtIdentity identity,
        Func<JsonObject, CancellationToken, Task<JsonObject>>? call = null)
        : base(owner.Icon, "Tlachihuani · Website accounts", "Tlachihuani   /   xaiht.org", "Choose remote accounts", new(700, 470), new(620, 430))
    {
        this.identity = identity; native = call ?? RcEndpoint.CallInstalledAsync;
        var layout = Studio.Grid([-1], [82, 10, -1, 10, 54]);
        var explanation = Studio.Label($"Allow website user #{identity.UserId} on {identity.Origin}, computer #{identity.ComputerId}, to chat with the accounts checked below. "
            + "The website carries prompts and answers over HTTPS; this connection is not end-to-end or post-quantum encrypted. Uncheck an account to revoke its access.", Studio.Small, Studio.Body);
        explanation.AutoEllipsis = false; explanation.TextAlign = ContentAlignment.TopLeft;
        layout.Controls.Add(explanation, 0, 0);
        layout.Controls.Add(Studio.Card(accounts, new Padding(10)), 0, 2);
        message.AutoEllipsis = false; message.TextAlign = ContentAlignment.TopLeft; layout.Controls.Add(message, 0, 4);
        BodyArea.Controls.Add(layout);
        save = Command("Save account access", () => _ = Save(), ButtonTone.Primary); save.Enabled = false;
        Commands.Controls.Add(save); var cancel = Command("Close", Close); Commands.Controls.Add(cancel); CancelButton = cancel;
        Shown += async (_, _) => await LoadAccounts();
        FormClosed += (_, _) => closing.Cancel();
    }
    private JsonObject Request(string op) => new() { ["op"] = op, ["identity"] = JsonSerializer.SerializeToNode(identity, RcProtocol.Json) };
    private async Task<JsonObject> Call(JsonObject request)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(closing.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        return await native(request, timeout.Token);
    }
    private async Task LoadAccounts()
    {
        try
        {
            var result = await Call(Request("xaiht-accounts"));
            if (closing.IsCancellationRequested) return;
            var approved = result["approved"]?.Deserialize<string[]>(RcProtocol.Json) ?? [];
            accounts.Items.Clear();
            foreach (var item in result["accounts"]?.AsArray() ?? [])
            {
                var id = (string?)item?["id"] ?? "";
                accounts.Items.Add(new Choice(id, $"{item?["display_name"]} · {item?["edition"]} · {item?["application_state"]}"), approved.Contains(id, StringComparer.Ordinal));
            }
            message.Text = accounts.Items.Count == 0 ? "Start Tlamatini, sign in to the intended account and enable Remote-Controller there, then reopen this dialog."
                : "Only the checked accounts are available remotely. Tlamatini and Remote-Controller must remain running.";
            save.Enabled = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!closing.IsCancellationRequested) message.Text = "Unable to load accounts. Start Remote-Controller in XT first. " + ex.Message; }
    }
    private async Task Save()
    {
        save.Enabled = false; accounts.Enabled = false;
        try
        {
            var request = Request("xaiht-set-accounts");
            request["binding_ids"] = JsonSerializer.SerializeToNode(accounts.CheckedItems.Cast<Choice>().Select(x => x.Id).ToArray(), RcProtocol.Json);
            await Call(request);
            if (!closing.IsCancellationRequested) { message.Text = "Account access saved. Refresh the account list on your phone."; message.ForeColor = Studio.AccentHover; }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!closing.IsCancellationRequested) { message.Text = "Account access was not saved: " + ex.Message; message.ForeColor = Studio.Danger; } }
        finally { if (!closing.IsCancellationRequested) { save.Enabled = true; accounts.Enabled = true; } }
    }
    protected override void Dispose(bool disposing) { if (disposing) closing.Dispose(); base.Dispose(disposing); }
}
