// Tlachihuani — Angela López Mendoza · XAIHT
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using Tlachihuani.Windows;

namespace Tlachihuani.RC.Server;

internal sealed class RcRuntime : IDisposable
{
    internal readonly RcHostSettings Settings;
    internal readonly StateStore Store;
    internal readonly Access Access;
    internal readonly Operations Operations;
    internal readonly TimeProvider Clock = TimeProvider.System;
    internal readonly string InstanceId = RcProtocol.Id();
    internal readonly DateTimeOffset Started = DateTimeOffset.UtcNow;
    internal volatile bool ListenerReady;
    internal string? StartupError;
    private readonly ConcurrentDictionary<string, (DateTimeOffset Seen, bool Ready, ExecutionCapabilities? Capabilities)> adapters = new(StringComparer.Ordinal);
    private readonly object nativeGate = new();
    private DateTimeOffset? brokerSeen;
    private readonly IHostApplicationLifetime lifetime;
    public RcRuntime(RcHostSettings settings, IHostApplicationLifetime lifetime)
    {
        Settings = settings;
        this.lifetime = lifetime;
        Store = new(Path.Combine(settings.Root, "state"));
        Access = new(Store, settings.Configuration, Clock);
        Operations = new(Store, Clock);
        Operations.Recover();
    }
    internal object Status() => new
    {
        protocol_major = 1, service = RcProtocol.ServiceName, display_name = RcProtocol.DisplayName,
        instance_id = InstanceId, started_at = Started, observed_at = Clock.GetUtcNow(),
        health = !Operations.Accepting ? "stopping" : ListenerReady ? "ready" : "setup_required",
        listener = ListenerReady, origin = Settings.Configuration.PublicOrigin,
        security_profile = Settings.Configuration.SecurityProfile, post_quantum = false,
        sessions = Access.SessionCount, include_in_start_services = Settings.Configuration.IncludeInStartServices,
        schedules = Store.Read(s => s.Schedules.Values.Count(x => x.Enabled)),
        pending_operations = Store.Read(s => s.Operations.Values.Count(x => x.State is "accepted" or "running")),
        error = StartupError
    };
    internal JsonObject Native(JsonObject req, string sid)
    {
        lock (nativeGate) return NativeCore(req, sid);
    }
    private JsonObject NativeCore(JsonObject req, string sid)
    {
        if (sid != Settings.OperatorSid) throw new RcFault("forbidden", "Only the delegated local Windows operator may control RC.", 403);
        var op = (string?)req["op"];
        string Text(string field) => (string?)req[field] ?? throw new RcFault("invalid_request", "Missing " + field, 400);
        object result = op switch
        {
            "status" => Status(),
            "development-stop" when !Settings.Installed => StopDevelopment(),
            "accounts" => Accounts(sid),
            "devices" => Store.Read(s => new { devices = s.Devices.Values.Where(d => s.Bindings[d.BindingId].WindowsSid == sid).Select(d => new { d.Id, d.BindingId, d.Name, d.Scopes, d.EnrolledAt, d.Revoked }).ToArray() }),
            "pending-enrollments" => Store.Read(s => new { enrollments = s.Enrollments.Values.Where(e => e.Status == "pending" && e.ExpiresAt > Clock.GetUtcNow() && s.Bindings[s.Invitations[e.InvitationId].BindingId].WindowsSid == sid).Select(e => new { e.Id, e.Name, e.Fingerprint, e.ExpiresAt, binding_id = s.Invitations[e.InvitationId].BindingId, scopes = s.Invitations[e.InvitationId].Scopes }).ToArray() }),
            "invite" => Access.CreateInvitation(Text("binding_id"), req["scopes"]?.Deserialize<string[]>(RcProtocol.Json) ?? [], sid),
            "approve" => Access.Approve(Text("enrollment_id"), Text("fingerprint"), sid),
            "decline" => Decline(Text("enrollment_id"), sid),
            "revoke" => Revoke(Text("device_id"), sid),
            "configuration" => Settings.Configuration,
            "configure" => Configure(req["configuration"]?.Deserialize<RcConfiguration>(RcProtocol.Json) ?? throw new RcFault("invalid_request", "Configuration is required.", 400)),
            // Account registration is available only through the trusted local operator channel.
            // The immutable account identity must come from the installed account adapter.
            "bind-account" => Access.RegisterBinding(Text("edition"), Text("account_id"), Text("display_name"), sid, Text("adapter_id")),
            "unbind-account" => Unbind(Text("binding_id"), Text("adapter_id"), sid),
            "adapter-heartbeat" => Heartbeat(Text("adapter_id"), (bool?)req["ready"] ?? false, (string?)req["operation_id"], sid,
                req["capabilities"]?.Deserialize<ExecutionCapabilities>(RcProtocol.Json)),
            "adapter-status" => Application(Text("binding_id")),
            "broker-application-status" => BrokerApplication(Text("binding_id")),
            "adapter-next" => ApplicationNext(Text("adapter_id"), sid, (bool?)req["controls_only"] ?? false),
            "adapter-progress" => Progress(req, sid),
            "adapter-complete" => Complete(req, sid),
            "broker-next" => BrokerNext(sid),
            "broker-complete" => Complete(req, sid, true),
            _ => throw new RcFault("unknown_operation", "Unsupported local RC operation.", 400)
        };
        return JsonSerializer.SerializeToNode(result, RcProtocol.Json)!.AsObject();
    }
    private object Accounts(string sid)
    {
        var bindings = Store.Read(s => s.Bindings.Values.Where(b => b.WindowsSid == sid && b.Enabled).ToArray());
        // Native operator context only. Retain every immutable binding field and
        // distinguish a live adapter from a previously enabled, offline account.
        return new { accounts = bindings.Select(binding =>
        {
            var account = JsonSerializer.SerializeToNode(binding, RcProtocol.Json)!.AsObject();
            var connected = adapters.TryGetValue(binding.AdapterId, out var observation)
                && Clock.GetUtcNow() - observation.Seen < TimeSpan.FromSeconds(15);
            account["application_state"] = connected ? observation.Ready ? "ready" : "loading" : "offline";
            return account;
        }).ToArray() };
    }
    internal object Application(string bindingId)
    {
        lock (nativeGate)
        {
        var binding = Store.Read(s => Access.Binding(s, bindingId));
        var connected = adapters.TryGetValue(binding.AdapterId, out var observation) && Clock.GetUtcNow() - observation.Seen < TimeSpan.FromSeconds(15);
        var ready = connected && observation.Ready;
        return new { state = ready ? "ready" : connected ? "loading" : "offline", connected, ready,
            observed_at = connected ? (DateTimeOffset?)observation.Seen : null, reason = connected ? null : "adapter_disconnected",
            capabilities = connected ? observation.Capabilities : null };
        }
    }
    internal object ClientCapabilities(string bindingId, bool chat, bool workspace)
    {
        lock (nativeGate)
        {
            var binding = Store.Read(s => Access.Binding(s, bindingId));
            var current = adapters.TryGetValue(binding.AdapterId, out var observation)
                && Clock.GetUtcNow() - observation.Seen < TimeSpan.FromSeconds(15);
            var capabilities = current ? observation.Capabilities : null;
            // Feature availability for this account only. Monitoring observations,
            // model names, service state and local ports remain outside this response.
            var webPort = capabilities?.WebPort ?? 0;
            return new
            {
                execution_options = chat && capabilities?.ExecutionOptions == true,
                self_modify_available = chat && capabilities?.SelfModifyAvailable == true,
                self_modify_active = chat && capabilities?.SelfModifyActive == true,
                self_modify_locked = chat && capabilities?.SelfModifyLocked == true,
                workspace_available = workspace && webPort is >= 1024 and <= 65535
                    && webPort != Settings.Configuration.Port && webPort != 12064
            };
        }
    }
    private object BrokerApplication(string bindingId) { brokerSeen = Clock.GetUtcNow(); return Application(bindingId); }
    internal Uri WorkspaceEndpoint(string bindingId)
    {
        var binding = Store.Read(s => s.Bindings[bindingId]);
        if (!binding.Enabled || !adapters.TryGetValue(binding.AdapterId, out var state)
            || Clock.GetUtcNow() - state.Seen >= TimeSpan.FromSeconds(15)
            || state.Capabilities?.WebPort is not int port || port is < 1024 or > 65535
            || port == Settings.Configuration.Port || port == 12064)
            throw new RcFault("application_offline", "Start the updated Tlamatini edition before opening its workspace.");
        return new Uri($"http://127.0.0.1:{port}/");
    }
    internal void ObserveDisconnectedAdapters()
    {
        lock (nativeGate)
        {
            foreach (var item in adapters.Where(p => Clock.GetUtcNow() - p.Value.Seen >= TimeSpan.FromSeconds(20)).ToArray())
            {
                Operations.AdapterDisconnected(item.Key);
                adapters.TryRemove(item.Key, out _);
            }
            if (brokerSeen is DateTimeOffset seen && Clock.GetUtcNow() - seen >= TimeSpan.FromSeconds(45))
            { Operations.AdapterDisconnected("", true); brokerSeen = null; }
        }
    }
    private object Heartbeat(string adapterId, bool ready, string? operationId, string sid, ExecutionCapabilities? capabilities)
    {
        var found = Store.Read(s => s.Bindings.Values.Any(b => b.AdapterId == adapterId && b.WindowsSid == sid && b.Enabled));
        if (!found) throw new RcFault("forbidden", "The account adapter is not enabled.", 403);
        if (capabilities is not null && (capabilities.Model is null || capabilities.Model.Length > 256))
            throw new RcFault("invalid_request", "Invalid model label.", 400);
        adapters[adapterId] = (Clock.GetUtcNow(), ready, capabilities);
        var authorized = operationId is null || Store.Read(s => s.Operations.TryGetValue(operationId, out var op)
            && op.State == "running" && s.Bindings[op.BindingId].AdapterId == adapterId
            && s.Devices.TryGetValue(op.DeviceId, out var device) && !device.Revoked && device.Scopes.Contains("chat", StringComparer.Ordinal));
        return new { accepted = true, active_authorized = authorized };
    }
    private object Unbind(string bindingId, string adapterId, string sid)
    {
        Access.DisableBinding(bindingId, adapterId, sid);
        adapters.TryRemove(adapterId, out _);
        return new { disabled = true };
    }
    private object BrokerNext(string sid)
    {
        RequireListener();
        brokerSeen = Clock.GetUtcNow();
        var operation = Operations.TakeNext("", sid, "broker");
        var binding = operation is null ? null : Store.Read(s => s.Bindings[operation.BindingId]);
        return new { operation, binding };
    }
    private void RequireListener()
    {
        if (!ListenerReady) throw new RcFault("service_not_ready", "Remote-Controller is not ready to dispatch work.", 503);
    }
    private object ApplicationNext(string adapterId, string sid, bool controlsOnly)
    {
        RequireListener();
        return new { operation = Operations.TakeNext(adapterId, sid, "application", controlsOnly) };
    }
    private object Progress(JsonObject req, string sid)
    {
        Operations.Progress((string?)req["operation_id"] ?? "", (string?)req["adapter_id"] ?? "", sid,
            req["progress"]?.Deserialize<JsonElement>(RcProtocol.Json) ?? throw new RcFault("invalid_request", "Progress is required.", 400));
        return new { accepted = true };
    }
    private object StopDevelopment()
    {
        Operations.BeginStop();
        _ = Task.Run(async () => { await Task.Delay(250); lifetime.StopApplication(); });
        return new { stopping = true };
    }
    private object Decline(string id, string sid) { Access.Decline(id, sid); return new { declined = true }; }
    private object Revoke(string id, string sid) { Access.Revoke(id, sid); return new { revoked = true }; }
    private object Complete(JsonObject req, string sid, bool broker = false)
    {
        if (broker) Operations.Complete((string?)req["operation_id"] ?? "", "", sid, req["result"]?.Deserialize<JsonElement>(RcProtocol.Json), (string?)req["error"], true);
        else Operations.CompleteApplication((string?)req["operation_id"] ?? "", (string?)req["adapter_id"] ?? "", sid, req["result"]?.Deserialize<JsonElement>(RcProtocol.Json), (string?)req["error"]);
        return new { accepted = true };
    }
    private object Configure(RcConfiguration configuration)
    {
        configuration.Validate();
        var directory = Path.Combine(Settings.Root, "settings");
        StateStore.RejectProfileOrReparse(directory);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "configuration.json");
        var staging = file + "." + RcProtocol.Id();
        try
        {
            StateStore.RejectProfileOrReparse(file);
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { JsonSerializer.Serialize(stream, configuration, RcProtocol.Json); stream.Flush(true); }
            File.Move(staging, file, true);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
        return new { saved = true, restart_required = true };
    }
    public void Dispose() { Access.Dispose(); Store.Dispose(); }
}
