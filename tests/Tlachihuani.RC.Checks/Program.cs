// Tlachihuani — Angela López Mendoza · XAIHT
using System.Security.Cryptography;
using System.Text.Json;
using Tlachihuani.RC;

if (args is not ["--output", var output]) { Console.Error.WriteLine("Use --output <application-owned evidence directory>."); return 2; }
output = Path.GetFullPath(output);
StateStore.RejectProfileOrReparse(output);
Directory.CreateDirectory(output);
var tests = new List<(string Name, Action<Fixture> Run)>();
void Test(string name, Action<Fixture> run) => tests.Add((name, run));
static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
static void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; received {actual}."); }
static void Fault(string code, Action action)
{
    try { action(); } catch (RcFault fault) { Eq(code, fault.Code); return; }
    throw new Exception("Expected RC refusal: " + code);
}
static void Fails<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

Test("001 configuration: default remains loopback", f => { f.Config.Validate(); Eq("127.0.0.1", f.Config.BindAddress); });
Test("002 configuration: wildcard IPv4 refused", f => Fault("invalid_configuration", () => (f.Config with { BindAddress = "0.0.0.0" }).Validate()));
Test("003 configuration: wildcard IPv6 refused", f => Fault("invalid_configuration", () => (f.Config with { BindAddress = "::" }).Validate()));
Test("004 configuration: remote HTTP refused", f => Fault("invalid_configuration", () => (f.Config with { BindAddress = "192.0.2.1" }).Validate()));
Test("005 configuration: Torrenter port reserved", f => Fault("invalid_configuration", () => (f.Config with { Port = 12064 }).Validate()));
Test("006 configuration: privileged port refused", f => Fault("invalid_configuration", () => (f.Config with { Port = 80 }).Validate()));
Test("007 configuration: out of range port refused", f => Fault("invalid_configuration", () => (f.Config with { Port = 65536 }).Validate()));
Test("008 configuration: origin port must match", f => Fault("invalid_configuration", () => (f.Config with { PublicOrigin = "http://127.0.0.1:12482" }).Validate()));
Test("009 configuration: origin credentials refused", f => Fault("invalid_configuration", () => (f.Config with { PublicOrigin = "http://user@127.0.0.1:12481" }).Validate()));
Test("010 configuration: origin path refused", f => Fault("invalid_configuration", () => (f.Config with { PublicOrigin = "http://127.0.0.1:12481/path" }).Validate()));
Test("011 configuration: origin query refused", f => Fault("invalid_configuration", () => (f.Config with { PublicOrigin = "http://127.0.0.1:12481/?secret=x" }).Validate()));
Test("012 configuration: origin fragment refused", f => Fault("invalid_configuration", () => (f.Config with { PublicOrigin = "http://127.0.0.1:12481/#token" }).Validate()));
Test("013 configuration: remote hostname refused in local mode", f => Fault("invalid_configuration", () => (f.Config with { PublicOrigin = "http://example.com:12481" }).Validate()));
Test("014 configuration: schema mismatch refused", f => Fault("incompatible_configuration", () => (f.Config with { Schema = 2 }).Validate()));
Test("015 configuration: unknown security profile refused", f => Fault("invalid_configuration", () => (f.Config with { SecurityProfile = "insecure" }).Validate()));
Test("016 configuration: PQ requirement never downgrades", f => Fault("security_profile_unavailable", () => (f.Config with { SecurityProfile = "post-quantum-required" }).Validate()));
Test("017 configuration: zero sessions refused", f => Fault("invalid_configuration", () => (f.Config with { MaxSessions = 0 }).Validate()));
Test("018 configuration: account cap cannot exceed host cap", f => Fault("invalid_configuration", () => (f.Config with { MaxSessions = 2, MaxAccountSessions = 3 }).Validate()));
Test("019 configuration: session lifetime bounded", f => Fault("invalid_configuration", () => (f.Config with { SessionMinutes = 31 }).Validate()));
Test("020 configuration: signing certificate not accepted for cleartext mode", f => Fault("invalid_configuration", () => (f.Config with { CertificateThumbprint = "abc" }).Validate()));
Test("021 identity: account UUID required", f => Fault("invalid_request", () => f.Access.RegisterBinding("english", "angela", "Name", f.Sid, f.Adapter)));
Test("022 identity: display name is not edition", f => Fault("invalid_request", () => f.Access.RegisterBinding("arbitrary", RcProtocol.Id(), "Name", f.Sid, f.Adapter)));
Test("023 identity: local Windows SID required", f => Fault("invalid_request", () => f.Access.RegisterBinding("english", RcProtocol.Id(), "Name", "user", f.Adapter)));
Test("024 identity: adapter UUID required", f => Fault("invalid_request", () => f.Access.RegisterBinding("english", RcProtocol.Id(), "Name", f.Sid, "path.exe")));
Test("025 identity: existing binding registration idempotent", f => Eq(f.Binding.Id, f.Access.RegisterBinding("english", f.Binding.AccountId, "Name", f.Sid, f.Adapter).Id));
Test("026 identity: same account cannot change adapter silently", f => Fault("binding_conflict", () => f.Access.RegisterBinding("english", f.Binding.AccountId, "Name", f.Sid, RcProtocol.Id())));
Test("027 identity: edition has its own account boundary", f => True(f.Access.RegisterBinding("spanish", f.Binding.AccountId, "Name", f.Sid, f.Adapter).Id != f.Binding.Id));
Test("028 identity: disabled binding rejects enrollment", f => { f.DisableBinding(); Fault("account_unavailable", () => f.Invite()); });
Test("029 enrollment: local owner required", f => Fault("forbidden", () => f.Access.CreateInvitation(f.Binding.Id, ["monitor"], "S-1-5-21-99")));
Test("030 enrollment: unknown scope refused", f => Fault("invalid_request", () => f.Access.CreateInvitation(f.Binding.Id, ["exec"], f.Sid)));
Test("031 enrollment: empty scopes refused", f => Fault("invalid_request", () => f.Access.CreateInvitation(f.Binding.Id, [], f.Sid)));
Test("032 enrollment: duplicate scopes refused", f => Fault("invalid_request", () => f.Access.CreateInvitation(f.Binding.Id, ["monitor", "monitor"], f.Sid)));
Test("033 enrollment: secret stored only as hash", f => { var invite = f.Invite(); True(!File.ReadAllText(Path.Combine(f.Directory, "state.json")).Contains(invite.Invitation)); });
Test("034 enrollment: invalid invitation rejected", f => Fault("invalid_invitation", () => f.Access.Enroll(RcProtocol.Token(), "Phone", f.PublicKey)));
Test("035 enrollment: expired invitation rejected", f => { var i = f.Invite(); f.Clock.Advance(TimeSpan.FromMinutes(6)); Fault("invalid_invitation", () => f.Access.Enroll(i.Invitation, "Phone", f.PublicKey)); });
Test("036 enrollment: invitation consumed once", f => { var i = f.Invite(); f.Access.Enroll(i.Invitation, "Phone", f.PublicKey); Fault("invalid_invitation", () => f.Access.Enroll(i.Invitation, "Other", f.PublicKey)); });
Test("037 enrollment: invalid SPKI refused", f => Fault("invalid_key", () => f.Access.Enroll(f.Invite().Invitation, "Phone", "not-base64")));
Test("038 enrollment: weak or wrong curve refused", f => { using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384); Fault("invalid_key", () => Access.ValidateKey(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()))); });
Test("039 enrollment: trailing SPKI bytes refused", f => Fault("invalid_key", () => Access.ValidateKey(Convert.ToBase64String(f.Key.ExportSubjectPublicKeyInfo().Concat(new byte[] { 0 }).ToArray()))));
Test("040 enrollment: control characters in name refused", f => Fault("invalid_request", () => f.Access.Enroll(f.Invite().Invitation, "Bad\0Name", f.PublicKey)));
Test("041 enrollment: fingerprint comparison required", f => { var e = f.Pending(); Fault("identity_changed", () => f.Access.Approve(e.Id, "wrong", f.Sid)); });
Test("042 enrollment: wrong Windows user cannot approve", f => { var e = f.Pending(); Fault("forbidden", () => f.Access.Approve(e.Id, e.Fingerprint, "S-1-5-21-99")); });
Test("043 enrollment: pending device cannot authenticate", f => { var e = f.Pending(); Fault("device_unavailable", () => f.Access.CreateChallenge(e.Id)); });
Test("044 enrollment: approval grants only invited scopes", f => { var e = f.Pending(["monitor"]); Eq("monitor", string.Join(',', f.Access.Approve(e.Id, e.Fingerprint, f.Sid).Scopes)); });
Test("045 enrollment: repeated approval refused", f => { var e = f.Pending(); f.Access.Approve(e.Id, e.Fingerprint, f.Sid); Fault("enrollment_unavailable", () => f.Access.Approve(e.Id, e.Fingerprint, f.Sid)); });
Test("046 enrollment: expired local approval refused", f => { var e = f.Pending(); f.Clock.Advance(TimeSpan.FromMinutes(6)); Fault("enrollment_unavailable", () => f.Access.Approve(e.Id, e.Fingerprint, f.Sid)); });
Test("047 enrollment: decline prevents approval", f => { var e = f.Pending(); f.Access.Decline(e.Id, f.Sid); Fault("enrollment_unavailable", () => f.Access.Approve(e.Id, e.Fingerprint, f.Sid)); });
Test("048 enrollment: poll secret required", f => { var e = f.Pending(); Fault("enrollment_unavailable", () => f.Access.PollEnrollment(e.Id, RcProtocol.Token())); });
Test("049 enrollment: approved result available only to originating phone", f => { var e = f.Pending(); var d = f.Access.Approve(e.Id, e.Fingerprint, f.Sid); True(JsonSerializer.Serialize(f.Access.PollEnrollment(e.Id, e.PollSecret), RcProtocol.Json).Contains(d.Id)); });
Test("050 enrollment: pending approval survives invitation cleanup", f => { var i = f.Invite(); f.Clock.Advance(TimeSpan.FromMinutes(4)); var e = f.Access.Enroll(i.Invitation, "Phone", f.PublicKey); f.Clock.Advance(TimeSpan.FromMinutes(2)); f.Invite(); True(f.Access.Approve(e.Id, e.Fingerprint, f.Sid).Id.Length == 32); });
Test("051 authentication: valid device proof creates session", f => Eq(f.Device.Id, f.Login().DeviceId));
Test("052 authentication: malformed signature rejected", f => { var c = f.Access.CreateChallenge(f.Device.Id); Fault("authentication_failed", () => f.Access.Establish(f.Device.Id, c.Id, "bad")); });
Test("053 authentication: another private key refused", f => { var c = f.Access.CreateChallenge(f.Device.Id); using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256); Fault("authentication_failed", () => f.Access.Establish(f.Device.Id, c.Id, Convert.ToBase64String(k.SignData(Convert.FromBase64String(c.Challenge), HashAlgorithmName.SHA256)))); });
Test("054 authentication: challenge replay rejected", f => { var c = f.Access.CreateChallenge(f.Device.Id); var sig = f.Sign(c); f.Access.Establish(f.Device.Id, c.Id, sig); Fault("authentication_failed", () => f.Access.Establish(f.Device.Id, c.Id, sig)); });
Test("055 authentication: failed proof consumes challenge", f => { var c = f.Access.CreateChallenge(f.Device.Id); Fault("authentication_failed", () => f.Access.Establish(f.Device.Id, c.Id, "bad")); Fault("authentication_failed", () => f.Access.Establish(f.Device.Id, c.Id, f.Sign(c))); });
Test("056 authentication: expired challenge refused", f => { var c = f.Access.CreateChallenge(f.Device.Id); f.Clock.Advance(TimeSpan.FromSeconds(61)); Fault("authentication_failed", () => f.Access.Establish(f.Device.Id, c.Id, f.Sign(c))); });
Test("057 authentication: challenge bound to device", f => { var c = f.Access.CreateChallenge(f.Device.Id); Fault("authentication_failed", () => f.Access.Establish(RcProtocol.Id(), c.Id, f.Sign(c))); });
Test("058 authentication: nonce differs on each challenge", f => True(f.Access.CreateChallenge(f.Device.Id).Challenge != f.Access.CreateChallenge(f.Device.Id).Challenge));
Test("059 authentication: unknown bearer rejected", f => Fault("unauthorized", () => f.Access.Authenticate(RcProtocol.Token())));
Test("060 authentication: expired session rejected", f => { var s = f.Login(); f.Clock.Advance(TimeSpan.FromMinutes(16)); Fault("unauthorized", () => f.Access.Authenticate(s.AccessToken)); });
Test("061 authentication: account session cap enforced", f => { for (int i = 0; i < 4; i++) f.Login(); Fault("session_limit", () => f.Login()); });
Test("062 authentication: logout invalidates bearer", f => { var s = f.Login(); f.Access.Close(s.AccessToken); Fault("unauthorized", () => f.Access.Authenticate(s.AccessToken)); });
Test("063 authentication: revocation cancels live channel", f => { var p = f.Principal(); f.Access.Revoke(f.Device.Id, f.Sid); True(p.Revoked.IsCancellationRequested); });
Test("064 authentication: revoked device cannot reauthenticate", f => { f.Access.Revoke(f.Device.Id, f.Sid); Fault("device_unavailable", () => f.Access.CreateChallenge(f.Device.Id)); });
Test("065 authentication: wrong user cannot revoke", f => Fault("forbidden", () => f.Access.Revoke(f.Device.Id, "S-1-5-21-99")));
Test("066 authentication: account disable invalidates session", f => { var s = f.Login(); f.DisableBinding(); Fault("account_unavailable", () => f.Access.Authenticate(s.AccessToken)); });
Test("067 authentication: access token never persisted", f => { var s = f.Login(); True(!File.ReadAllText(Path.Combine(f.Directory, "state.json")).Contains(s.AccessToken)); });
Test("068 authentication: permission check enforced", f => { var e = f.Pending(["monitor"]); var d = f.Access.Approve(e.Id, e.Fingerprint, f.Sid); var s = f.Login(d.Id); Fault("forbidden", () => f.Access.Authenticate(s.AccessToken, "launch")); });
Test("069 channel: one-use ticket accepted", f => { var s = f.Login(); Eq(f.Device.Id, f.Access.ConsumeTicket(f.Access.IssueTicket(s.AccessToken)).DeviceId); });
Test("070 channel: ticket replay refused", f => { var s = f.Login(); var t = f.Access.IssueTicket(s.AccessToken); f.Access.ConsumeTicket(t); Fault("unauthorized", () => f.Access.ConsumeTicket(t)); });
Test("071 channel: expired ticket refused", f => { var t = f.Access.IssueTicket(f.Login().AccessToken); f.Clock.Advance(TimeSpan.FromSeconds(11)); Fault("unauthorized", () => f.Access.ConsumeTicket(t)); });
Test("072 channel: logout invalidates pending ticket", f => { var s = f.Login(); var t = f.Access.IssueTicket(s.AccessToken); f.Access.Close(s.AccessToken); Fault("unauthorized", () => f.Access.ConsumeTicket(t)); });
Test("073 channel: session expiry closes ongoing channel authority", f => { var p = f.Principal(); f.Clock.Advance(TimeSpan.FromMinutes(16)); Fault("unauthorized", () => f.Access.EnsureSession(p)); True(p.Revoked.IsCancellationRequested); });
Test("074 operations: arbitrary executable operation refused", f => Fault("unknown_operation", () => f.Ops.Submit(f.Principal(), "k", "exec", J("{}"))));
Test("075 operations: caller-supplied executable field refused", f => Fault("invalid_request", () => f.Ops.Submit(f.Principal(), "k", "application.start", J("{\"path\":\"cmd.exe\"}"))));
Test("076 operations: caller-supplied PID refused", f => Fault("invalid_request", () => f.Ops.Submit(f.Principal(), "k", "application.status", J("{\"pid\":1}"))));
Test("077 operations: missing conversation text refused", f => Fault("invalid_request", () => f.Ops.Submit(f.Principal(), "k", "conversation.submit", J("{}"))));
Test("078 operations: oversize prompt refused", f => Fault("invalid_request", () => f.Ops.Submit(f.Principal(), "k", "conversation.submit", JsonSerializer.SerializeToElement(new { text = new string('x', 16001) }))));
Test("079 operations: duplicate JSON property refused", f => Fault("invalid_request", () => Operations.ValidateOperation("conversation.submit", J("{\"text\":\"a\",\"text\":\"b\"}"))));
Test("080 operations: non-object payload refused", f => Fault("invalid_request", () => f.Ops.Submit(f.Principal(), "k", "application.start", J("[]"))));
Test("081 operations: deadline bound enforced", f => Fault("invalid_request", () => f.Ops.Submit(f.Principal(), "k", "application.start", J("{}"), 301)));
Test("082 operations: acceptance is not completion", f => Eq("accepted", f.Ops.Submit(f.Principal(), "k", "application.start", J("{}")).State));
Test("083 operations: same key returns same receipt", f => { var p = f.Principal(); var a = f.Ops.Submit(p, "k", "application.start", J("{}")); Eq(a.Id, f.Ops.Submit(p, "k", "application.start", J("{}")).Id); });
Test("084 operations: changed payload conflicts on same key", f => { var p = f.Principal(); f.Ops.Submit(p, "k", "conversation.submit", J("{\"text\":\"a\"}")); Fault("idempotency_conflict", () => f.Ops.Submit(p, "k", "conversation.submit", J("{\"text\":\"b\"}"))); });
Test("085 operations: changed deadline conflicts on same key", f => { var p = f.Principal(); f.Ops.Submit(p, "k", "application.start", J("{}")); Fault("idempotency_conflict", () => f.Ops.Submit(p, "k", "application.start", J("{}"), 20)); });
Test("086 operations: wrong adapter cannot dequeue", f => { f.Ops.Submit(f.Principal(), "k", "application.start", J("{}")); True(f.Ops.TakeNext(RcProtocol.Id(), f.Sid) is null); });
Test("087 operations: wrong Windows session owner cannot dequeue", f => { f.Ops.Submit(f.Principal(), "k", "application.start", J("{}")); True(f.Ops.TakeNext(f.Adapter, "S-1-5-21-99") is null); });
Test("088 operations: dequeue durably records running", f => { var p = f.Principal(); var op = f.Ops.Submit(p, "k", "application.start", J("{}")); Eq(op.Id, f.Ops.TakeNext(f.Adapter, f.Sid)!.Id); Eq("running", f.Ops.Get(p, op.Id).State); });
Test("089 operations: expired pending request never dispatched", f => { var p = f.Principal(); var op = f.Ops.Submit(p, "k", "application.start", J("{}"), 1); f.Clock.Advance(TimeSpan.FromSeconds(2)); True(f.Ops.TakeNext(f.Adapter, f.Sid) is null); Eq("failed", f.Ops.Get(p, op.Id).State); });
Test("090 operations: wrong adapter cannot complete", f => { var op = f.SubmitRunning(); Fault("forbidden", () => f.Ops.Complete(op.Id, RcProtocol.Id(), f.Sid, J("{}"), null)); });
Test("091 operations: completion records success", f => { var p = f.Principal(); var op = f.Ops.Submit(p, "k", "application.start", J("{}")); f.Ops.TakeNext(f.Adapter, f.Sid); f.Ops.Complete(op.Id, f.Adapter, f.Sid, J("{\"ready\":true}"), null); Eq("succeeded", f.Ops.Get(p, op.Id).State); });
Test("092 operations: completion cannot be forged before dispatch", f => { var op = f.Ops.Submit(f.Principal(), "k", "application.start", J("{}")); Fault("operation_conflict", () => f.Ops.Complete(op.Id, f.Adapter, f.Sid, J("{}"), null)); });
Test("093 operations: duplicate completion is idempotent", f => { var op = f.SubmitRunning(); f.Ops.Complete(op.Id, f.Adapter, f.Sid, J("{}"), null); f.Ops.Complete(op.Id, f.Adapter, f.Sid, J("{}"), null); });
Test("094 operations: conflicting completion refused", f => { var op = f.SubmitRunning(); f.Ops.Complete(op.Id, f.Adapter, f.Sid, J("{}"), null); Fault("operation_conflict", () => f.Ops.Complete(op.Id, f.Adapter, f.Sid, J("{\"changed\":true}"), null)); });
Test("095 operations: crash never redispatches running operation", f => { var op = f.SubmitRunning(); f.Ops.Recover(); Eq("outcome_unknown", f.Store.Read(s => s.Operations[op.Id].State)); True(f.Ops.TakeNext(f.Adapter, f.Sid) is null); });
Test("096 operations: unknown outcome can be reconciled", f => { var op = f.SubmitRunning(); f.Ops.Recover(); f.Ops.Complete(op.Id, f.Adapter, f.Sid, J("{}"), null); Eq("succeeded", f.Store.Read(s => s.Operations[op.Id].State)); });
Test("097 operations: pending cancellation prevents dispatch", f => { var p = f.Principal(); var op = f.Ops.Submit(p, "k", "application.start", J("{}")); f.Ops.Cancel(p, op.Id); True(f.Ops.TakeNext(f.Adapter, f.Sid) is null); });
Test("098 operations: running cancellation is never falsely reported", f => { var p = f.Principal(); var op = f.Ops.Submit(p, "k", "application.start", J("{}")); f.Ops.TakeNext(f.Adapter, f.Sid); Fault("already_dispatched", () => f.Ops.Cancel(p, op.Id)); });
Test("099 operations: shutdown rejects new admission", f => { f.Ops.BeginStop(); Fault("service_stopping", () => f.Ops.Submit(f.Principal(), "k", "application.start", J("{}"))); });
Test("100 operations: revoked device cancels pending work", f => { var op = f.Ops.Submit(f.Principal(), "k", "application.start", J("{}")); f.Access.Revoke(f.Device.Id, f.Sid); Eq("cancelled", f.Store.Read(s => s.Operations[op.Id].State)); });
Test("101 storage: failed mutation leaves state unchanged", f => { var cursor = f.Store.Read(s => s.Cursor); Fails<InvalidOperationException>(() => f.Store.Write<bool>(s => { s.Cursor = 999; throw new InvalidOperationException(); })); Eq(cursor, f.Store.Read(s => s.Cursor)); });
Test("102 storage: returned snapshot cannot mutate live store", f => { var state = f.Store.Read(s => s); state.Devices.Clear(); True(f.Store.Read(s => s.Devices.Count) > 0); });
Test("103 storage: second writer refused", f => Fails<IOException>(() => { using var second = new StateStore(f.Directory); }));
Test("104 storage: durable state survives reopen", f => { var id = f.Store.Read(s => s.InstallationId); f.Access.Dispose(); f.Store.Dispose(); using var reopened = new StateStore(f.Directory); Eq(id, reopened.Read(s => s.InstallationId)); Eq(1, reopened.Read(s => s.Devices.Count)); });
Test("105 storage: corrupt state fails closed", f => { f.Access.Dispose(); f.Store.Dispose(); File.WriteAllText(Path.Combine(f.Directory, "state.json"), "{bad}"); Fails<JsonException>(() => { using var reopened = new StateStore(f.Directory); }); });
Test("106 storage: user-profile scratch refused", f => Fails<InvalidDataException>(() => StateStore.RejectProfileOrReparse(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "rc-test"))));
Test("107 schedules: timezone required", f => Fault("invalid_schedule", () => Operations.NextOccurrence("Not/AZone", "08:00", [1], f.Clock.GetUtcNow())));
Test("108 schedules: malformed local time refused", f => Fault("invalid_schedule", () => Operations.NextOccurrence("UTC", "25:00", [1], f.Clock.GetUtcNow())));
Test("109 schedules: duplicate weekdays refused", f => Fault("invalid_schedule", () => Operations.NextOccurrence("UTC", "08:00", [1, 1], f.Clock.GetUtcNow())));
Test("110 schedules: due instant is strictly in the future", f => True(Operations.NextOccurrence("UTC", "12:00", [1], f.Clock.GetUtcNow()) > f.Clock.GetUtcNow()));
Test("111 schedules: create persists owner and revision", f => { var p = f.Principal(); var s = f.Ops.SaveSchedule(p, f.Schedule(p), null); Eq(1L, s.Revision); Eq(p.BindingId, s.BindingId); });
Test("112 schedules: stale revision rejected", f => { var p = f.Principal(); var s = f.Ops.SaveSchedule(p, f.Schedule(p), null); Fault("revision_conflict", () => f.Ops.SaveSchedule(p, s, 99)); });
Test("113 schedules: delete requires matching revision", f => { var p = f.Principal(); var s = f.Ops.SaveSchedule(p, f.Schedule(p), null); Fault("revision_conflict", () => f.Ops.RemoveSchedule(p, s.Id, 99)); });
Test("114 schedules: stopped host dispatches nothing", f => { var p = f.Principal(); f.Ops.SaveSchedule(p, f.Schedule(p), null); f.Clock.Advance(TimeSpan.FromMinutes(1)); f.Ops.BeginStop(); Eq(0, f.Ops.Tick()); });
Test("115 schedules: due execution produces a single receipt", f => { var p = f.Principal(); f.Ops.SaveSchedule(p, f.Schedule(p), null); f.Clock.Advance(TimeSpan.FromMinutes(1)); Eq(1, f.Ops.Tick()); Eq(0, f.Ops.Tick()); Eq(1, f.Store.Read(s => s.Operations.Count)); });
Test("116 schedules: missed skip policy prevents catch-up burst", f => { var p = f.Principal(); f.Ops.SaveSchedule(p, f.Schedule(p), null); f.Clock.Advance(TimeSpan.FromHours(1)); Eq(0, f.Ops.Tick()); });
Test("117 schedules: bounded catch-up runs once", f => { var p = f.Principal(); f.Ops.SaveSchedule(p, f.Schedule(p) with { MissedRun = "catch-up-once" }, null); f.Clock.Advance(TimeSpan.FromMinutes(5)); Eq(1, f.Ops.Tick()); Eq(0, f.Ops.Tick()); });
Test("118 schedules: catch-up outside grace window skipped", f => { var p = f.Principal(); f.Ops.SaveSchedule(p, f.Schedule(p) with { MissedRun = "catch-up-once" }, null); f.Clock.Advance(TimeSpan.FromMinutes(20)); Eq(0, f.Ops.Tick()); });
Test("119 schedules: revoked creator cannot dispatch future work", f => { var p = f.Principal(); f.Ops.SaveSchedule(p, f.Schedule(p), null); f.Access.Revoke(f.Device.Id, f.Sid); f.Clock.Advance(TimeSpan.FromMinutes(1)); Eq(0, f.Ops.Tick()); Eq(1, f.Store.Read(s => s.Schedules.Count)); });
Test("120 events: retention gap requires resynchronization", f => { var p = f.Principal(); f.Store.Write(s => { for (int i = 0; i < 600; i++) StateStore.Event(s, f.Clock, "test"); return true; }); Fault("resync_required", () => f.Ops.Events(p, 1)); Eq(512, f.Store.Read(s => s.Events.Count)); });

Test("121 storage: locked durable commit leaves memory and disk unchanged", f =>
{
    var before = f.Store.Read(s => s.Cursor);
    using (var locked = new FileStream(Path.Combine(f.Directory, "state.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        var refused = false;
        try { f.Store.Write(s => { s.Cursor += 100; return true; }); }
        catch (IOException) { refused = true; }
        catch (UnauthorizedAccessException) { refused = true; }
        Eq(true, refused); Eq(before, f.Store.Read(s => s.Cursor));
    }
    Eq(before, JsonSerializer.Deserialize<DurableState>(File.ReadAllText(Path.Combine(f.Directory, "state.json")), RcProtocol.Json)!.Cursor);
});
Test("122 operations: disabled account cannot block other adapter work", f =>
{
    f.Ops.Submit(f.Principal(), "disabled", "application.start", JsonSerializer.SerializeToElement(new { }));
    f.DisableBinding();
    Eq<OperationReceipt?>(null, f.Ops.TakeNext(f.Adapter, f.Sid));
    Eq("cancelled", f.Store.Read(s => s.Operations.Values.Single().State));
});

Test("123 operations: cancellation cannot address an unknown conversation", f => Fault("not_found", () => f.Ops.Submit(f.Principal(), "cancel", "conversation.cancel", J("{\"operation_id\":\"" + RcProtocol.Id() + "\"}"))));
Test("124 operations: cancellation cannot address a different operation kind", f => { var p = f.Principal(); var o = f.Ops.Submit(p, "start", "application.start", J("{}")); Fault("not_found", () => f.Ops.Submit(p, "cancel", "conversation.cancel", J("{\"operation_id\":\"" + o.Id + "\"}"))); });
Test("125 operations: cancellation can address its owned conversation", f => { var p = f.Principal(); var o = f.Ops.Submit(p, "chat", "conversation.submit", J("{\"text\":\"Hello\"}")); Eq("accepted", f.Ops.Submit(p, "cancel", "conversation.cancel", J("{\"operation_id\":\"" + o.Id + "\"}")).State); });
Test("126 operations: cancellation cannot address another account's conversation", f =>
{
    var binding = f.Access.RegisterBinding("spanish", RcProtocol.Id(), "Other account", f.Sid, f.Adapter);
    var invitation = f.Access.CreateInvitation(binding.Id, ["chat"], f.Sid);
    var enrollment = f.Access.Enroll(invitation.Invitation, "Other device", f.PublicKey);
    var device = f.Access.Approve(enrollment.Id, enrollment.Fingerprint, f.Sid);
    var other = f.Access.Authenticate(f.Login(device.Id).AccessToken);
    var operation = f.Ops.Submit(other, "other-chat", "conversation.submit", J("{\"text\":\"Private\"}"));
    Fault("not_found", () => f.Ops.Submit(f.Principal(), "cancel", "conversation.cancel", J("{\"operation_id\":\"" + operation.Id + "\"}")));
    Eq("accepted", f.Ops.Get(other, operation.Id).State);
});
Test("127 operations: unavailable speech is refused before creating a receipt", f =>
{
    var enrollment = f.Pending(["audio"]);
    var device = f.Access.Approve(enrollment.Id, enrollment.Fingerprint, f.Sid);
    var principal = f.Access.Authenticate(f.Login(device.Id).AccessToken);
    Fault("capability_unavailable", () => f.Ops.Submit(principal, "audio", "audio.transcribe", J("{\"audio_id\":\"" + RcProtocol.Id() + "\"}")));
    Eq(0, f.Store.Read(s => s.Operations.Count));
});

AdapterChecks.Register(Test);
Test("193 history: conversation permission is required", f => Fault("forbidden", () => f.Ops.Submit(f.Principal() with { Scopes = ["monitor"] }, "history", "conversation.history", J("{}"))));
Test("194 history: browser cannot select a different account", f => Fault("invalid_request", () => Operations.ValidateOperation("conversation.history", J("{\"account_id\":\"other\"}"))));
Test("195 history: a typed empty request is supported", f => Operations.ValidateOperation("conversation.history", J("{}")));
Test("196 disconnect: a lost chat adapter produces an unknown outcome", f => { var op = f.Ops.Submit(f.Principal(), "lost", "conversation.submit", J("{\"text\":\"Hello\"}")); f.Ops.TakeNext(f.Adapter, f.Sid, "application"); f.Ops.AdapterDisconnected(f.Adapter); Eq("outcome_unknown", f.Store.Read(s => s.Operations[op.Id].State)); True(f.Ops.TakeNext(f.Adapter, f.Sid, "application") is null); });
Test("197 disconnect: another adapter cannot change running work", f => { var op = f.Ops.Submit(f.Principal(), "lost", "conversation.submit", J("{\"text\":\"Hello\"}")); f.Ops.TakeNext(f.Adapter, f.Sid, "application"); f.Ops.AdapterDisconnected(RcProtocol.Id()); Eq("running", f.Store.Read(s => s.Operations[op.Id].State)); });
Test("198 disconnect: an application loss leaves the XT launch separate", f => { var op = f.SubmitRunning(); f.Ops.AdapterDisconnected(f.Adapter); Eq("running", f.Store.Read(s => s.Operations[op.Id].State)); f.Ops.AdapterDisconnected("", true); Eq("outcome_unknown", f.Store.Read(s => s.Operations[op.Id].State)); });
Test("199 disconnect: queued work is not treated as already dispatched", f => { var op = f.Ops.Submit(f.Principal(), "lost", "conversation.submit", J("{\"text\":\"Hello\"}")); f.Ops.AdapterDisconnected(f.Adapter); Eq("accepted", f.Store.Read(s => s.Operations[op.Id].State)); });
Test("200 disconnect: the owning adapter may reconcile its late result", f => { var op = f.Ops.Submit(f.Principal(), "lost", "conversation.submit", J("{\"text\":\"Hello\"}")); f.Ops.TakeNext(f.Adapter, f.Sid, "application"); f.Ops.AdapterDisconnected(f.Adapter); f.Ops.CompleteApplication(op.Id, f.Adapter, f.Sid, J("{\"text\":\"Observed final answer\"}"), null); Eq("succeeded", f.Store.Read(s => s.Operations[op.Id].State)); });

Test("201 disable: dispatched work records uncertainty without a heartbeat", f =>
{
    var op = f.Ops.Submit(f.Principal(), "disable-running", "conversation.submit", J("{\"text\":\"Hello\"}"));
    f.Ops.TakeNext(f.Adapter, f.Sid, "application");
    f.Ops.Progress(op.Id, f.Adapter, f.Sid, J("{\"type\":\"permission\",\"request_id\":\"pending-request\"}"));
    f.Access.DisableBinding(f.Binding.Id, f.Adapter, f.Sid);
    var stored = f.Store.Read(s => s.Operations[op.Id]);
    Eq("outcome_unknown", stored.State); Eq("account_disabled", stored.Error); True(stored.PendingPermission is null);
});
Test("202 disable: late owned completion reconciles the disabled account", f =>
{
    var op = f.Ops.Submit(f.Principal(), "disable-late", "conversation.submit", J("{\"text\":\"Hello\"}"));
    f.Ops.TakeNext(f.Adapter, f.Sid, "application");
    f.Access.DisableBinding(f.Binding.Id, f.Adapter, f.Sid);
    f.Ops.CompleteApplication(op.Id, f.Adapter, f.Sid, J("{\"cancelled\":true}"), null);
    var stored = f.Store.Read(s => s.Operations[op.Id]);
    Eq("succeeded", stored.State); True(stored.Error is null); True(stored.Result!.Value.GetProperty("cancelled").GetBoolean());
});
Test("203 disable: an already claimed launch also records uncertainty", f =>
{
    var op = f.SubmitRunning();
    f.Access.DisableBinding(f.Binding.Id, f.Adapter, f.Sid);
    Eq("outcome_unknown", f.Store.Read(s => s.Operations[op.Id].State));
});

AdapterChecks.RegisterRevocationCases(Test);
StopChecks.Register(Test);
MobileChecks.Register(Test);

// Permission edits are trusted local actions; an approved browser keeps its key.
Test("268 permissions: Start can be added without granting Stop", f => { var e=f.Pending(["monitor","chat"]); var d=f.Access.Approve(e.Id,e.Fingerprint,f.Sid); var u=f.Access.ChangeDeviceScopes(d.Id,["monitor","chat","launch"],d.Revision,f.Sid); var p=f.Access.Authenticate(f.Login(d.Id).AccessToken); True(p.Scopes.Contains("launch")); True(!p.Scopes.Contains("stop")); Eq("accepted",f.Ops.Submit(p,"new-start","application.start",J("{}")).State); });
Test("269 permissions: identity key account and enrollment are preserved", f => { var u=f.Access.ChangeDeviceScopes(f.Device.Id,["chat"],f.Device.Revision,f.Sid); Eq(f.Device.Id,u.Id); Eq(f.Device.PublicKey,u.PublicKey); Eq(f.Device.BindingId,u.BindingId); Eq(f.Device.EnrolledAt,u.EnrolledAt); Eq(f.Device.Revision+1,u.Revision); True(!u.Revoked); });
Test("270 permissions: wrong local owner is refused", f => Fault("forbidden",()=>f.Access.ChangeDeviceScopes(f.Device.Id,["chat"],f.Device.Revision,"S-1-5-21-123-456-789-1002")));
Test("271 permissions: stale review cannot overwrite a newer grant", f => { f.Access.ChangeDeviceScopes(f.Device.Id,["chat"],f.Device.Revision,f.Sid); Fault("revision_conflict",()=>f.Access.ChangeDeviceScopes(f.Device.Id,["launch"],f.Device.Revision,f.Sid)); True(f.Store.Read(s=>s.Devices[f.Device.Id].Scopes.SequenceEqual(new[]{"chat"}))); });
Test("272 permissions: revoked browser cannot be revived by editing", f => { f.Access.Revoke(f.Device.Id,f.Sid); Fault("device_unavailable",()=>f.Access.ChangeDeviceScopes(f.Device.Id,["launch"],f.Device.Revision,f.Sid)); });
Test("273 permissions: disabled account cannot change permissions", f => { f.DisableBinding(); Fault("account_unavailable",()=>f.Access.ChangeDeviceScopes(f.Device.Id,["chat"],f.Device.Revision,f.Sid)); });
Test("274 permissions: invalid and empty grants are refused", f => { Fault("invalid_request",()=>f.Access.ChangeDeviceScopes(f.Device.Id,[],1,f.Sid)); Fault("invalid_request",()=>f.Access.ChangeDeviceScopes(f.Device.Id,["arbitrary"],1,f.Sid)); Fault("invalid_request",()=>f.Access.ChangeDeviceScopes(f.Device.Id,["chat","chat"],1,f.Sid)); });
Test("275 permissions: changed grant ends existing sessions and tickets", f => { var login=f.Login();var p=f.Access.Authenticate(login.AccessToken);var ticket=f.Access.IssueTicket(login.AccessToken); f.Access.ChangeDeviceScopes(f.Device.Id,["chat"],f.Device.Revision,f.Sid); True(p.Revoked.IsCancellationRequested); Fault("unauthorized",()=>f.Access.Authenticate(login.AccessToken)); Fault("unauthorized",()=>f.Access.ConsumeTicket(ticket)); });
Test("276 permissions: unchanged selection keeps sessions and revision", f => { var login=f.Login();var u=f.Access.ChangeDeviceScopes(f.Device.Id,f.Device.Scopes.Reverse().ToArray(),f.Device.Revision,f.Sid); Eq(f.Device.Revision,u.Revision); Eq(f.Device.Id,f.Access.Authenticate(login.AccessToken).DeviceId); });
Test("277 permissions: another browser keeps its session and grant", f => { var e=f.Pending(["monitor"]);var d=f.Access.Approve(e.Id,e.Fingerprint,f.Sid);var login=f.Login(d.Id);f.Access.ChangeDeviceScopes(f.Device.Id,["chat"],f.Device.Revision,f.Sid);Eq(d.Id,f.Access.Authenticate(login.AccessToken,"monitor").DeviceId); });
Test("278 permissions: removed launch cancels queued start", f => {var p=f.Principal();var op=f.Ops.Submit(p,"queued","application.start",J("{}"));f.Access.ChangeDeviceScopes(f.Device.Id,["chat"],f.Device.Revision,f.Sid);Eq("cancelled",f.Store.Read(s=>s.Operations[op.Id].State));True(f.Ops.TakeNext("",f.Sid,"broker") is null);});
Test("279 permissions: removed authority records running work uncertain", f => {var op=f.SubmitRunning();f.Access.ChangeDeviceScopes(f.Device.Id,["chat"],f.Device.Revision,f.Sid);Eq("outcome_unknown",f.Store.Read(s=>s.Operations[op.Id].State));Eq("permissions_changed",f.Store.Read(s=>s.Operations[op.Id].Error));});
Test("280 permissions: adding authority preserves existing queued chat", f => {var op=f.Ops.Submit(f.Principal(),"chat","conversation.submit",J("{\"text\":\"Hello\"}"));f.Access.ChangeDeviceScopes(f.Device.Id,[.. f.Device.Scopes,"stop"],f.Device.Revision,f.Sid);Eq("accepted",f.Store.Read(s=>s.Operations[op.Id].State));});
Test("281 permissions: removed schedule authority prevents dispatch", f => {var p=f.Principal();f.Ops.SaveSchedule(p,f.Schedule(p),null);f.Access.ChangeDeviceScopes(f.Device.Id,["chat","launch"],f.Device.Revision,f.Sid);f.Clock.Advance(TimeSpan.FromMinutes(1));Eq(0,f.Ops.Tick());Eq(1,f.Store.Read(s=>s.Schedules.Count));});
Test("282 permissions: removing chat clears pending consent", f => {var op=f.Ops.Submit(f.Principal(),"chat","conversation.submit",J("{\"text\":\"Hello\"}"));f.Ops.TakeNext(f.Adapter,f.Sid,"application");f.Ops.Progress(op.Id,f.Adapter,f.Sid,J("{\"type\":\"permission\",\"request_id\":\"pending-request\"}"));f.Access.ChangeDeviceScopes(f.Device.Id,["monitor"],f.Device.Revision,f.Sid);True(f.Store.Read(s=>s.Operations[op.Id].PendingPermission is null));Eq("outcome_unknown",f.Store.Read(s=>s.Operations[op.Id].State));});
Test("283 permissions: stale in-flight principal cannot admit removed authority", f => {var p=f.Principal() with {Revoked=CancellationToken.None};f.Access.ChangeDeviceScopes(f.Device.Id,["monitor"],f.Device.Revision,f.Sid);Fault("unauthorized",()=>f.Ops.Submit(p,"stale","application.start",J("{}")));});
Test("284 permissions: failed durable update keeps grant and session", f => {var login=f.Login();using(var locked=new FileStream(Path.Combine(f.Directory,"state.json"),FileMode.Open,FileAccess.Read,FileShare.Read)){try{f.Access.ChangeDeviceScopes(f.Device.Id,["chat"],f.Device.Revision,f.Sid);throw new Exception("Expected durable write failure");}catch(IOException){}catch(UnauthorizedAccessException){}}Eq(f.Device.Revision,f.Store.Read(s=>s.Devices[f.Device.Id].Revision));True(f.Access.Authenticate(login.AccessToken).Scopes.Contains("launch"));});
Test("285 permissions: durable update survives reopening the store", f => {var u=f.Access.ChangeDeviceScopes(f.Device.Id,["chat","launch"],f.Device.Revision,f.Sid);var saved=JsonSerializer.Deserialize<DurableState>(File.ReadAllText(Path.Combine(f.Directory,"state.json")),RcProtocol.Json)!;True(saved.Devices[u.Id].Scopes.SequenceEqual(new[]{"chat","launch"}));Eq(u.Revision,saved.Devices[u.Id].Revision);});


// Approval resolution must follow the request identity across every client.
static OperationReceipt PendingChat(Fixture f, string requestId = "approval-a")
{
    var op = f.Ops.Submit(f.Principal(), "approval-chat", "conversation.submit", J("{\"text\":\"Read-only fixture\"}"));
    f.Ops.TakeNext(f.Adapter, f.Sid, "application");
    f.Ops.Progress(op.Id, f.Adapter, f.Sid, JsonSerializer.SerializeToElement(new {type="permission", request_id=requestId}));
    return op;
}
foreach (var decision in new[]{"proceed", "deny"}) Test("286 resolution: " + decision + " removes only its pending prompt", f =>
{
    var op=PendingChat(f);
    f.Ops.Progress(op.Id,f.Adapter,f.Sid,JsonSerializer.SerializeToElement(new {type="permission",request_id="approval-a",decision}));
    True(f.Ops.Get(f.Principal(),op.Id).PendingPermission is null);
});
Test("287 resolution: old decision cannot dismiss a newer prompt", f =>
{
    var op=PendingChat(f);
    f.Ops.Progress(op.Id,f.Adapter,f.Sid,J("{\"type\":\"permission\",\"request_id\":\"approval-b\"}"));
    f.Ops.Progress(op.Id,f.Adapter,f.Sid,J("{\"type\":\"permission\",\"request_id\":\"approval-a\",\"decision\":\"proceed\"}"));
    Eq("approval-b",f.Ops.Get(f.Principal(),op.Id).PendingPermission!.Value.GetProperty("request_id").GetString());
});
Test("288 resolution: a delayed prompt cannot reopen a resolved request", f =>
{
    var op=PendingChat(f);
    f.Ops.Progress(op.Id,f.Adapter,f.Sid,J("{\"type\":\"permission\",\"request_id\":\"approval-a\",\"decision\":\"deny\"}"));
    f.Ops.Progress(op.Id,f.Adapter,f.Sid,J("{\"type\":\"permission\",\"request_id\":\"approval-a\"}"));
    True(f.Ops.Get(f.Principal(),op.Id).PendingPermission is null);
});
Test("289 resolution: invalid decisions preserve pending consent", f =>
{
    var op=PendingChat(f);
    foreach(var decision in new[]{"null","true","123","\"allow\""})
        Fault("invalid_request",()=>f.Ops.Progress(op.Id,f.Adapter,f.Sid,J("{\"type\":\"permission\",\"request_id\":\"approval-a\",\"decision\":"+decision+"}")));
    True(f.Ops.Get(f.Principal(),op.Id).PendingPermission is not null);
});
Test("290 resolution: competing client decisions dispatch only once", f =>
{
    var op=PendingChat(f);
    JsonElement Answer(string decision)=>JsonSerializer.SerializeToElement(new {operation_id=op.Id,request_id="approval-a",decision});
    f.Ops.Submit(f.Principal(),"first-answer","conversation.permission",Answer("deny"));
    f.Ops.Submit(f.Principal(),"second-answer","conversation.permission",Answer("proceed"));
    Eq("deny",f.Ops.TakeNext(f.Adapter,f.Sid,"application",true)!.Payload.GetProperty("decision").GetString());
    True(f.Ops.TakeNext(f.Adapter,f.Sid,"application",true) is null);
    f.Ops.Progress(op.Id,f.Adapter,f.Sid,J("{\"type\":\"permission\",\"request_id\":\"approval-a\"}"));
    True(f.Ops.Get(f.Principal(),op.Id).PendingPermission is null);
});
Test("291 resolution: foreign adapters cannot clear approval", f =>
{
    var op=PendingChat(f);
    Fault("forbidden",()=>f.Ops.Progress(op.Id,RcProtocol.Id(),f.Sid,J("{\"type\":\"permission\",\"request_id\":\"approval-a\",\"decision\":\"deny\"}")));
    True(f.Ops.Get(f.Principal(),op.Id).PendingPermission is not null);
});

Test("292 resolution: restart rejects stale progress without changing the stored contract", f =>
{
    var op=PendingChat(f);
    f.Ops.Progress(op.Id,f.Adapter,f.Sid,J("{\"type\":\"permission\",\"request_id\":\"approval-a\",\"decision\":\"deny\"}"));
    True(!File.ReadAllText(Path.Combine(f.Directory,"state.json")).Contains("resolved_permission_ids",StringComparison.Ordinal));
    var recovered = new Operations(f.Store,f.Clock);
    recovered.Recover();
    Fault("operation_conflict",()=>recovered.Progress(op.Id,f.Adapter,f.Sid,J("{\"type\":\"permission\",\"request_id\":\"approval-a\"}")));
    Eq("outcome_unknown",recovered.Get(f.Principal(),op.Id).State);
});
Test("293 resolution: the answered list forgets its oldest answer first", f =>
{
    var op=PendingChat(f);
    for (var i = 0; i < 257; i++)
        f.Ops.Progress(op.Id,f.Adapter,f.Sid,JsonSerializer.SerializeToElement(new {type="permission",request_id="answered-"+i,decision="deny"}));
    // Answer 1 is still remembered, so its late prompt stays hidden.
    f.Ops.Progress(op.Id,f.Adapter,f.Sid,JsonSerializer.SerializeToElement(new {type="permission",request_id="answered-1"}));
    Eq("approval-a",f.Ops.Get(f.Principal(),op.Id).PendingPermission!.Value.GetProperty("request_id").GetString());
    // Answer 0 was the oldest of 257 and was forgotten, so its prompt can show again.
    f.Ops.Progress(op.Id,f.Adapter,f.Sid,JsonSerializer.SerializeToElement(new {type="permission",request_id="answered-0"}));
    Eq("answered-0",f.Ops.Get(f.Principal(),op.Id).PendingPermission!.Value.GetProperty("request_id").GetString());
});

var results = new List<object>(); var failed = 0;
foreach (var test in tests)
{
    var watch = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        using var fixture = new Fixture(Path.Combine(output, "cases", test.Name[..3] + "-" + RcProtocol.Id()));
        test.Run(fixture);
        Console.WriteLine("PASS " + test.Name);
        results.Add(new { name = test.Name, passed = true, milliseconds = watch.ElapsedMilliseconds });
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine("FAIL " + test.Name + " :: " + ex.Message);
        results.Add(new { name = test.Name, passed = false, milliseconds = watch.ElapsedMilliseconds, error = ex.ToString() });
    }
}
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { total = tests.Count, passed = tests.Count - failed, failed, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"RC core checks: {tests.Count - failed}/{tests.Count} passed; {failed} failed.");
return failed == 0 ? 0 : 1;

sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 1, 5, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}
sealed class Fixture : IDisposable
{
    public readonly string Directory;
    public readonly string Sid = "S-1-5-21-123-456-789-1001";
    public readonly string Adapter = RcProtocol.Id();
    public readonly TestClock Clock = new();
    public readonly RcConfiguration Config = new();
    public readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public readonly StateStore Store;
    public readonly Access Access;
    public readonly Operations Ops;
    public readonly AccountBinding Binding;
    public readonly DeviceGrant Device;
    public string PublicKey => Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());
    public Fixture(string directory)
    {
        Directory = directory; Store = new(directory); Access = new(Store, Config, Clock); Ops = new(Store, Clock);
        Binding = Access.RegisterBinding("english", RcProtocol.Id(), "Test account", Sid, Adapter);
        var enrollment = Pending(); Device = Access.Approve(enrollment.Id, enrollment.Fingerprint, Sid);
    }
    public InvitationResult Invite(string[]? scopes = null) => Access.CreateInvitation(Binding.Id, scopes ?? ["monitor", "chat", "launch", "schedules"], Sid);
    public EnrollmentResult Pending(string[]? scopes = null) => Access.Enroll(Invite(scopes).Invitation, "Test phone", PublicKey);
    public string Sign(ChallengeResult c) => Convert.ToBase64String(Key.SignData(Convert.FromBase64String(c.Challenge), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    public SessionResult Login(string? deviceId = null) { var c = Access.CreateChallenge(deviceId ?? Device.Id); return Access.Establish(deviceId ?? Device.Id, c.Id, Sign(c)); }
    public Principal Principal() => Access.Authenticate(Login().AccessToken);
    public void DisableBinding() => Store.Write(s => { s.Bindings[Binding.Id] = Binding with { Enabled = false }; return true; });
    public OperationReceipt SubmitRunning() { var op = Ops.Submit(Principal(), "k", "application.start", JsonSerializer.SerializeToElement(new { })); Ops.TakeNext(Adapter, Sid); return op; }
    public RcSchedule Schedule(Principal p) => new("", p.BindingId, "Daily task", "application.start", JsonSerializer.SerializeToElement(new { }), "UTC", "12:01", [0,1,2,3,4,5,6], "skip", true, 0, default);
    public void Dispose() { Access.Dispose(); Store.Dispose(); Key.Dispose(); }
}
