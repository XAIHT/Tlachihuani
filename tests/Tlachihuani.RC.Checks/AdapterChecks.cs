// Tlachihuani — Angela López Mendoza · XAIHT
using System.Text.Json;
using Tlachihuani.RC;

internal static class AdapterChecks
{
    internal static void Register(Action<string, Action<Fixture>> test)
    {
        test("128 adapters: application role leaves launches for XT", f =>
        {
            var p = f.Principal(); var launch = f.Ops.Submit(p, "launch", "application.start", J(new { }));
            var chat = f.Ops.Submit(p, "chat", "conversation.submit", J(new { text = "Hello" }));
            Eq(chat.Id, f.Ops.TakeNext(f.Adapter, f.Sid, "application")!.Id); Eq("accepted", f.Ops.Get(p, launch.Id).State);
        });
        test("129 adapters: broker role leaves conversations for the application", f =>
        {
            var p = f.Principal(); var chat = f.Ops.Submit(p, "chat", "conversation.submit", J(new { text = "Hello" }));
            var launch = f.Ops.Submit(p, "launch", "application.start", J(new { }));
            Eq(launch.Id, f.Ops.TakeNext("", f.Sid, "broker")!.Id); Eq("accepted", f.Ops.Get(p, chat.Id).State);
        });
        test("130 adapters: broker cannot claim another Windows user's launch", f =>
        {
            f.Ops.Submit(f.Principal(), "launch", "application.start", J(new { }));
            True(f.Ops.TakeNext("", "S-1-5-21-999", "broker") is null);
        });
        test("131 adapters: application role requires the exact adapter identity", f =>
        {
            f.Ops.Submit(f.Principal(), "chat", "conversation.submit", J(new { text = "Hello" }));
            True(f.Ops.TakeNext(RcProtocol.Id(), f.Sid, "application") is null);
        });
        test("132 adapters: active conversation can poll controls without taking a second prompt", f =>
        {
            var p = f.Principal(); var chat = f.Ops.Submit(p, "chat", "conversation.submit", J(new { text = "Wait" }));
            True(f.Ops.TakeNext(f.Adapter, f.Sid, "application", true) is null); Eq("accepted", f.Ops.Get(p, chat.Id).State);
        });
        test("133 adapters: cancellation takes priority over queued conversation text", f =>
        {
            var chat = RunningChat(f); var p = f.Principal();
            f.Ops.Submit(p, "second", "conversation.submit", J(new { text = "Later" }));
            var cancel = f.Ops.Submit(p, "cancel", "conversation.cancel", J(new { operation_id = chat.Id }));
            Eq(cancel.Id, f.Ops.TakeNext(f.Adapter, f.Sid, "application", true)!.Id);
        });
        test("134 adapters: approval takes priority over queued conversation text", f =>
        {
            var chat = Pending(f); var p = f.Principal(); f.Ops.Submit(p, "second", "conversation.submit", J(new { text = "Later" }));
            var approval = f.Ops.Submit(p, "approval", "conversation.permission", Reply(chat.Id));
            Eq(approval.Id, f.Ops.TakeNext(f.Adapter, f.Sid, "application", true)!.Id);
        });
        test("135 adapters: broker cannot complete a conversation", f =>
        {
            var chat = RunningChat(f); Fault("forbidden", () => f.Ops.Complete(chat.Id, "", f.Sid, J(new { }), null, true));
        });
        test("136 adapters: broker completion still requires the owning Windows SID", f =>
        {
            var launch = f.Ops.Submit(f.Principal(), "launch", "application.start", J(new { })); f.Ops.TakeNext("", f.Sid, "broker");
            Fault("forbidden", () => f.Ops.Complete(launch.Id, "", "S-1-5-21-999", J(new { }), null, true));
        });
        test("137 adapters: broker completes its claimed launch with observed readiness", f =>
        {
            var launch = f.Ops.Submit(f.Principal(), "launch", "application.start", J(new { })); f.Ops.TakeNext("", f.Sid, "broker");
            f.Ops.Complete(launch.Id, "", f.Sid, J(new { status = "starting", ready = false }), null, true);
            var stored = f.Store.Read(s => s.Operations[launch.Id]); Eq("succeeded", stored.State); True(!stored.Result!.Value.GetProperty("ready").GetBoolean());
        });
        test("138 adapters: application completion preserves its real conversation result", f =>
        {
            var chat = RunningChat(f); f.Ops.Complete(chat.Id, f.Adapter, f.Sid, J(new { text = "Real adapter result" }), null);
            Eq("Real adapter result", f.Store.Read(s => s.Operations[chat.Id]).Result!.Value.GetProperty("text").GetString());
        });
        test("139 progress: wrong adapter cannot update a conversation", f =>
        {
            var chat = RunningChat(f); Fault("forbidden", () => f.Ops.Progress(chat.Id, RcProtocol.Id(), f.Sid, J(new { type = "status" })));
        });
        test("140 progress: wrong Windows identity cannot update a conversation", f =>
        {
            var chat = RunningChat(f); Fault("forbidden", () => f.Ops.Progress(chat.Id, f.Adapter, "S-1-5-21-999", J(new { type = "status" })));
        });
        test("141 progress: application launch cannot acquire conversation progress", f =>
        {
            var launch = f.SubmitRunning(); Fault("forbidden", () => f.Ops.Progress(launch.Id, f.Adapter, f.Sid, J(new { type = "status" })));
        });
        test("142 progress: a queued conversation cannot claim running progress", f =>
        {
            var chat = f.Ops.Submit(f.Principal(), "chat", "conversation.submit", J(new { text = "Hello" }));
            Fault("operation_conflict", () => f.Ops.Progress(chat.Id, f.Adapter, f.Sid, J(new { type = "status" })));
        });
        test("143 progress: a completed conversation cannot publish a new permission", f =>
        {
            var chat = RunningChat(f); f.Ops.Complete(chat.Id, f.Adapter, f.Sid, J(new { }), null);
            Fault("operation_conflict", () => f.Ops.Progress(chat.Id, f.Adapter, f.Sid, Permission()));
        });
        test("144 progress: oversized messages are refused before state mutation", f =>
        {
            var chat = RunningChat(f); Fault("invalid_request", () => f.Ops.Progress(chat.Id, f.Adapter, f.Sid, J(new { text = new string('x', 16385) })));
            True(f.Store.Read(s => s.Operations[chat.Id]).Progress is null);
        });
        test("145 progress: arrays cannot replace a typed progress object", f =>
        {
            var chat = RunningChat(f); Fault("invalid_request", () => f.Ops.Progress(chat.Id, f.Adapter, f.Sid, J(new[] { "status" })));
        });
        test("146 progress: observed progress is stored and emits an account event", f =>
        {
            var chat = RunningChat(f); f.Ops.Progress(chat.Id, f.Adapter, f.Sid, J(new { type = "status", message = "Working" }));
            Eq("Working", f.Store.Read(s => s.Operations[chat.Id]).Progress!.Value.GetProperty("message").GetString());
            True(f.Store.Read(s => s.Events.Any(e => e.Kind == "operation.progress" && e.BindingId == f.Binding.Id && e.OperationId == chat.Id)));
        });
        test("147 permissions: ordinary progress cannot hide pending execution consent", f =>
        {
            var chat = Pending(f); f.Ops.Progress(chat.Id, f.Adapter, f.Sid, J(new { type = "status", message = "Still waiting" }));
            Eq("request-1", f.Store.Read(s => s.Operations[chat.Id]).PendingPermission!.Value.GetProperty("request_id").GetString());
        });
        test("148 permissions: an observed decision clears the pending prompt", f =>
        {
            var chat = Pending(f); f.Ops.Progress(chat.Id, f.Adapter, f.Sid, J(new { type = "permission", request_id = "request-1", decision = "deny" }));
            True(f.Store.Read(s => s.Operations[chat.Id]).PendingPermission is null);
        });
        test("149 permissions: completion clears pending consent and progress", f =>
        {
            var chat = Pending(f); f.Ops.Complete(chat.Id, f.Adapter, f.Sid, J(new { text = "Finished" }), null);
            var receipt = f.Store.Read(s => s.Operations[chat.Id]); True(receipt.PendingPermission is null && receipt.Progress is null);
        });
        test("150 permissions: an owned live prompt accepts an explicit Proceed", f =>
        {
            var chat = Pending(f); Eq("accepted", f.Ops.Submit(f.Principal(), "approval", "conversation.permission", Reply(chat.Id)).State);
        });
        test("151 permissions: another account cannot approve the conversation", f =>
        {
            var chat = Pending(f); var other = OtherPrincipal(f);
            Fault("not_found", () => f.Ops.Submit(other, "approval", "conversation.permission", Reply(chat.Id)));
        });
        test("152 permissions: a launch receipt is never an execution-consent target", f =>
        {
            var launch = f.SubmitRunning(); Fault("not_found", () => f.Ops.Submit(f.Principal(), "approval", "conversation.permission", Reply(launch.Id)));
        });
        test("153 permissions: an unknown target does not disclose another operation", f =>
            Fault("not_found", () => f.Ops.Submit(f.Principal(), "approval", "conversation.permission", Reply(RcProtocol.Id()))));
        test("154 permissions: a running chat without a prompt refuses consent", f =>
        {
            var chat = RunningChat(f); Fault("permission_expired", () => f.Ops.Submit(f.Principal(), "approval", "conversation.permission", Reply(chat.Id)));
        });
        test("155 permissions: a mismatched prompt identifier is stale", f =>
        {
            var chat = Pending(f); Fault("permission_expired", () => f.Ops.Submit(f.Principal(), "approval", "conversation.permission", Reply(chat.Id, "stale")));
        });
        test("156 permissions: consent after conversation completion is refused", f =>
        {
            var chat = Pending(f); f.Ops.Complete(chat.Id, f.Adapter, f.Sid, J(new { }), null);
            Fault("permission_expired", () => f.Ops.Submit(f.Principal(), "approval", "conversation.permission", Reply(chat.Id)));
        });
        test("157 permissions: arbitrary decision words are refused", f => InvalidReply(new { operation_id = RcProtocol.Id(), request_id = "r", decision = "execute" }));
        test("158 permissions: boolean approval cannot bypass explicit typed decisions", f => InvalidReply(new { operation_id = RcProtocol.Id(), request_id = "r", decision = true }));
        test("159 permissions: request identifier is mandatory", f => InvalidReply(new { operation_id = RcProtocol.Id(), decision = "proceed" }));
        test("160 permissions: decision is mandatory", f => InvalidReply(new { operation_id = RcProtocol.Id(), request_id = "r" }));
        test("161 permissions: prompt identifier is bounded", f => InvalidReply(new { operation_id = RcProtocol.Id(), request_id = new string('x', 129), decision = "proceed" }));
        test("162 permissions: control characters in prompt identity are refused", f => InvalidReply(new { operation_id = RcProtocol.Id(), request_id = "r\0", decision = "proceed" }));
        test("163 permissions: conversation target must have the immutable ID format", f => InvalidReply(new { operation_id = "1", request_id = "r", decision = "proceed" }));
        test("164 permissions: duplicate decision fields are refused", f =>
        {
            using var document = JsonDocument.Parse("{\"operation_id\":\"" + RcProtocol.Id() + "\",\"request_id\":\"r\",\"decision\":\"deny\",\"decision\":\"proceed\"}");
            Fault("invalid_request", () => Operations.ValidateOperation("conversation.permission", document.RootElement));
        });
        test("165 permissions: consent cannot supply a command to execute", f => InvalidReply(new { operation_id = RcProtocol.Id(), request_id = "r", decision = "proceed", command = "cmd.exe" }));
        test("166 permissions: monitoring scope cannot grant execution consent", f =>
        {
            var chat = Pending(f); var enrollment = f.Pending(["monitor"]); var device = f.Access.Approve(enrollment.Id, enrollment.Fingerprint, f.Sid);
            var p = f.Access.Authenticate(f.Login(device.Id).AccessToken);
            Fault("forbidden", () => f.Ops.Submit(p, "approval", "conversation.permission", Reply(chat.Id)));
        });
        test("167 permissions: dispatch atomically consumes pending execution consent", f =>
        {
            var chat = Pending(f); var approval = f.Ops.Submit(f.Principal(), "approval", "conversation.permission", Reply(chat.Id));
            Eq(approval.Id, f.Ops.TakeNext(f.Adapter, f.Sid, "application", true)!.Id);
            True(f.Store.Read(s => s.Operations[chat.Id]).PendingPermission is null);
        });
        test("168 permissions: two accepted decisions cannot both dispatch", f =>
        {
            var chat = Pending(f); var p = f.Principal();
            var first = f.Ops.Submit(p, "first", "conversation.permission", Reply(chat.Id));
            var second = f.Ops.Submit(p, "second", "conversation.permission", Reply(chat.Id, decision: "deny"));
            Eq(first.Id, f.Ops.TakeNext(f.Adapter, f.Sid, "application", true)!.Id); True(f.Ops.TakeNext(f.Adapter, f.Sid, "application", true) is null);
            Eq("permission_expired", f.Ops.Get(p, second.Id).Error);
        });
        test("169 permissions: a replaced prompt invalidates a queued decision", f =>
        {
            var chat = Pending(f); var p = f.Principal(); var reply = f.Ops.Submit(p, "approval", "conversation.permission", Reply(chat.Id));
            f.Ops.Progress(chat.Id, f.Adapter, f.Sid, Permission("request-2"));
            True(f.Ops.TakeNext(f.Adapter, f.Sid, "application", true) is null); Eq("permission_expired", f.Ops.Get(p, reply.Id).Error);
            Eq("request-2", f.Ops.Get(p, chat.Id).PendingPermission!.Value.GetProperty("request_id").GetString());
        });
        test("170 permissions: retry before dispatch returns the same receipt", f =>
        {
            var chat = Pending(f); var p = f.Principal(); var first = f.Ops.Submit(p, "approval", "conversation.permission", Reply(chat.Id));
            Eq(first.Id, f.Ops.Submit(p, "approval", "conversation.permission", Reply(chat.Id)).Id);
        });
        test("171 permissions: retry after dispatch returns the receipt without reapproving", f =>
        {
            var chat = Pending(f); var p = f.Principal(); var first = f.Ops.Submit(p, "approval", "conversation.permission", Reply(chat.Id));
            f.Ops.TakeNext(f.Adapter, f.Sid, "application", true);
            Eq(first.Id, f.Ops.Submit(p, "approval", "conversation.permission", Reply(chat.Id)).Id);
            True(f.Ops.TakeNext(f.Adapter, f.Sid, "application", true) is null);
        });
        test("172 permissions: explicit Deny survives dispatch unchanged", f =>
        {
            var chat = Pending(f); f.Ops.Submit(f.Principal(), "deny", "conversation.permission", Reply(chat.Id, decision: "deny"));
            Eq("deny", f.Ops.TakeNext(f.Adapter, f.Sid, "application", true)!.Payload.GetProperty("decision").GetString());
        });
        test("173 progress: revocation prevents new application progress", f =>
        {
            var chat = RunningChat(f); f.Access.Revoke(f.Device.Id, f.Sid);
            Fault("device_unavailable", () => f.Ops.Progress(chat.Id, f.Adapter, f.Sid, J(new { type = "status" })));
        });
        test("174 binding disable: live session and channel authority end", f =>
        {
            var p = f.Principal(); Disable(f); True(p.Revoked.IsCancellationRequested); Fault("unauthorized", () => f.Access.EnsureSession(p));
        });
        test("175 binding disable: every enrolled device is revoked", f =>
        {
            var enrollment = f.Pending(); f.Access.Approve(enrollment.Id, enrollment.Fingerprint, f.Sid); Disable(f);
            True(f.Store.Read(s => s.Devices.Values.Where(d => d.BindingId == f.Binding.Id).All(d => d.Revoked)));
        });
        test("176 binding disable: pending launch and conversation are cancelled", f =>
        {
            var p = f.Principal(); f.Ops.Submit(p, "launch", "application.start", J(new { })); f.Ops.Submit(p, "chat", "conversation.submit", J(new { text = "Hello" })); Disable(f);
            True(f.Store.Read(s => s.Operations.Values.All(o => o.State == "cancelled" && o.Error == "account_disabled")));
        });
        test("177 binding disable: running adapter can no longer publish progress", f =>
        {
            var chat = RunningChat(f); Disable(f); Fault("account_unavailable", () => f.Ops.Progress(chat.Id, f.Adapter, f.Sid, J(new { type = "status" })));
        });
        test("178 binding disable: wrong adapter is refused", f => Fault("forbidden", () => f.Access.DisableBinding(f.Binding.Id, RcProtocol.Id(), f.Sid)));
        test("179 binding disable: wrong Windows SID is refused", f => Fault("forbidden", () => f.Access.DisableBinding(f.Binding.Id, f.Adapter, "S-1-5-21-999")));
        test("180 binding disable: unknown binding is refused", f => Fault("not_found", () => f.Access.DisableBinding(RcProtocol.Id(), f.Adapter, f.Sid)));
        test("181 binding enable: immutable identity survives with a newer revision", f =>
        {
            Disable(f); var reenabled = Enable(f); Eq(f.Binding.Id, reenabled.Id); True(reenabled.Enabled && reenabled.Revision > f.Binding.Revision);
        });
        test("182 binding enable: old device proofs remain revoked", f =>
        {
            Disable(f); Enable(f); Fault("device_unavailable", () => f.Access.CreateChallenge(f.Device.Id));
        });
        test("183 binding enable: a newly invited and approved device can authenticate", f =>
        {
            Disable(f); Enable(f); var enrollment = f.Pending(); var device = f.Access.Approve(enrollment.Id, enrollment.Fingerprint, f.Sid);
            var p = f.Access.Authenticate(f.Login(device.Id).AccessToken); Eq(f.Binding.Id, p.BindingId); True(p.DeviceId != f.Device.Id);
        });
        test("184 binding enable: unused invitations from before disable stay invalid", f =>
        {
            var invite = f.Invite(); Disable(f); Enable(f); Fault("invalid_invitation", () => f.Access.Enroll(invite.Invitation, "Old invitation", f.PublicKey));
        });
        test("185 binding enable: pending approvals from before disable stay closed", f =>
        {
            var pending = f.Pending(); Disable(f); Enable(f); Fault("enrollment_unavailable", () => f.Access.Approve(pending.Id, pending.Fingerprint, f.Sid));
        });
        test("186 binding disable: another edition's live device remains authorized", f =>
        {
            var other = OtherPrincipal(f); Disable(f); f.Access.EnsureSession(other); True(!other.Revoked.IsCancellationRequested);
        });
        test("187 account identities: the same UUID in both editions stays separate", f =>
        {
            var spanish = f.Access.RegisterBinding("spanish", f.Binding.AccountId, "Cuenta", f.Sid, f.Adapter);
            True(spanish.Id != f.Binding.Id); Eq("spanish", spanish.Edition); Eq(2, f.Store.Read(s => s.Bindings.Count));
        });
        test("188 plane isolation: RC cannot execute a Torrenter lifecycle operation", f =>
            Fault("unknown_operation", () => f.Ops.Submit(f.Principal(), "torrenter", "torrenter.start", J(new { }))));
        test("189 plane isolation: an approval cannot choose another service", f =>
            InvalidReply(new { operation_id = RcProtocol.Id(), request_id = "r", decision = "proceed", service = "Tlachihuani.Jobs" }));
        test("190 permissions: another account cannot read the pending command details", f =>
        {
            var chat = Pending(f); var other = OtherPrincipal(f); Fault("not_found", () => f.Ops.Get(other, chat.Id));
        });
        test("191 adapters: broker cannot consume application status work", f =>
        {
            var p = f.Principal(); var status = f.Ops.Submit(p, "status", "application.status", J(new { }));
            True(f.Ops.TakeNext("", f.Sid, "broker") is null); Eq("accepted", f.Ops.Get(p, status.Id).State);
        });
        test("192 permissions: recovery never dispatches approval for an unknown chat outcome", f =>
        {
            var chat = Pending(f); var p = f.Principal(); var reply = f.Ops.Submit(p, "approval", "conversation.permission", Reply(chat.Id));
            f.Ops.Recover(); True(f.Ops.TakeNext(f.Adapter, f.Sid, "application", true) is null); Eq("permission_expired", f.Ops.Get(p, reply.Id).Error);
        });
    }

    internal static void RegisterRevocationCases(Action<string, Action<Fixture>> test)
    {
        test("204 consent revocation: originating device loses pending approval immediately", f =>
        {
            var chat = Pending(f); f.Access.Revoke(f.Device.Id, f.Sid);
            var stored = f.Store.Read(s => s.Operations[chat.Id]);
            True(stored.PendingPermission is null); Eq("running", stored.State);
        });
        test("205 consent revocation: a second authorized device cannot approve revoked work", f =>
        {
            var chat = Pending(f); var other = SameAccountPrincipal(f); f.Access.Revoke(f.Device.Id, f.Sid);
            f.Access.EnsureSession(other);
            Fault("permission_expired", () => f.Ops.Submit(other, "approval", "conversation.permission", Reply(chat.Id)));
        });
        test("206 consent revocation: an already queued second-device approval cannot dispatch", f =>
        {
            var chat = Pending(f); var other = SameAccountPrincipal(f);
            var reply = f.Ops.Submit(other, "approval", "conversation.permission", Reply(chat.Id));
            f.Access.Revoke(f.Device.Id, f.Sid);
            True(f.Ops.TakeNext(f.Adapter, f.Sid, "application", true) is null);
            Eq("permission_expired", f.Ops.Get(other, reply.Id).Error);
        });
        test("207 consent authority: originating chat scope is rechecked at admission", f =>
        {
            var chat = Pending(f); var other = SameAccountPrincipal(f);
            f.Store.Write(s => { s.Devices[f.Device.Id] = s.Devices[f.Device.Id] with { Scopes = ["monitor"] }; return true; });
            Fault("permission_expired", () => f.Ops.Submit(other, "approval", "conversation.permission", Reply(chat.Id)));
        });
        test("208 consent authority: originating chat scope is rechecked before dispatch", f =>
        {
            var chat = Pending(f); var other = SameAccountPrincipal(f);
            var reply = f.Ops.Submit(other, "approval", "conversation.permission", Reply(chat.Id));
            f.Store.Write(s => { s.Devices[f.Device.Id] = s.Devices[f.Device.Id] with { Scopes = ["monitor"] }; return true; });
            True(f.Ops.TakeNext(f.Adapter, f.Sid, "application", true) is null);
            Eq("permission_expired", f.Ops.Get(other, reply.Id).Error);
        });
        test("209 consent revocation: another account-owned device can still request cleanup", f =>
        {
            var chat = Pending(f); var other = SameAccountPrincipal(f); f.Access.Revoke(f.Device.Id, f.Sid);
            var cancel = f.Ops.Submit(other, "cancel", "conversation.cancel", J(new { operation_id = chat.Id }));
            Eq(cancel.Id, f.Ops.TakeNext(f.Adapter, f.Sid, "application", true)!.Id);
        });
        test("210 consent revocation: revoking an unrelated device preserves valid pending consent", f =>
        {
            var chat = Pending(f); var other = SameAccountPrincipal(f); f.Access.Revoke(other.DeviceId, f.Sid);
            var p = f.Principal(); var reply = f.Ops.Submit(p, "approval", "conversation.permission", Reply(chat.Id));
            Eq(reply.Id, f.Ops.TakeNext(f.Adapter, f.Sid, "application", true)!.Id);
        });
        test("211 consent revocation: owning adapter may still report its actual final result", f =>
        {
            var chat = Pending(f); f.Access.Revoke(f.Device.Id, f.Sid);
            f.Ops.CompleteApplication(chat.Id, f.Adapter, f.Sid, J(new { cancelled = true }), null);
            var stored = f.Store.Read(s => s.Operations[chat.Id]);
            Eq("succeeded", stored.State); True(stored.Result!.Value.GetProperty("cancelled").GetBoolean());
        });
        test("212 consent revocation: idempotent retry observes its receipt without renewed authority", f =>
        {
            var chat = Pending(f); var other = SameAccountPrincipal(f);
            var reply = f.Ops.Submit(other, "approval", "conversation.permission", Reply(chat.Id));
            f.Access.Revoke(f.Device.Id, f.Sid);
            Eq(reply.Id, f.Ops.Submit(other, "approval", "conversation.permission", Reply(chat.Id)).Id);
            True(f.Ops.TakeNext(f.Adapter, f.Sid, "application", true) is null);
            Eq("permission_expired", f.Ops.Get(other, reply.Id).Error);
        });
    }

    private static Principal SameAccountPrincipal(Fixture f)
    {
        var enrollment = f.Pending(["monitor", "chat"]);
        var device = f.Access.Approve(enrollment.Id, enrollment.Fingerprint, f.Sid);
        return f.Access.Authenticate(f.Login(device.Id).AccessToken);
    }

    private static OperationReceipt RunningChat(Fixture f)
    {
        var chat = f.Ops.Submit(f.Principal(), "chat", "conversation.submit", J(new { text = "Adapter test conversation" }));
        Eq(chat.Id, f.Ops.TakeNext(f.Adapter, f.Sid, "application")!.Id);
        return chat;
    }
    private static OperationReceipt Pending(Fixture f)
    {
        var chat = RunningChat(f); f.Ops.Progress(chat.Id, f.Adapter, f.Sid, Permission()); return chat;
    }
    private static JsonElement Permission(string request = "request-1") => J(new { type = "permission", request_id = request, tool = "Executer", description = "QA permission fixture; no command is executed" });
    private static JsonElement Reply(string operation, string request = "request-1", string decision = "proceed") => J(new { operation_id = operation, request_id = request, decision });
    private static void InvalidReply(object value) => Fault("invalid_request", () => Operations.ValidateOperation("conversation.permission", J(value)));
    private static Principal OtherPrincipal(Fixture f)
    {
        var binding = f.Access.RegisterBinding("spanish", RcProtocol.Id(), "Other account", f.Sid, RcProtocol.Id());
        var invite = f.Access.CreateInvitation(binding.Id, ["monitor", "chat", "launch"], f.Sid);
        var enrollment = f.Access.Enroll(invite.Invitation, "Other account device", f.PublicKey);
        var device = f.Access.Approve(enrollment.Id, enrollment.Fingerprint, f.Sid);
        return f.Access.Authenticate(f.Login(device.Id).AccessToken);
    }
    private static void Disable(Fixture f) => f.Access.DisableBinding(f.Binding.Id, f.Adapter, f.Sid);
    private static AccountBinding Enable(Fixture f) => f.Access.RegisterBinding(f.Binding.Edition, f.Binding.AccountId, f.Binding.DisplayName, f.Sid, f.Adapter);
    private static JsonElement J(object value) => JsonSerializer.SerializeToElement(value);
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; received {actual}."); }
    private static void Fault(string code, Action action)
    {
        try { action(); } catch (RcFault fault) { Eq(code, fault.Code); return; }
        throw new Exception("Expected RC refusal: " + code);
    }
}
