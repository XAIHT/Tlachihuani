// Tlachihuani — Angela López Mendoza · XAIHT
using System.Globalization;
using System.Text.Json;

namespace Tlachihuani.RC;

public sealed class Operations(StateStore store, TimeProvider clock)
{
    private int accepting = 1;
    // Accessed only inside store.Write's serialized mutation. This bounded UI
    // replay guard is deliberately transient: recovery ends running authority,
    // so an earlier process cannot resume progress. Durable receipt format stays
    // compatible with existing installations and older readers.
    // Answered approvals per running conversation, so a delayed prompt cannot reopen
    // one. Each list keeps the newest 256 (oldest out first) and is dropped once its
    // operation stops running.
    private sealed class ResolvedRequests
    {
        private readonly HashSet<string> ids = new(StringComparer.Ordinal);
        private readonly Queue<string> order = new();
        public bool Contains(string id) => ids.Contains(id);
        public void Add(string id)
        {
            if (!ids.Add(id)) return;
            order.Enqueue(id);
            while (order.Count > 256) ids.Remove(order.Dequeue());
        }
    }
    private readonly Dictionary<string, ResolvedRequests> resolvedPermissions = new(StringComparer.Ordinal);
    public bool Accepting => Volatile.Read(ref accepting) == 1;
    public void BeginStop() => Interlocked.Exchange(ref accepting, 0);
    public static void Permit(Principal principal, string scope)
    {
        if (principal.Revoked.IsCancellationRequested) throw new RcFault("unauthorized", "Session expired or revoked.", 401);
        if (!principal.Scopes.Contains(scope, StringComparer.Ordinal)) throw new RcFault("forbidden", "Permission is required.", 403);
    }
    private void Admitting() { if (!Accepting) throw new RcFault("service_stopping", "Remote-Controller is stopping.", 503); }
    public OperationReceipt Submit(Principal principal, string key, string operation, JsonElement payload, int deadlineSeconds = 120)
    {
        Admitting();
        ValidateOperation(operation, payload);
        Permit(principal, RcProtocol.Operations[operation]);
        key = RcProtocol.Text(key, "idempotency key", 128);
        if (deadlineSeconds is < 1 or > 300) throw new RcFault("invalid_request", "Invalid operation deadline.", 400);
        return store.Write(s =>
        {
            Admitting(); VerifyPrincipal(s, principal);
            return Admit(s, principal.BindingId, principal.DeviceId, key, operation, payload, deadlineSeconds);
        });
    }
    private OperationReceipt Admit(DurableState s, string binding, string device, string key, string operation, JsonElement payload, int deadlineSeconds)
    {
        var digest = RcProtocol.Hash(operation + "\n" + Canonical(payload) + "\n" + deadlineSeconds);
        var previous = s.Operations.Values.FirstOrDefault(o => o.BindingId == binding && o.Key == key);
        if (previous is not null)
        {
            if (previous.Digest != digest) throw new RcFault("idempotency_conflict", "This request key already identifies different work.");
            return previous;
        }
        if (operation is "conversation.cancel" or "conversation.permission")
        {
            var target = payload.GetProperty("operation_id").GetString()!;
            if (!s.Operations.TryGetValue(target, out var owned) || owned.BindingId != binding || owned.Kind != "conversation.submit")
                throw new RcFault("not_found", "Conversation operation not found.", 404);
            if (operation == "conversation.permission" && (!HasConversationAuthority(s, owned) || owned.PendingPermission is not JsonElement progress
                || !progress.TryGetProperty("type", out var type) || type.GetString() != "permission"
                || !progress.TryGetProperty("request_id", out var request) || request.GetString() != payload.GetProperty("request_id").GetString()))
                throw new RcFault("permission_expired", "This execution approval is no longer pending.");
        }
        if (operation == "audio.transcribe")
            throw new RcFault("capability_unavailable", "Speech input is not available in this service version.", 409);
        if (s.Operations.Count >= 10000 || s.Operations.Values.Count(o => o.BindingId == binding && o.State is "accepted" or "running") >= 32)
            throw new RcFault("queue_limit", "The operation queue is full.", 429);
        var now = clock.GetUtcNow();
        var item = new OperationReceipt(RcProtocol.Id(), binding, device, key, digest, operation, payload.Clone(), now, now.AddSeconds(deadlineSeconds));
        s.Operations.Add(item.Id, item);
        StateStore.Event(s, clock, "operation.accepted", binding, item.Id, "accepted");
        return item;
    }
    public OperationReceipt Get(Principal principal, string id) => store.Read(s =>
    {
        VerifyPrincipal(s, principal);
        if (!s.Operations.TryGetValue(id, out var operation) || operation.BindingId != principal.BindingId) throw new RcFault("not_found", "Operation not found.", 404);
        Permit(principal, RcProtocol.Operations[operation.Kind]);
        return operation;
    });
    public void Cancel(Principal principal, string id)
    {
        store.Write(s =>
        {
            VerifyPrincipal(s, principal);
            if (!s.Operations.TryGetValue(id, out var operation) || operation.BindingId != principal.BindingId) throw new RcFault("not_found", "Operation not found.", 404);
            Permit(principal, RcProtocol.Operations[operation.Kind]);
            if (operation.State == "accepted")
            {
                s.Operations[id] = operation with { State = "cancelled", Error = "user_cancelled" };
                StateStore.Event(s, clock, "operation.cancelled", principal.BindingId, id, "cancelled");
            }
            else if (operation.State == "running") throw new RcFault("already_dispatched", "The application has accepted this operation. Request its supported cancellation action.");
            return true;
        });
    }
    public OperationReceipt? TakeNext(string adapterId, string windowsSid, string role = "all", bool controlsOnly = false)
    {
        Admitting();
        return store.Write(s =>
        {
            Admitting();
            foreach (var item in s.Operations.Values.Where(o => o.State == "accepted").OrderBy(o => o.Kind is "conversation.cancel" or "conversation.permission" ? 0 : 1).ThenBy(o => o.CreatedAt).ToArray())
            {
                if (!s.Bindings.TryGetValue(item.BindingId, out var binding) || !binding.Enabled)
                { s.Operations[item.Id] = item with { State = "cancelled", Error = "account_unavailable" }; continue; }
                if ((role != "broker" && binding.AdapterId != adapterId) || binding.WindowsSid != windowsSid) continue;
                if (role == "broker" ? item.Kind is not ("application.start" or "application.stop") : role == "application" && item.Kind is "application.start" or "application.stop") continue;
                if (controlsOnly && item.Kind == "conversation.submit") continue;
                if (item.Deadline <= clock.GetUtcNow()) { s.Operations[item.Id] = item with { State = "failed", Error = "deadline_exceeded" }; continue; }
                if (!s.Devices.TryGetValue(item.DeviceId, out var device) || device.Revoked || !device.Scopes.Contains(RcProtocol.Operations[item.Kind], StringComparer.Ordinal))
                { s.Operations[item.Id] = item with { State = "cancelled", Error = "authorization_changed" }; continue; }
                var running = item with { State = "running", AdapterOperationId = item.Id };
                if (item.Kind == "conversation.permission")
                {
                    var target = item.Payload.GetProperty("operation_id").GetString()!;
                    if (!s.Operations.TryGetValue(target, out var owner) || owner.BindingId != item.BindingId
                        || !HasConversationAuthority(s, owner) || owner.PendingPermission is not JsonElement pending
                        || pending.GetProperty("request_id").GetString() != item.Payload.GetProperty("request_id").GetString())
                    { s.Operations[item.Id] = item with { State = "failed", Error = "permission_expired" }; continue; }
                    RememberPermission(s, owner.Id, pending.GetProperty("request_id").GetString()!);
                    s.Operations[target] = owner with { PendingPermission = null };
                }
                s.Operations[item.Id] = running;
                StateStore.Event(s, clock, "operation.dispatched", binding.Id, item.Id, "running");
                return running;
            }
            return null;
        });
    }
    public void Progress(string operationId, string adapterId, string windowsSid, JsonElement progress)
    {
        if (progress.ValueKind != JsonValueKind.Object || progress.GetRawText().Length > 16384) throw new RcFault("invalid_request", "Invalid progress update.", 400);
        if (!progress.TryGetProperty("type", out var progressType) || progressType.ValueKind != JsonValueKind.String || progressType.GetString() is not ("message" or "permission" or "status"))
            throw new RcFault("invalid_request", "Unknown progress type.", 400);
        if (progressType.GetString() == "permission")
        {
            if (!progress.TryGetProperty("request_id", out var request) || request.ValueKind != JsonValueKind.String)
                throw new RcFault("invalid_request", "Permission request identity is required.", 400);
            _ = RcProtocol.Text(request.GetString(), "permission request", 128);
            if (progress.TryGetProperty("decision", out var decision)
                && (decision.ValueKind != JsonValueKind.String || decision.GetString() is not ("proceed" or "deny")))
                throw new RcFault("invalid_request", "Invalid permission decision.", 400);
        }
        store.Write(s =>
        {
            if (!s.Operations.TryGetValue(operationId, out var operation)) throw new RcFault("not_found", "Operation not found.", 404);
            var binding = Access.Binding(s, operation.BindingId);
            if (binding.AdapterId != adapterId || binding.WindowsSid != windowsSid || operation.Kind != "conversation.submit") throw new RcFault("forbidden", "Adapter cannot update this operation.", 403);
            if (operation.State != "running") throw new RcFault("operation_conflict", "Operation is no longer running.");
            _ = Access.Device(s, operation.DeviceId);
            var pending = operation.PendingPermission;
            resolvedPermissions.TryGetValue(operation.Id, out var resolved);
            if (progressType.GetString() == "permission")
            {
                var requestId = progress.GetProperty("request_id").GetString()!;
                if (progress.TryGetProperty("decision", out _))
                {
                    RememberPermission(s, operation.Id, requestId);
                    // A late resolution of A must never dismiss a newer prompt B.
                    if (pending?.GetProperty("request_id").GetString() == requestId) pending = null;
                }
                else
                {
                    if (resolved?.Contains(requestId) == true) return false;
                    pending = progress.Clone();
                }
            }
            s.Operations[operation.Id] = operation with { Progress = progress.Clone(), PendingPermission = pending };
            StateStore.Event(s, clock, "operation.progress", binding.Id, operation.Id, "running");
            return true;
        });
    }
    private void RememberPermission(DurableState s, string operationId, string requestId)
    {
        // Operations that are no longer running cannot receive prompts: forget them.
        foreach (var stale in resolvedPermissions.Keys.Where(id => !s.Operations.TryGetValue(id, out var o) || o.State != "running").ToArray())
            resolvedPermissions.Remove(stale);
        if (!resolvedPermissions.TryGetValue(operationId, out var requests))
            resolvedPermissions[operationId] = requests = new();
        requests.Add(requestId);
    }

    public void CompleteApplication(string operationId, string adapterId, string windowsSid, JsonElement? result, string? error)
    {
        var isLaunch = store.Read(s => s.Operations.TryGetValue(operationId, out var operation) && operation.Kind is "application.start" or "application.stop");
        if (isLaunch) throw new RcFault("forbidden", "Only XT may complete an application launch.", 403);
        Complete(operationId, adapterId, windowsSid, result, error);
    }
    public void Complete(string operationId, string adapterId, string windowsSid, JsonElement? result, string? error, bool broker = false)
    {
        if (result?.GetRawText().Length > 32768) throw new RcFault("invalid_request", "Operation result exceeds the limit.", 400);
        if (error is not null && (error.Length > 64 || error.Length == 0 || error.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))) throw new RcFault("invalid_request", "Invalid error code.", 400);
        store.Write(s =>
        {
            if (!s.Operations.TryGetValue(operationId, out var operation)) throw new RcFault("not_found", "Operation not found.", 404);
            var binding = s.Bindings[operation.BindingId];
            if (binding.WindowsSid != windowsSid || (broker ? operation.Kind is not ("application.start" or "application.stop") : binding.AdapterId != adapterId)) throw new RcFault("forbidden", "The adapter does not own this operation.", 403);
            var desired = error is null ? "succeeded" : "failed";
            if (operation.State is not ("running" or "outcome_unknown"))
            {
                if (operation.State == desired && operation.Error == error && operation.Result?.GetRawText() == result?.GetRawText()) return true;
                throw new RcFault("operation_conflict", "Operation is not awaiting an adapter result.");
            }
            s.Operations[operation.Id] = operation with { State = desired, Result = result?.Clone(), Error = error, Progress = null, PendingPermission = null, Payload = JsonSerializer.SerializeToElement(new { }) };
            resolvedPermissions.Remove(operation.Id);
            StateStore.Event(s, clock, "operation.completed", binding.Id, operation.Id, desired);
            return true;
        });
    }
    public void Recover() => store.Write(s =>
    {
        resolvedPermissions.Clear();
        foreach (var item in s.Operations.Values.ToArray())
        {
            if (item.State == "running") s.Operations[item.Id] = item with { State = "outcome_unknown", Error = "host_restarted" };
            else if (item.State == "accepted" && item.Deadline <= clock.GetUtcNow()) s.Operations[item.Id] = item with { State = "failed", Error = "deadline_exceeded" };
        }
        StateStore.Event(s, clock, "service.recovered"); return true;
    });
    public void AdapterDisconnected(string adapterId, bool broker = false)
    {
        bool Owned(DurableState s, OperationReceipt item) => item.State == "running"
            && (broker ? item.Kind is "application.start" or "application.stop" : item.Kind is not ("application.start" or "application.stop") && s.Bindings[item.BindingId].AdapterId == adapterId);
        if (!store.Read(s => s.Operations.Values.Any(item => Owned(s, item)))) return;
        store.Write(s =>
        {
            foreach (var item in s.Operations.Values.Where(item => Owned(s, item)).ToArray())
            {
                s.Operations[item.Id] = item with { State = "outcome_unknown", Error = "adapter_disconnected", PendingPermission = null };
                resolvedPermissions.Remove(item.Id);
                StateStore.Event(s, clock, "operation.uncertain", item.BindingId, item.Id, "outcome_unknown");
            }
            return true;
        });
    }
    public object Events(Principal principal, long cursor) => store.Read<object>(s =>
    {
        VerifyPrincipal(s, principal); Permit(principal, "monitor");
        if (cursor < 0 || cursor > s.Cursor) throw new RcFault("invalid_cursor", "Invalid event cursor.", 400);
        if (cursor != 0 && s.Events.Count > 0 && cursor < s.Events[0].Cursor - 1) throw new RcFault("resync_required", "Fetch a fresh snapshot.", 410);
        return new { cursor = s.Cursor, events = s.Events.Where(e => e.Cursor > cursor && (e.BindingId is null || e.BindingId == principal.BindingId)).ToArray() };
    });
    public RcSchedule SaveSchedule(Principal principal, RcSchedule schedule, long? expectedRevision)
    {
        Admitting(); Permit(principal, "schedules"); ValidateOperation(schedule.Operation, schedule.Payload); Permit(principal, RcProtocol.Operations[schedule.Operation]);
        if (schedule.Operation is not ("conversation.submit" or "application.start")) throw new RcFault("invalid_schedule", "This operation cannot be scheduled.", 400);
        var name = RcProtocol.Text(schedule.Name, "schedule name", 80);
        if (schedule.BindingId != principal.BindingId) throw new RcFault("forbidden", "Wrong schedule owner.", 403);
        if (schedule.GraceMinutes is < 1 or > 60 || schedule.MissedRun is not ("skip" or "catch-up-once")) throw new RcFault("invalid_schedule", "Invalid missed-run policy.", 400);
        var next = NextOccurrence(schedule.TimeZone, schedule.LocalTime, schedule.Days, clock.GetUtcNow());
        return store.Write(s =>
        {
            Admitting(); VerifyPrincipal(s, principal);
            var id = schedule.Id;
            long revision;
            if (id.Length == 0)
            {
                if (expectedRevision is not null) throw new RcFault("revision_conflict", "New schedules have no previous revision.", 412);
                if (s.Schedules.Values.Count(x => x.BindingId == principal.BindingId) >= 256) throw new RcFault("schedule_limit", "Schedule limit reached.", 429);
                id = RcProtocol.Id(); revision = 1;
            }
            else
            {
                if (!s.Schedules.TryGetValue(id, out var previous) || previous.BindingId != principal.BindingId) throw new RcFault("not_found", "Schedule not found.", 404);
                if (expectedRevision != previous.Revision) throw new RcFault("revision_conflict", "Refresh the schedule before editing.", 412);
                revision = previous.Revision + 1;
            }
            var saved = schedule with { Id = id, Name = name, Revision = revision, NextRun = next, AuthorDeviceId = principal.DeviceId, Payload = schedule.Payload.Clone() };
            s.Schedules[id] = saved;
            StateStore.Event(s, clock, "schedule.saved", principal.BindingId);
            return saved;
        });
    }
    public RcSchedule[] Schedules(Principal principal) => store.Read(s => { VerifyPrincipal(s, principal); Permit(principal, "schedules"); return s.Schedules.Values.Where(x => x.BindingId == principal.BindingId).ToArray(); });
    public void RemoveSchedule(Principal principal, string id, long revision) => store.Write(s =>
    {
        Admitting(); VerifyPrincipal(s, principal); Permit(principal, "schedules");
        if (!s.Schedules.TryGetValue(id, out var schedule) || schedule.BindingId != principal.BindingId) throw new RcFault("not_found", "Schedule not found.", 404);
        if (revision != schedule.Revision) throw new RcFault("revision_conflict", "Refresh before removing the schedule.", 412);
        s.Schedules.Remove(id); StateStore.Event(s, clock, "schedule.removed", principal.BindingId); return true;
    });
    public int Tick()
    {
        if (!Accepting) return 0;
        return store.Write(s =>
        {
            if (!Accepting) return 0;
            var now = clock.GetUtcNow(); var count = 0;
            foreach (var pending in s.Operations.Values.Where(x => x.State == "accepted" && x.Deadline <= now).ToArray())
            {
                s.Operations[pending.Id] = pending with { State = "failed", Error = "deadline_exceeded" };
                StateStore.Event(s, clock, "operation.expired", pending.BindingId, pending.Id, "deadline_exceeded");
            }
            foreach (var schedule in s.Schedules.Values.Where(x => x.Enabled && x.NextRun <= now).ToArray())
            {
                s.Schedules[schedule.Id] = schedule with { NextRun = NextOccurrence(schedule.TimeZone, schedule.LocalTime, schedule.Days, now) };
                string outcome;
                try
                {
                    var device = Access.Device(s, schedule.AuthorDeviceId);
                    if (device.BindingId != schedule.BindingId || !device.Scopes.Contains("schedules", StringComparer.Ordinal) || !device.Scopes.Contains(RcProtocol.Operations[schedule.Operation], StringComparer.Ordinal))
                        throw new RcFault("authorization_changed", "Schedule authorization changed.", 403);
                    var late = now - schedule.NextRun;
                    if (late > TimeSpan.FromSeconds(60) && (schedule.MissedRun == "skip" || late > TimeSpan.FromMinutes(schedule.GraceMinutes))) outcome = "missed";
                    else
                    {
                        Admit(s, schedule.BindingId, device.Id, $"schedule:{schedule.Id}:{schedule.NextRun.ToUnixTimeSeconds()}", schedule.Operation, schedule.Payload, 300);
                        outcome = "accepted"; count++;
                    }
                }
                catch (RcFault fault) { outcome = fault.Code; }
                StateStore.Event(s, clock, "schedule.due", schedule.BindingId, outcome: outcome);
            }
            return count;
        });
    }
    public static DateTimeOffset NextOccurrence(string timeZone, string localTime, int[] days, DateTimeOffset after)
    {
        if (days is null || days.Length is < 1 or > 7 || days.Any(d => d is < 0 or > 6) || days.Distinct().Count() != days.Length || !TimeOnly.TryParseExact(localTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new RcFault("invalid_schedule", "Invalid local time or weekdays.", 400);
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { throw new RcFault("invalid_schedule", "Unknown timezone.", 400); }
        var date = TimeZoneInfo.ConvertTime(after, zone).Date;
        for (var day = 0; day < 16; day++)
        {
            var candidate = DateTime.SpecifyKind(date.AddDays(day).Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
            if (!days.Contains((int)candidate.DayOfWeek) || zone.IsInvalidTime(candidate)) continue;
            // An ambiguous wall time runs once, at the later UTC instant.
            var offset = zone.IsAmbiguousTime(candidate) ? zone.GetAmbiguousTimeOffsets(candidate).Min() : zone.GetUtcOffset(candidate);
            var instant = new DateTimeOffset(candidate, offset).ToUniversalTime();
            if (instant > after) return instant;
        }
        throw new RcFault("invalid_schedule", "No eligible occurrence found.", 400);
    }
    public static void ValidateOperation(string operation, JsonElement payload)
    {
        if (!RcProtocol.Operations.ContainsKey(operation)) throw new RcFault("unknown_operation", "Unsupported operation.", 400);
        if (payload.ValueKind != JsonValueKind.Object || payload.GetRawText().Length > 49152) throw new RcFault("invalid_request", "Invalid operation payload.", 400);
        var allowed = operation switch { "conversation.submit" => new[] { "text", "options" }, "conversation.cancel" => ["operation_id"], "conversation.permission" => ["operation_id", "request_id", "decision"], "audio.transcribe" => ["audio_id"], _ => Array.Empty<string>() };
        if (payload.EnumerateObject().Any(p => !allowed.Contains(p.Name, StringComparer.Ordinal)) || payload.EnumerateObject().GroupBy(p => p.Name).Any(g => g.Count() > 1)) throw new RcFault("invalid_request", "Unexpected operation field.", 400);
        if (operation == "conversation.submit")
        {
            if (!payload.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String) throw new RcFault("invalid_request", "Conversation text is required.", 400);
            _ = RcProtocol.Text(text.GetString(), "conversation text", 16000);
            if (JsonSerializer.SerializeToUtf8Bytes(text.GetString(), RcProtocol.Json).Length > 24000)
                throw new RcFault("invalid_request", "Conversation text exceeds the native message limit.", 400);
            if (payload.TryGetProperty("options", out var options))
            {
                string[] modes = ["multi_turn_enabled", "exec_report_enabled", "acpx_enabled", "self_modify_enabled", "ask_execs_enabled", "step_by_step_enabled"];
                if (options.ValueKind != JsonValueKind.Object || options.EnumerateObject().Any(p => !modes.Contains(p.Name, StringComparer.Ordinal) || p.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    || options.EnumerateObject().GroupBy(p => p.Name).Any(g => g.Count() > 1))
                    throw new RcFault("invalid_request", "Execution modes must be supported boolean settings.", 400);
                if (options.TryGetProperty("multi_turn_enabled", out var multi) && !multi.GetBoolean()
                    && new[] { "exec_report_enabled", "acpx_enabled", "ask_execs_enabled", "step_by_step_enabled" }.Any(name => options.TryGetProperty(name, out var mode) && mode.GetBoolean()))
                    throw new RcFault("invalid_request", "Exec-Report, ACPX, Ask Execs and Step by step require Multiturn.", 400);
            }
        }
        else if (allowed.Length != 0)
        {
            if (!payload.TryGetProperty(allowed[0], out var id) || id.ValueKind != JsonValueKind.String) throw new RcFault("invalid_request", "An owned operation identifier is required.", 400);
            _ = RcProtocol.RequireId(id.GetString(), allowed[0]);
            if (operation == "conversation.permission")
            {
                if (!payload.TryGetProperty("request_id", out var request) || request.ValueKind != JsonValueKind.String
                    || !payload.TryGetProperty("decision", out var decision) || decision.ValueKind != JsonValueKind.String || decision.GetString() is not ("proceed" or "deny"))
                    throw new RcFault("invalid_request", "A pending request and Proceed or Deny are required.", 400);
                _ = RcProtocol.Text(request.GetString(), "permission request", 128);
            }
        }
    }
    private static bool HasConversationAuthority(DurableState s, OperationReceipt operation) =>
        operation.Kind == "conversation.submit" && operation.State == "running"
        && s.Bindings.TryGetValue(operation.BindingId, out var binding) && binding.Enabled
        && s.Devices.TryGetValue(operation.DeviceId, out var origin) && !origin.Revoked
        && origin.BindingId == operation.BindingId && origin.Scopes.Contains("chat", StringComparer.Ordinal);

    private static void VerifyPrincipal(DurableState s, Principal p)
    {
        if (p.Revoked.IsCancellationRequested) throw new RcFault("unauthorized", "Session ended.", 401);
        var device = Access.Device(s, p.DeviceId);
        if (device.BindingId != p.BindingId) throw new RcFault("forbidden", "Account mismatch.", 403);
        if (p.Scopes.Any(scope => !device.Scopes.Contains(scope, StringComparer.Ordinal)))
            throw new RcFault("unauthorized", "Device permissions changed. Reconnect to continue.", 401);
    }
    private static string Canonical(JsonElement value) => value.ValueKind == JsonValueKind.Object
        ? "{" + string.Join(",", value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => JsonSerializer.Serialize(p.Name) + ":" + Canonical(p.Value))) + "}"
        : value.ValueKind == JsonValueKind.Array ? "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]" : value.GetRawText();
}
