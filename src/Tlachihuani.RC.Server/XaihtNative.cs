// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (account-bound command/result bodies must not enter method-argument traces)
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tlachihuani.RC.Server;

internal sealed partial class RcRuntime
{
    private static XaihtIdentity WebsiteIdentity(JsonObject request) => XaihtAccess.Validate(
        request["identity"]?.Deserialize<XaihtIdentity>(RcProtocol.Json)
        ?? throw new RcFault("invalid_request", "Website identity is required.", 400));

    private object XaihtAccounts(JsonObject request, string sid)
    {
        var approved = Xaiht.ApprovedBindings(WebsiteIdentity(request), sid);
        var all = JsonSerializer.SerializeToNode(Accounts(sid), RcProtocol.Json)!.AsObject();
        all["approved"] = JsonSerializer.SerializeToNode(approved, RcProtocol.Json);
        return all;
    }
    private object XaihtSetAccounts(JsonObject request, string sid)
    {
        Xaiht.SetAccounts(WebsiteIdentity(request), request["binding_ids"]?.Deserialize<string[]>(RcProtocol.Json)
            ?? throw new RcFault("invalid_request", "Account selection is required.", 400), sid);
        return new { saved = true };
    }
    private object XaihtRevoke(string connectionId, string sid)
    { Xaiht.RevokeConnection(connectionId, sid); return new { revoked = true }; }

    private static object WebsiteReceipt(OperationReceipt op)
    {
        var receipt = JsonSerializer.SerializeToNode(new
        {
            op.Id, op.BindingId, op.Kind, op.State, op.Result, op.Error, op.Progress,
            op.PendingPermission, op.CreatedAt, op.Deadline
        }, RcProtocol.Json)!.AsObject();
        var utf8 = new JsonSerializerOptions(RcProtocol.Json) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        int Size() => JsonSerializer.SerializeToUtf8Bytes(receipt, utf8).Length;
        if (Size() > 60000 && receipt["progress"] is not null) receipt["progress"] = null;
        if (Size() > 60000 && receipt["result"] is JsonObject result && (string?)result["content_ticket"] is { Length: > 0 })
        {
            // Preserve the signed full-answer ticket rather than losing an entire
            // native reply when escaped or multi-byte content exceeds its frame.
            result.Remove("html"); result["text"] = "Full answer available."; result["truncated"] = true;
        }
        if (Size() > 60000) throw new RcFault("result_too_large", "The adapter response exceeds the native envelope limit and needs a full-answer ticket.", 413);
        return receipt;
    }

    private object XaihtOperation(JsonObject request, string sid)
    {
        var principal = Xaiht.Principal(WebsiteIdentity(request), (string?)request["binding_id"] ?? "", sid);
        var op = Operations.Get(principal, RcProtocol.RequireId((string?)request["operation_id"] ?? "", "operation ID"));
        if (op.DeviceId != principal.DeviceId) throw new RcFault("not_found", "Operation not found.", 404);
        return new { operation = WebsiteReceipt(op) };
    }

    // Returned only over the authenticated local pipe. XT fetches this fixed
    // loopback endpoint itself; no local URL or port is returned to the website.
    private object XaihtContent(JsonObject request, string sid)
    {
        var identity = WebsiteIdentity(request);
        var bindingId = (string?)request["binding_id"] ?? "";
        _ = Xaiht.Principal(identity, bindingId, sid);
        var ticket = RcProtocol.Text((string?)request["ticket"] ?? "", "response ticket", 4096);
        if (ticket.Any(c => !char.IsAscii(c) || char.IsControl(c))) throw new RcFault("invalid_request", "Invalid response ticket.", 400);
        return new { endpoint = new Uri(WorkspaceEndpoint(bindingId), "agent/remote-controller/response-content/?ticket=" + Uri.EscapeDataString(ticket)).AbsoluteUri };
    }

    private object XaihtCommand(JsonObject request, string sid)
    {
        var identity = WebsiteIdentity(request);
        var command = request["command"] as JsonObject ?? throw new RcFault("invalid_request", "Command is required.", 400);
        if ((string?)command["schema"] != "xaiht.tlamatini-rc.command/1"
            || (long?)command["computer"]?["id"] != identity.ComputerId
            || (long?)command["from"]?["user_id"] != identity.UserId)
            throw new RcFault("forbidden", "Command identity does not match this connection.", 403);
        var requestId = RcProtocol.RequireId((string?)command["request_id"] ?? "", "request ID");
        if (!DateTimeOffset.TryParse((string?)command["expires_at"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expires))
            throw new RcFault("invalid_request", "Command expiry is required.", 400);
        var operation = (string?)command["operation"] ?? "";
        var payload = command["payload"] as JsonObject ?? throw new RcFault("invalid_request", "Object payload required.", 400);
        if (operation == "accounts.list")
        {
            if (expires <= Clock.GetUtcNow()) throw new RcFault("command_expired", "Send this request again.", 410);
            var approved = Xaiht.ApprovedBindings(identity, sid);
            return new { accounts = approved.Select(id =>
            {
                var binding = Store.Read(s => Access.Binding(s, id));
                return new { binding.Id, binding.DisplayName, binding.Edition,
                    application_state = ((JsonObject)JsonSerializer.SerializeToNode(Application(id), RcProtocol.Json)!)["state"]?.GetValue<string>() };
            }).ToArray() };
        }
        var bindingId = RcProtocol.RequireId((string?)command["binding_id"] ?? "", "account binding");
        var principal = Xaiht.Principal(identity, bindingId, sid);
        if (operation is "conversation.submit" or "conversation.history" or "conversation.cancel" or "conversation.permission")
        {
            var value = JsonSerializer.SerializeToElement(payload, RcProtocol.Json);
            var key = XaihtAccess.IdempotencyKey(identity, requestId);
            var existing = Operations.FindExisting(principal, key, operation, value);
            if (existing is not null) return new { operation = WebsiteReceipt(existing) };
            if (expires <= Clock.GetUtcNow()) throw new RcFault("command_expired", "Request expired before dispatch; send a new request.", 410);
            // A locally running, connected account is required. This path never
            // launches applications, switches users, or grants extra scopes.
            if (operation is "conversation.cancel" or "conversation.permission")
            {
                var target = Operations.Get(principal, (string?)payload["operation_id"] ?? "");
                if (target.DeviceId != principal.DeviceId) throw new RcFault("not_found", "Conversation not found.", 404);
                if (operation != "conversation.cancel" || target.State != "accepted") _ = WorkspaceEndpoint(bindingId);
            }
            else _ = WorkspaceEndpoint(bindingId);
            return new { operation = WebsiteReceipt(Operations.Submit(principal, key, operation, value, 300)) };
        }
        if (expires <= Clock.GetUtcNow()) throw new RcFault("command_expired", "Send this request again.", 410);
        if (operation == "operation.get")
        {
            var op = Operations.Get(principal, RcProtocol.RequireId((string?)payload["operation_id"] ?? "", "operation ID"));
            if (op.DeviceId != principal.DeviceId) throw new RcFault("not_found", "Operation not found.", 404);
            return new { operation = WebsiteReceipt(op) };
        }
        if (operation == "application.status") return new { application = Application(bindingId) };
        if (operation == "response.content")
        {
            if ((int?)payload["offset"] is not int offset || offset is < 0 or > 8388608)
                throw new RcFault("invalid_request", "Invalid response byte offset.", 400);
            return XaihtContent(new JsonObject { ["identity"] = request["identity"]!.DeepClone(), ["binding_id"] = bindingId,
                ["ticket"] = (string?)payload["ticket"] }, sid);
        }
        throw new RcFault("unknown_operation", "Unsupported website operation.", 400);
    }
}
