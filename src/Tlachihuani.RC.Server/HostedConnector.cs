// Tlachihuani — Angela López Mendoza · XAIHT
// Isolated hosted-transport laboratory. The production PQ gate remains closed.
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tlachihuani.RC.Server;

internal sealed record HostedConnectorConfiguration
{
    public int Schema { get; init; } = 1;
    public bool Enabled { get; init; }
    public string Profile { get; init; } = "development-loopback";
    public string CloudOrigin { get; init; } = "";
    public string CertificateSha256 { get; init; } = "";
    public string ConnectorToken { get; init; } = "";
    public string[] AllowedBindings { get; init; } = [];
    internal void Validate(RcRuntime runtime, bool running = false)
    {
        if (runtime.Settings.Installed)
            throw new RcFault("security_profile_unavailable", "Hosted development transport cannot run in an installed service.", 403);
        if (Schema != 1 || Profile != "development-loopback")
            throw new RcFault("security_profile_unavailable", "The reviewed production remote security profile is not installed.", 403);
        if (!Enabled) return;
        if (!Uri.TryCreate(CloudOrigin, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Host != "127.0.0.1" || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/"
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.Port is < 1024 or > 65535
            || uri.Port == 12064 || uri.Port == runtime.Settings.Configuration.Port)
            throw new RcFault("invalid_hosted_configuration", "The development relay must use an explicit HTTPS IPv4 loopback origin.", 400);
        if (!Regex.IsMatch(CertificateSha256, "^[a-fA-F0-9]{64}$") || !Regex.IsMatch(ConnectorToken, "^[a-fA-F0-9]{64}$")
            || AllowedBindings is null || AllowedBindings.Length is < 1 or > 16 || AllowedBindings.Distinct(StringComparer.Ordinal).Count() != AllowedBindings.Length
            || AllowedBindings.Any(x => !RcProtocol.IsId(x)))
            throw new RcFault("invalid_hosted_configuration", "Invalid pinned certificate, connector credential or account selection.", 400);
        runtime.Store.Read(s =>
        {
            foreach (var id in AllowedBindings)
            {
                // An account turned off in Tlamatini is refused request by request, so
                // the other accounts keep their hosted connection. An unknown account is
                // a configuration mistake, unless it was removed after configuration.
                if (!s.Bindings.TryGetValue(id, out var binding))
                {
                    if (running) continue;
                    throw new RcFault("account_unavailable", "Account binding is unavailable.", 403);
                }
                if (binding.WindowsSid != runtime.Settings.OperatorSid)
                    throw new RcFault("forbidden", "The selected account belongs to another Windows operator.", 403);
            }
            return true;
        });
    }
}

internal sealed class HostedConnector(RcRuntime runtime) : IDisposable
{
    private const int FrameLimit = 131072;
    private readonly object gate = new();
    private CancellationTokenSource? active;
    private long configurationGeneration;
    private volatile bool connected;
    private volatile string state = "disabled";
    private readonly ConcurrentDictionary<string, Task> channels = new(StringComparer.Ordinal);
    private string ConfigurationPath => Path.Combine(runtime.Settings.Root, "settings", "hosted-connector.json");
    internal object Status() => new { profile = "development-loopback", connected, state,
        public_remote_enabled = false, post_quantum = false, active_channels = channels.Count };
    internal object Configure(HostedConnectorConfiguration configuration)
    {
        configuration.Validate(runtime);
        lock (gate)
        {
            // Persist, supersede any in-flight read, and cancel its connection as
            // one transition. A concurrent enable cannot overwrite a later disable.
            var path = ConfigurationPath;
            StateStore.RejectProfileOrReparse(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var pending = path + "." + RcProtocol.Id() + ".tmp";
            try
            {
                File.WriteAllBytes(pending, JsonSerializer.SerializeToUtf8Bytes(configuration, RcProtocol.Json));
                File.Move(pending, path, true);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
            configurationGeneration++;
            active?.Cancel();
            connected = false;
            state = configuration.Enabled ? "connecting" : "disabled";
        }
        return new { saved = true, profile = configuration.Profile, enabled = configuration.Enabled, public_remote_enabled = false };
    }
    internal object Disconnect() => Configure(new HostedConnectorConfiguration());
    internal async Task Run(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            // About once a second while not connected: written only when the check's outcome changes.
            // The pass ends before a connection starts, so a live session is written as it happens.
            var pass = Tlachihuani.Contracts.TraceLog.Pass("RC hosted connector check");
            try {
            long attemptGeneration;
            lock (gate) attemptGeneration = configurationGeneration;
            void ReportState(string value)
            { lock (gate) if (attemptGeneration == configurationGeneration) state = value; }
            try
            {
                if (!runtime.ListenerReady || !File.Exists(ConfigurationPath))
                { ReportState("disabled"); await Task.Delay(1000, stopping); continue; }
                StateStore.RejectProfileOrReparse(ConfigurationPath);
                var bytes = File.ReadAllBytes(ConfigurationPath);
                if (bytes.Length > 16384) throw new RcFault("invalid_hosted_configuration", "Hosted configuration is too large.");
                var configuration = JsonSerializer.Deserialize<HostedConnectorConfiguration>(bytes, RcProtocol.Json)
                    ?? throw new RcFault("invalid_hosted_configuration", "Missing configuration.");
                configuration.Validate(runtime, running: true);
                if (!configuration.Enabled) { ReportState("disabled"); await Task.Delay(1000, stopping); continue; }
                using var connection = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                lock (gate)
                {
                    // Configure may have replaced the file while it was being read.
                    // Never publish a connection from that stale configuration.
                    if (attemptGeneration != configurationGeneration) continue;
                    active = connection;
                }
                pass.Dispose();
                try { await Control(configuration, connection.Token); }
                finally
                {
                    connected = false;
                    connection.Cancel();
                    var pending = channels.Values.ToArray();
                    try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { }
                    lock (gate) if (ReferenceEquals(active, connection)) active = null;
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { connected = false; ReportState(ex is RcFault f ? f.Code : "relay_unavailable"); }
            try { await Task.Delay(Random.Shared.Next(800, 1800), stopping); }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { break; }
            } finally { pass.Dispose(); }
        }
        connected = false; state = "stopped";
    }
    private ClientWebSocket Socket(HostedConnectorConfiguration configuration)
    {
        var socket = new ClientWebSocket();
        socket.Options.Proxy = null;
        socket.Options.AddSubProtocol("tlachihuani.relay.v1");
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        // The certificate's own validity dates, never text parsed in the PC's
        // regional date format.
        socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
            certificate is X509Certificate2 pinned
            && CryptographicOperations.FixedTimeEquals(SHA256.HashData(pinned.GetRawCertData()),
                Convert.FromHexString(configuration.CertificateSha256))
            && DateTime.UtcNow >= pinned.NotBefore.ToUniversalTime()
            && DateTime.UtcNow < pinned.NotAfter.ToUniversalTime();
        return socket;
    }
    private static Uri Endpoint(HostedConnectorConfiguration configuration, string path)
        => new UriBuilder(new Uri(configuration.CloudOrigin)) { Scheme = "wss", Path = path }.Uri;
    private async Task Control(HostedConnectorConfiguration configuration, CancellationToken stop)
    {
        using var socket = Socket(configuration);
        // The relay stores lower-case hex; a token entered in upper case still matches.
        socket.Options.SetRequestHeader("Authorization", "Bearer " + configuration.ConnectorToken.ToLowerInvariant());
        var installationId = runtime.Store.Read(s => s.InstallationId);
        socket.Options.SetRequestHeader("X-Installation-Id", installationId);
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop))
        { timeout.CancelAfter(TimeSpan.FromSeconds(8)); await socket.ConnectAsync(Endpoint(configuration, "/relay/connector"), timeout.Token); }
        lock (gate)
        {
            stop.ThrowIfCancellationRequested();
            connected = true; state = "connected";
        }
        using var life = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var heartbeat = Heartbeat(socket, life.Token);
        try
        {
            while (socket.State == WebSocketState.Open && !stop.IsCancellationRequested)
            {
                var frame = await Read(socket, stop);
                if (frame is null) break;
                using var document = JsonDocument.Parse(frame, new JsonDocumentOptions { MaxDepth = 12 });
                var value = document.RootElement;
                var type = value.GetProperty("type").GetString();
                if (type is "pong" or "registered" or "heartbeat") continue;
                if (type != "open") throw new RcFault("invalid_relay_frame", "Unsupported connector control frame.");
                var channelId = Field(value, "channel_id");
                var ticket = Field(value, "ticket");
                var bindingId = Field(value, "binding_id");
                var publicKey = Field(value, "device_public_key");
                // One unusable request refuses only its own channel; it never closes the
                // control link or the other accounts' channels. A turned-off account is
                // refused per request, so the phone can say why.
                if (!RcProtocol.IsId(channelId) || ticket.Length is < 32 or > 256 || channels.Count >= 16
                    || !configuration.AllowedBindings.Contains(bindingId, StringComparer.Ordinal)
                    || !UsablePublicKey(publicKey)) continue;
                if (!channels.TryAdd(channelId, Task.CompletedTask)) continue;
                var task = Session(configuration, installationId, ticket, bindingId, publicKey, life.Token);
                channels[channelId] = task;
                _ = task.ContinueWith(completed => { channels.TryRemove(channelId, out _); _ = completed.Exception; },
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        finally { life.Cancel(); socket.Abort(); try { await heartbeat; } catch (Exception ex) when (ex is not OutOfMemoryException) { } }
    }
    private static async Task Heartbeat(WebSocket socket, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            using var pass = Tlachihuani.Contracts.TraceLog.Pass("RC hosted heartbeat");
            await Task.Delay(TimeSpan.FromSeconds(15), stop);
            await Send(socket, new { type = "heartbeat" }, stop);
        }
    }
    private async Task Session(HostedConnectorConfiguration configuration, string installation, string ticket,
        string bindingId, string publicKey, CancellationToken stop)
    {
        using var socket = Socket(configuration);
        socket.Options.SetRequestHeader("Authorization", "Bearer " + ticket);
        socket.Options.SetRequestHeader("X-Installation-Id", installation);
        using var life = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var requests = new ConcurrentDictionary<string, (CancellationTokenSource Stop, Task Task)>(StringComparer.Ordinal);
        // Recent request IDs only: a repeat is refused, but a long session is never
        // cut off after a fixed number of requests.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var seenOrder = new Queue<string>();
        var tokens = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        using var sendGate = new SemaphoreSlim(1);
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false, AutomaticDecompression = DecompressionMethods.None };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(life.Token))
            { timeout.CancelAfter(TimeSpan.FromSeconds(10)); await socket.ConnectAsync(Endpoint(configuration, "/relay/connector/session"), timeout.Token); }
            while (socket.State == WebSocketState.Open && !life.IsCancellationRequested)
            {
                var data = await Read(socket, life.Token);
                if (data is null) break;
                var frame = JsonSerializer.Deserialize<RelayRequest>(data, RcProtocol.Json) ?? throw new RcFault("invalid_relay_frame", "Missing request.");
                if (frame.Type == "ready") continue;
                if (!RcProtocol.IsId(frame.Id)) throw new RcFault("invalid_relay_frame", "Invalid request ID.");
                if (frame.Type == "cancel")
                { if (requests.TryGetValue(frame.Id!, out var existing)) existing.Stop.Cancel(); continue; }
                if (frame.Type != "request" || !seen.Add(frame.Id!))
                    throw new RcFault("invalid_relay_frame", "Repeated or unsupported request.");
                seenOrder.Enqueue(frame.Id!);
                if (seenOrder.Count > 4096) seen.Remove(seenOrder.Dequeue());
                // The browser sends at most 4 at once; the headroom absorbs requests that
                // are still stopping after the browser has already moved on.
                if (requests.Count >= 6)
                { await ReplyError(socket, sendGate, frame.Id!, "request_limit", 429, life.Token); continue; }
                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(life.Token);
                cancellation.CancelAfter(TimeSpan.FromMinutes(2));
                if (!requests.TryAdd(frame.Id!, (cancellation, Task.CompletedTask))) { cancellation.Dispose(); continue; }
                var task = Forward(http, socket, sendGate, frame, bindingId, publicKey, tokens, cancellation.Token);
                requests[frame.Id!] = (cancellation, task);
                _ = task.ContinueWith(completed => {
                    requests.TryRemove(frame.Id!, out _); cancellation.Dispose(); _ = completed.Exception;
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        finally
        {
            life.Cancel(); socket.Abort();
            foreach (var token in tokens.Keys) runtime.Access.Close(token);
            try { await Task.WhenAll(requests.Values.Select(v => v.Task)).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
            // A session response may finish while cancellation is draining.
            foreach (var token in tokens.Keys) runtime.Access.Close(token);
        }
    }
    private async Task Forward(HttpClient http, WebSocket socket, SemaphoreSlim sendGate, RelayRequest request,
        string bindingId, string publicKey, ConcurrentDictionary<string, byte> sessions, CancellationToken stop)
    {
        try
        {
            request = request with { Headers = NormalizeHeaders(request.Headers) };
            var principal = Authorize(request, bindingId, publicKey);
            using var authorized = CancellationTokenSource.CreateLinkedTokenSource(stop, principal?.Revoked ?? CancellationToken.None);
            var token = request.Headers?.GetValueOrDefault("Authorization") ?? "";
            if (token.StartsWith("Bearer ", StringComparison.Ordinal)) sessions.TryAdd(token[7..], 0);
            using var message = new HttpRequestMessage(new HttpMethod(request.Method!), new Uri(new Uri(runtime.Settings.Configuration.PublicOrigin), request.Path!));
            message.Headers.TryAddWithoutValidation("Origin", runtime.Settings.Configuration.PublicOrigin);
            foreach (var header in request.Headers ?? [])
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            if (request.Body is not null) message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, authorized.Token);
            if (principal is not null) runtime.Access.EnsureSession(principal);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in new[] { "Content-Type", "Content-Length", "Content-Disposition", "X-Content-SHA256", "ETag", "Cache-Control" })
            {
                if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
                    headers[name] = string.Join(", ", values);
            }
            if (request.Path == "/api/rc/v1/sessions" && response.IsSuccessStatusCode)
            {
                var sessionBytes = await response.Content.ReadAsByteArrayAsync(authorized.Token);
                if (sessionBytes.Length > 16384) throw new RcFault("invalid_response", "Session response too large.");
                var session = JsonSerializer.Deserialize<SessionResult>(sessionBytes, RcProtocol.Json)
                    ?? throw new RcFault("invalid_response", "Invalid session response.");
                sessions.TryAdd(session.AccessToken, 0);
                await LockedSend(socket, sendGate, new { type = "response", id = request.Id, status = (int)response.StatusCode, headers }, authorized.Token);
                await LockedSend(socket, sendGate, new { type = "chunk", id = request.Id, data = Convert.ToBase64String(sessionBytes) }, authorized.Token);
            }
            else
            {
                await LockedSend(socket, sendGate, new { type = "response", id = request.Id, status = (int)response.StatusCode, headers }, authorized.Token);
                using var stream = await response.Content.ReadAsStreamAsync(authorized.Token);
                var buffer = new byte[32768]; long count = 0;
                var maximum = request.Path!.StartsWith("/api/rc/v1/response-file?", StringComparison.Ordinal) ? 64L * 1024 * 1024 : 8L * 1024 * 1024;
                int read;
                while ((read = await stream.ReadAsync(buffer, authorized.Token)) > 0)
                {
                    count += read;
                    if (count > maximum) throw new RcFault("response_too_large", "Response exceeds the transport limit.", 413);
                    if (principal is not null) runtime.Access.EnsureSession(principal);
                    _ = runtime.Store.Read(s => Access.Binding(s, bindingId));
                    await LockedSend(socket, sendGate, new { type = "chunk", id = request.Id, data = Convert.ToBase64String(buffer, 0, read) }, authorized.Token);
                }
            }
            await LockedSend(socket, sendGate, new { type = "end", id = request.Id }, authorized.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (socket.State != WebSocketState.Open) return;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await ReplyError(socket, sendGate, request.Id!, ex is RcFault f ? f.Code : ex is OperationCanceledException ? "request_cancelled" : "application_unavailable",
                ex is RcFault fault ? fault.Status : 503, deadline.Token); }
            catch (Exception failure) when (failure is not OutOfMemoryException) { socket.Abort(); }
        }
    }
    private static Dictionary<string, string> NormalizeHeaders(Dictionary<string, string>? headers)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headers?.Count > 4)
            throw new RcFault("route_forbidden", "The hosted connection cannot forward these headers.", 403);
        foreach (var header in headers ?? [])
        {
            var allowed = header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Idempotency-Key", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("If-Match", StringComparison.OrdinalIgnoreCase);
            if (!allowed || header.Value is null || header.Value.Length > 512
                || header.Value.Any(c => c is '\r' or '\n')
                || !normalized.TryAdd(header.Key, header.Value))
                throw new RcFault("route_forbidden", "The hosted connection cannot forward these headers.", 403);
        }
        return normalized;
    }
    private Principal? Authorize(RelayRequest request, string bindingId, string publicKey)
    {
        _ = runtime.Store.Read(s => Access.Binding(s, bindingId));
        if (!runtime.Operations.Accepting) throw new RcFault("service_stopping", "Remote-Controller is stopping.", 503);
        var method = request.Method;
        var path = request.Path ?? "";
        if (path.Length > 8192 || !AllowedRoute(method, path)
            || request.Body is not null && Encoding.UTF8.GetByteCount(request.Body) > RcProtocol.MaxMessageBytes)
            throw new RcFault("route_forbidden", "The hosted connection cannot forward this request.", 403);
        if (method == "GET" && path is "/api/rc/v1/info" or "/api/rc/v1/health") return null;
        if (method == "POST" && path is "/api/rc/v1/enrollments" or "/api/rc/v1/enrollments/poll" or "/api/rc/v1/sessions/challenges" or "/api/rc/v1/sessions")
        {
            using var document = JsonDocument.Parse(request.Body ?? "{}", new JsonDocumentOptions { MaxDepth = 16 });
            var body = document.RootElement;
            string Text(string key) => body.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
            runtime.Store.Read(s =>
            {
                if (path == "/api/rc/v1/enrollments")
                {
                    var secret = Text("invitation");
                    if (Text("public_key") != publicKey || secret.Length != 64
                        || !s.Invitations.Values.Any(i => i.BindingId == bindingId && i.TokenHash == RcProtocol.Hash(secret)))
                        throw new RcFault("invalid_invitation", "Invitation does not belong to the selected account/device.", 403);
                }
                else if (path == "/api/rc/v1/enrollments/poll")
                {
                    if (!s.Enrollments.TryGetValue(Text("id"), out var e) || e.PublicKey != publicKey
                        || !s.Invitations.TryGetValue(e.InvitationId, out var invite) || invite.BindingId != bindingId)
                        throw new RcFault("enrollment_unavailable", "Enrollment does not belong to this account/device.", 404);
                }
                else if (!s.Devices.TryGetValue(Text("device_id"), out var d) || d.BindingId != bindingId || d.PublicKey != publicKey || d.Revoked)
                    throw new RcFault("device_unavailable", "The device is not approved for this account.", 403);
                return true;
            });
            return null;
        }
        var header = request.Headers?.GetValueOrDefault("Authorization") ?? "";
        var principal = runtime.Access.Authenticate(header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : "");
        if (principal.BindingId != bindingId || runtime.Store.Read(s => s.Devices[principal.DeviceId].PublicKey) != publicKey)
            throw new RcFault("forbidden", "Session belongs to a different account or device.", 403);
        return principal;
    }
    internal static bool AllowedRoute(string? method, string path)
    {
        var split = path.Split('?', 2); var route = split[0];
        if (!route.StartsWith("/api/rc/v1/", StringComparison.Ordinal) || route.Contains('%') || route.Contains('\\') || route.Contains('#')) return false;
        if (split.Length == 2)
        {
            if (method != "GET") return false;
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery("?" + split[1]);
            var key = route is "/api/rc/v1/response-file" or "/api/rc/v1/response-content" ? "ticket" : route == "/api/rc/v1/events" ? "after" : null;
            if (key is null || query.Count != 1 || !query.TryGetValue(key, out var value) || value.Count != 1 || string.IsNullOrEmpty(value[0])) return false;
        }
        return method switch
        {
            "GET" => route is "/api/rc/v1/info" or "/api/rc/v1/health" or "/api/rc/v1/session" or "/api/rc/v1/snapshot"
                or "/api/rc/v1/capabilities" or "/api/rc/v1/events" or "/api/rc/v1/schedules" or "/api/rc/v1/response-file" or "/api/rc/v1/response-content"
                || Regex.IsMatch(route, "^/api/rc/v1/operations/[0-9a-f]{32}$"),
            "POST" => route is "/api/rc/v1/enrollments" or "/api/rc/v1/enrollments/poll" or "/api/rc/v1/sessions/challenges"
                or "/api/rc/v1/sessions" or "/api/rc/v1/operations" or "/api/rc/v1/schedules",
            "DELETE" => route == "/api/rc/v1/session" || Regex.IsMatch(route, "^/api/rc/v1/(?:schedules|operations)/[0-9a-f]{32}$"),
            "PUT" => Regex.IsMatch(route, "^/api/rc/v1/schedules/[0-9a-f]{32}$"),
            _ => false
        };
    }
    private static void ValidatePublicKey(string key)
    {
        if (key.Length is < 64 or > 512) throw new RcFault("invalid_device_key", "Invalid device key.");
        using var algorithm = ECDsa.Create();
        var bytes = Convert.FromBase64String(key);
        algorithm.ImportSubjectPublicKeyInfo(bytes, out var read);
        if (read != bytes.Length || algorithm.KeySize != 256 || algorithm.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
            throw new RcFault("invalid_device_key", "A P-256 development device key is required.");
    }
    private static bool UsablePublicKey(string key)
    {
        try { ValidatePublicKey(key); return true; }
        catch (Exception ex) when (ex is RcFault or FormatException or CryptographicException) { return false; }
    }
    private static string Field(JsonElement value, string name)
        => value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
    private static async Task<byte[]?> Read(WebSocket socket, CancellationToken stop)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), stop);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text || bytes.Length + result.Count > FrameLimit)
                throw new RcFault("invalid_relay_frame", "Invalid or oversized relay frame.");
            bytes.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) return bytes.ToArray();
        }
    }
    private static Task Send(WebSocket socket, object value, CancellationToken stop) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value, RcProtocol.Json).AsMemory(), WebSocketMessageType.Text, true, stop).AsTask();
    private static async Task LockedSend(WebSocket socket, SemaphoreSlim gate, object value, CancellationToken stop)
    {
        await gate.WaitAsync(stop);
        try { await Send(socket, value, stop); } finally { gate.Release(); }
    }
    private static Task ReplyError(WebSocket socket, SemaphoreSlim gate, string id, string code, int status, CancellationToken stop)
        => LockedSend(socket, gate, new { type = "error", id, code, status }, stop);
    public void Dispose() { lock (gate) active?.Cancel(); }
    private sealed record RelayRequest(string? Type, string? Id = null, string? Method = null, string? Path = null,
        Dictionary<string, string>? Headers = null, string? Body = null);
}
