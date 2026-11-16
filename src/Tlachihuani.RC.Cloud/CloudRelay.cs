// Tlachihuani — Angela López Mendoza · XAIHT
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tlachihuani.RC.Cloud;

public sealed class CloudRelay(CloudConfiguration configuration, CloudSessions sessions, CloudGrantStore grants) : IDisposable
{
    public const string Subprotocol = "tlachihuani.relay.v1";
    public const int MaxFrameBytes = 128 * 1024;
    private readonly object gate = new();
    private readonly Dictionary<string, Connector> connectors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Route> routes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> lastSeen = new(StringComparer.Ordinal);
    private bool disposed;

    public object Computers(string ownerId)
    {
        lock (gate)
            return new { computers = configuration.Computers
                .Where(computer => computer.Accounts.Any(account => account.OwnerId == ownerId && grants.Allows(computer.InstallationId, account.BindingId, ownerId)))
                .Select(computer => new
                {
                    computer.InstallationId, computer.Name,
                    online = connectors.TryGetValue(computer.InstallationId, out var connector) && Fresh(connector),
                    last_seen = lastSeen.GetValueOrDefault(computer.InstallationId) is var seen && seen != default ? (DateTimeOffset?)seen : null,
                    accounts = computer.Accounts.Where(account => account.OwnerId == ownerId && grants.Allows(computer.InstallationId, account.BindingId, ownerId))
                        .Select(account => new { account.BindingId, account.Edition, account.DisplayName }).ToArray()
                }).ToArray() };
    }

    public object Invite(CloudSession session, ConnectionRequest request)
    {
        if (session.User is null) throw new CloudFault("authentication_required", 401);
        if (request.Profile != CloudConfiguration.DevelopmentProfile) throw new CloudFault("security_profile_unavailable", 409);
        var computer = configuration.Computers.FirstOrDefault(item => item.InstallationId == request.InstallationId);
        var account = computer?.Accounts.FirstOrDefault(item => item.BindingId == request.BindingId && item.OwnerId == session.User.Id);
        if (computer is null || account is null || !grants.Allows(computer.InstallationId, account.BindingId, session.User.Id)) throw new CloudFault("computer_unavailable", 404);
        ValidateDeviceKey(request.DevicePublicKey ?? "");
        lock (gate)
        {
            if (disposed) throw new CloudFault("cloud_stopping", 503);
            SweepLocked();
            if (!connectors.TryGetValue(computer.InstallationId, out var connector) || !Fresh(connector))
                throw new CloudFault("computer_offline", 409);
            if (routes.Count >= 16 || routes.Values.Count(route => route.OwnerId == session.User.Id) >= 4)
                throw new CloudFault("connection_limit", 429);
            var route = new Route(CloudSessions.Secret()[..32], grants.Revision, session.Token, session.User.Id,
                computer.InstallationId, account.BindingId, request.DevicePublicKey ?? "");
            routes.Add(route.Id, route);
            return new { ticket = route.ClientTicket, expires_at = route.ExpiresAt };
        }
    }

    public async Task ConnectorControl(HttpContext context)
    {
        var installationId = context.Request.Headers["X-Installation-Id"].ToString();
        var computer = configuration.Computers.FirstOrDefault(item => item.InstallationId == installationId);
        if (computer is null || !CloudSessions.SameSecret(Bearer(context), computer.ConnectorToken))
            throw new CloudFault("connector_unauthorized", 401);
        RequireWebSocket(context);
        Connector connector;
        lock (gate)
        {
            if (disposed) throw new CloudFault("cloud_stopping", 503);
            if (connectors.ContainsKey(installationId)) throw new CloudFault("connector_already_connected", 409);
            connector = new Connector(installationId);
            connectors.Add(installationId, connector);
        }
        try
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync(Subprotocol);
            lock (gate)
            {
                // Shutdown can remove the reserved connector while the HTTP
                // upgrade is in flight. Never attach a socket to that stale entry.
                if (disposed || !connectors.TryGetValue(installationId, out var current)
                    || !ReferenceEquals(current, connector))
                { socket.Abort(); return; }
                connector.Socket = socket;
                connector.Seen = DateTimeOffset.UtcNow;
                lastSeen[installationId] = connector.Seen;
            }
            while (!context.RequestAborted.IsCancellationRequested)
            {
                var message = await Receive(socket, 4096, context.RequestAborted);
                if (message is null) break;
                if (message.Value.Type != WebSocketMessageType.Text) throw new CloudFault("invalid_control_frame");
                var heartbeat = JsonSerializer.Deserialize<Heartbeat>(message.Value.Bytes, CloudConfiguration.Json);
                if (heartbeat?.Type is not ("heartbeat" or "ping")) throw new CloudFault("invalid_control_frame");
                lock (gate) { connector.Seen = DateTimeOffset.UtcNow; lastSeen[installationId] = connector.Seen; }
                await connector.Send(new { type = "pong" }, context.RequestAborted);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or CloudFault or JsonException) { }
        finally
        {
            lock (gate)
            {
                if (connectors.TryGetValue(installationId, out var current) && ReferenceEquals(current, connector))
                {
                    connectors.Remove(installationId);
                    foreach (var route in routes.Values.Where(route => route.InstallationId == installationId).ToArray()) EndLocked(route);
                }
            }
            connector.Dispose();
        }
    }

    public async Task Client(HttpContext context, CloudSession session)
    {
        RequireWebSocket(context);
        using var socket = await context.WebSockets.AcceptWebSocketAsync(Subprotocol);
        Route? route = null;
        try
        {
            using var first = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            first.CancelAfter(TimeSpan.FromSeconds(3));
            var frame = await Receive(socket, 2048, first.Token);
            if (frame is null || frame.Value.Type != WebSocketMessageType.Text) throw new CloudFault("ticket_required");
            var hello = JsonSerializer.Deserialize<TicketRequest>(frame.Value.Bytes, CloudConfiguration.Json);
            Connector connector;
            lock (gate)
            {
                var candidate = routes.Values.FirstOrDefault(item => item.ClientTicket == hello?.Ticket);
                if (candidate is null || candidate.ClientUsed || candidate.ExpiresAt <= DateTimeOffset.UtcNow
                    || candidate.SessionToken != session.Token || session.User is null || candidate.OwnerId != session.User.Id
                    || sessions.Find(session.Token) is null || !grants.Allows(candidate.InstallationId, candidate.BindingId, candidate.OwnerId))
                    throw new CloudFault("invalid_ticket", 403);
                route = candidate;
                if (!connectors.TryGetValue(route.InstallationId, out connector!) || !Fresh(connector))
                    throw new CloudFault("computer_offline", 409);
                route.ClientUsed = true;
                route.ClientSocket = socket;
                route.PairDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
            }
            using var pairing = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, route.Stop.Token);
            pairing.CancelAfter(TimeSpan.FromSeconds(10));
            await connector.Send(new
            {
                type = "open", channel_id = route.Id, ticket = route.ConnectorTicket,
                binding_id = route.BindingId, device_public_key = route.DevicePublicKey
            }, pairing.Token);
            var peer = await route.ConnectorReady.Task.WaitAsync(pairing.Token);
            if (sessions.Find(session.Token) is null || !grants.Allows(route.InstallationId, route.BindingId, route.OwnerId)) throw new CloudFault("authentication_required", 401);
            await Send(socket, new { type = "ready", binding_id = route.BindingId, profile = CloudConfiguration.DevelopmentProfile }, pairing.Token);
            using var life = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, route.Stop.Token);
            var outbound = Pump(socket, peer, life.Token);
            var inbound = Pump(peer, socket, life.Token);
            await Task.WhenAny(outbound, inbound);
            await life.CancelAsync();
            socket.Abort(); peer.Abort();
            try { await Task.WhenAll(outbound, inbound); }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or CloudFault) { }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or CloudFault or JsonException)
        {
            if (socket.State == WebSocketState.Open)
            {
                using var close = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, ex is CloudFault fault ? fault.Code : "connection_closed", close.Token); }
                catch (Exception closeError) when (closeError is WebSocketException or OperationCanceledException) { }
            }
        }
        finally
        {
            if (route is not null) lock (gate) EndLocked(route);
            socket.Abort();
        }
    }

    public async Task ConnectorSession(HttpContext context)
    {
        RequireWebSocket(context);
        var installationId = context.Request.Headers["X-Installation-Id"].ToString();
        var ticket = Bearer(context);
        Route route;
        lock (gate)
        {
            route = routes.Values.FirstOrDefault(item => item.ConnectorTicket == ticket)
                ?? throw new CloudFault("invalid_ticket", 403);
            if (route.InstallationId != installationId || route.ConnectorUsed || !route.ClientUsed
                || route.PairDeadline <= DateTimeOffset.UtcNow || sessions.Find(route.SessionToken) is null
                || !grants.Allows(route.InstallationId, route.BindingId, route.OwnerId))
                throw new CloudFault("invalid_ticket", 403);
            route.ConnectorUsed = true;
        }
        try
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync(Subprotocol);
            lock (gate)
            {
                if (!routes.ContainsKey(route.Id) || route.Stop.IsCancellationRequested) throw new CloudFault("invalid_ticket", 403);
                route.ConnectorSocket = socket;
                route.ConnectorReady.TrySetResult(socket);
            }
            await route.Finished.Task.WaitAsync(context.RequestAborted);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or CloudFault) { }
        finally { lock (gate) EndLocked(route); }
    }

    public void CloseSession(string token)
    {
        lock (gate)
            foreach (var route in routes.Values.Where(route => route.SessionToken == token).ToArray()) EndLocked(route);
    }
    public long RevokeLink(string ownerId, string installationId, string bindingId)
    {
        var computer = configuration.Computers.FirstOrDefault(item => item.InstallationId == installationId);
        if (computer is null || !computer.Accounts.Any(item => item.BindingId == bindingId && item.OwnerId == ownerId))
            throw new CloudFault("computer_unavailable", 404);
        lock (gate)
        {
            var revision = grants.Revoke(installationId, bindingId, ownerId);
            foreach (var route in routes.Values.Where(route => route.OwnerId == ownerId
                && route.InstallationId == installationId && route.BindingId == bindingId).ToArray()) EndLocked(route);
            return revision;
        }
    }
    public void Sweep() { lock (gate) SweepLocked(); }
    private void SweepLocked()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var connector in connectors.Values.Where(connector => connector.Seen <= now.AddSeconds(-60)).ToArray())
        {
            connectors.Remove(connector.InstallationId);
            connector.Socket?.Abort();
        }
        foreach (var route in routes.Values.Where(route => sessions.Find(route.SessionToken) is null
            || !grants.Allows(route.InstallationId, route.BindingId, route.OwnerId)
            || (!route.ClientUsed && route.ExpiresAt <= now)
            || (route.ClientUsed && !route.ConnectorReady.Task.IsCompleted && route.PairDeadline <= now)
            || !connectors.TryGetValue(route.InstallationId, out var connector) || !Fresh(connector)).ToArray())
            EndLocked(route);
    }
    private void EndLocked(Route route)
    {
        if (!routes.Remove(route.Id)) return;
        route.Stop.Cancel();
        route.ClientSocket?.Abort();
        route.ConnectorSocket?.Abort();
        route.ConnectorReady.TrySetCanceled();
        route.Finished.TrySetResult();
    }
    private static bool Fresh(Connector connector) => connector.Socket?.State == WebSocketState.Open
        && connector.Seen > DateTimeOffset.UtcNow.AddSeconds(-60);
    private static async Task Pump(WebSocket source, WebSocket destination, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            var message = await Receive(source, MaxFrameBytes, cancellation);
            if (message is null) return;
            await destination.SendAsync(message.Value.Bytes.AsMemory(), message.Value.Type, true, cancellation);
        }
    }
    private static async Task<(byte[] Bytes, WebSocketMessageType Type)?> Receive(WebSocket socket, int max, CancellationToken cancellation)
    {
        var bytes = new byte[max + 1];
        var count = 0;
        var fragments = 0;
        WebSocketMessageType? type = null;
        while (true)
        {
            var received = await socket.ReceiveAsync(bytes.AsMemory(count), cancellation);
            if (++fragments > 1024) throw new CloudFault("too_many_fragments", 413);
            if (received.MessageType == WebSocketMessageType.Close) return null;
            if (type is not null && type != received.MessageType) throw new CloudFault("invalid_frame");
            type = received.MessageType;
            count += received.Count;
            if (count > max) throw new CloudFault("frame_too_large", 413);
            if (received.EndOfMessage) return (bytes.AsSpan(0, count).ToArray(), received.MessageType);
        }
    }
    private static Task Send(WebSocket socket, object value, CancellationToken cancellation) => socket.SendAsync(
        JsonSerializer.SerializeToUtf8Bytes(value, CloudConfiguration.Json).AsMemory(), WebSocketMessageType.Text, true, cancellation).AsTask();
    private static void RequireWebSocket(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest || !context.WebSockets.WebSocketRequestedProtocols.Contains(Subprotocol, StringComparer.Ordinal))
            throw new CloudFault("websocket_protocol_required", 400);
    }
    private static string Bearer(HttpContext context)
    {
        var value = context.Request.Headers.Authorization.ToString();
        return value.StartsWith("Bearer ", StringComparison.Ordinal) ? value[7..] : "";
    }
    private static void ValidateDeviceKey(string value)
    {
        try
        {
            if (value.Length is < 64 or > 1024) throw new CryptographicException();
            using var key = ECDsa.Create();
            var bytes = Convert.FromBase64String(value);
            key.ImportSubjectPublicKeyInfo(bytes, out var read);
            if (read != bytes.Length || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
                throw new CryptographicException();
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        { throw new CloudFault("invalid_device_key"); }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            foreach (var route in routes.Values.ToArray()) EndLocked(route);
            foreach (var connector in connectors.Values) connector.Socket?.Abort();
            connectors.Clear();
        }
    }
    private sealed class Connector(string installationId) : IDisposable
    {
        public string InstallationId { get; } = installationId;
        public WebSocket? Socket { get; set; }
        public DateTimeOffset Seen { get; set; } = DateTimeOffset.UtcNow;
        private readonly SemaphoreSlim sendGate = new(1, 1);
        public async Task Send(object value, CancellationToken cancellation)
        {
            await sendGate.WaitAsync(cancellation);
            try
            {
                if (Socket?.State != WebSocketState.Open) throw new CloudFault("computer_offline", 409);
                await CloudRelay.Send(Socket, value, cancellation);
            }
            finally { sendGate.Release(); }
        }
        // Concurrent route-send continuations can still release the semaphore after close.
        public void Dispose() => Socket?.Abort();
    }
    private sealed class Route(string id, long grantRevision, string sessionToken, string ownerId, string installationId, string bindingId, string devicePublicKey)
    {
        public string Id { get; } = id;
        public long GrantRevision { get; } = grantRevision;
        public string SessionToken { get; } = sessionToken;
        public string OwnerId { get; } = ownerId;
        public string InstallationId { get; } = installationId;
        public string BindingId { get; } = bindingId;
        public string DevicePublicKey { get; } = devicePublicKey;
        public string ClientTicket { get; } = CloudSessions.Secret();
        public string ConnectorTicket { get; } = CloudSessions.Secret();
        public DateTimeOffset ExpiresAt { get; } = DateTimeOffset.UtcNow.AddSeconds(60);
        public DateTimeOffset PairDeadline { get; set; }
        public bool ClientUsed { get; set; }
        public bool ConnectorUsed { get; set; }
        public WebSocket? ClientSocket { get; set; }
        public WebSocket? ConnectorSocket { get; set; }
        public CancellationTokenSource Stop { get; } = new();
        public TaskCompletionSource<WebSocket> ConnectorReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed record TicketRequest(string Ticket);
    private sealed record Heartbeat(string Type);
}

public sealed record ConnectionRequest(string InstallationId, string BindingId, string DevicePublicKey, string Profile);
