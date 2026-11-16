// Tlachihuani — Angela López Mendoza · XAIHT
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Tlachihuani.RC.Server;

internal static class RcWeb
{
    private const string Prefix = "/api/rc/v1";
    internal static WebApplication Create(RcRuntime runtime)
    {
        var config = runtime.Settings.Configuration;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory, WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot") });
        builder.Logging.ClearProviders(); // Request URLs and credentials must never become routine telemetry.
        if (!runtime.Settings.Installed) builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
        else builder.Logging.AddEventLog(o => o.SourceName = ".NET Runtime");
        builder.Logging.SetMinimumLevel(LogLevel.Error);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = RcProtocol.MaxMessageBytes;
            options.Limits.MaxConcurrentConnections = 64;
            options.Limits.MaxConcurrentUpgradedConnections = 16;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            options.Listen(IPAddress.Parse(config.BindAddress), config.Port);
        });
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = RcProtocol.Json.PropertyNamingPolicy;
            options.SerializerOptions.UnmappedMemberHandling = RcProtocol.Json.UnmappedMemberHandling;
            options.SerializerOptions.MaxDepth = 16;
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                // A full application page loads many immutable assets together.
                // Keep its bounded asset budget separate from operation traffic.
                var asset = context.Request.Path.StartsWithSegments("/workspace")
                    && context.Request.Path.Value!.Contains("/static/", StringComparison.Ordinal)
                    && context.Request.Method is "GET" or "HEAD";
                return RateLimitPartition.GetFixedWindowLimiter(asset ? "mobile-assets" : "rc", _ => new()
                { PermitLimit = asset ? 400 : 60, Window = TimeSpan.FromSeconds(1), QueueLimit = 0, AutoReplenishment = true });
            });
        });
        var app = builder.Build();
        var workspace = new MobileWorkspace(runtime);
        var responseFiles = new ResponseFiles(runtime);
        app.Lifetime.ApplicationStopped.Register(responseFiles.Dispose);
        app.Lifetime.ApplicationStopped.Register(workspace.Dispose);
        var origin = new Uri(config.PublicOrigin);
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; worker-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
            context.Response.Headers["Permissions-Policy"] = "microphone=(self), camera=(), geolocation=()";
            try
            {
                if (!string.Equals(context.Request.Host.Value, origin.Authority, StringComparison.OrdinalIgnoreCase)) throw new RcFault("invalid_host", "Unrecognized connection host.", 400);
                var requestOrigin = context.Request.Headers.Origin.ToString();
                if ((requestOrigin.Length > 0 && requestOrigin != origin.GetLeftPart(UriPartial.Authority)) || context.Request.Headers["Sec-Fetch-Site"] == "cross-site") throw new RcFault("invalid_origin", "Cross-origin requests are not accepted.", 403);
                if (!runtime.Operations.Accepting) throw new RcFault("service_stopping", "Remote-Controller is stopping.", 503);
                await next(context);
            }
            catch (Exception ex) when (ex is RcFault or JsonException or BadHttpRequestException or FormatException or InvalidOperationException or ArgumentException)
            {
                if (context.Response.HasStarted) { context.Abort(); return; }
                var fault = ex as RcFault;
                context.Response.StatusCode = fault?.Status ?? (ex is BadHttpRequestException bad ? bad.StatusCode : 400);
                if (context.Request.Path.StartsWithSegments("/workspace") && context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.Ordinal))
                {
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.WriteAsync("<!doctype html><html lang=\"en\"><head><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Tlamatini connection</title><link rel=\"stylesheet\" href=\"/remote.css\"></head><body><main><h1>Reconnect to your workspace</h1><p>" + System.Net.WebUtility.HtmlEncode(fault?.Message ?? "This request could not be completed.") + "</p><a href=\"/remote.html#security\">Return to RC connection</a></main></body></html>", context.RequestAborted);
                    return;
                }
                await context.Response.WriteAsJsonAsync(new { error = new { code = fault?.Code ?? "invalid_request", message = fault?.Message ?? "Invalid request." } }, RcProtocol.Json, context.RequestAborted);
            }
        });
        app.UseRateLimiter();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20), AllowedOrigins = { origin.GetLeftPart(UriPartial.Authority) } });
        app.MapGet(Prefix + "/response-content", (HttpContext c) => responseFiles.Content(c, Auth(runtime, c, "chat")));
        app.MapGet(Prefix + "/response-file", (HttpContext c) => responseFiles.Download(c, Auth(runtime, c, "chat")));
        app.MapPost(Prefix + "/workspace", (HttpContext c) => Results.Json(workspace.Open(c, Token(c)), RcProtocol.Json));
        app.MapPost("/workspace/{id}/renew-session", (HttpContext c, string id) => Results.Json(workspace.Renew(c, id, Token(c)), RcProtocol.Json));
        app.MapMethods("/workspace/{id}/{**route}", ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE"], workspace.Handle);
        app.MapGet(Prefix + "/info", () => new { protocol_major = 1, installation_id = runtime.Store.Read(s => s.InstallationId), authentication_origin = config.PublicOrigin, product = "Tlachihuani-RC Server", security_profile = config.SecurityProfile, post_quantum = false, mode = "live", features = new[] { "device-enrollment", "sessions", "operations", "schedules", "events", "account-capabilities" } });
        app.MapGet(Prefix + "/health", () => new { protocol_major = 1, ready = runtime.ListenerReady && runtime.Operations.Accepting });
        app.MapPost(Prefix + "/enrollments", async (HttpContext c) =>
        {
            var body = await Body<EnrollmentRequest>(c);
            return Results.Json(runtime.Access.Enroll(body.Invitation ?? "", body.Name ?? "", body.PublicKey ?? ""), RcProtocol.Json, statusCode: 202);
        });
        app.MapPost(Prefix + "/enrollments/poll", async (HttpContext c) =>
        {
            var body = await Body<EnrollmentPoll>(c);
            return Results.Json(runtime.Access.PollEnrollment(body.Id ?? "", body.PollSecret ?? ""), RcProtocol.Json);
        });
        app.MapPost(Prefix + "/sessions/challenges", (Delegate)(async (HttpContext c) => Results.Json(runtime.Access.CreateChallenge((await Body<DeviceRequest>(c)).DeviceId ?? ""), RcProtocol.Json)));
        app.MapPost(Prefix + "/sessions", async (HttpContext c) =>
        {
            var body = await Body<SessionRequest>(c);
            return Results.Json(runtime.Access.Establish(body.DeviceId ?? "", body.ChallengeId ?? "", body.Signature ?? ""), RcProtocol.Json);
        });
        app.MapDelete(Prefix + "/session", (HttpContext c) => { var token = Token(c); _ = runtime.Access.Authenticate(token); runtime.Access.Close(token); return Results.NoContent(); });
        app.MapGet(Prefix + "/session", (HttpContext c) =>
        {
            var principal = Auth(runtime, c);
            return runtime.Store.Read(s => { var binding = s.Bindings[principal.BindingId]; return new { principal.DeviceId, principal.Scopes, binding = new { binding.Id, binding.Edition, binding.DisplayName }, cursor = s.Cursor }; });
        });
        app.MapGet(Prefix + "/snapshot", (HttpContext c) =>
        {
            var principal = Auth(runtime, c, "monitor");
            var application = runtime.Application(principal.BindingId);
            return runtime.Store.Read(s => new { service = new { health = "ready", instance_id = runtime.InstanceId, observed_at = runtime.Clock.GetUtcNow() }, account = new { s.Bindings[principal.BindingId].DisplayName, s.Bindings[principal.BindingId].Edition }, application, cursor = s.Cursor });
        });
        app.MapGet(Prefix + "/capabilities", (HttpContext c) =>
        {
            var principal = Auth(runtime, c);
            var chat = principal.Scopes.Contains("chat", StringComparer.Ordinal);
            var workspaceAccess = principal.Scopes.Contains("workspace", StringComparer.Ordinal);
            if (!chat && !workspaceAccess) throw new RcFault("forbidden", "Conversation or workspace permission is required.", 403);
            return runtime.ClientCapabilities(principal.BindingId, chat, workspaceAccess);
        });
        app.MapPost(Prefix + "/operations", async (HttpContext c) =>
        {
            var principal = Auth(runtime, c);
            var body = await Body<OperationRequest>(c);
            var result = runtime.Operations.Submit(principal, c.Request.Headers["Idempotency-Key"].ToString(), body.Operation ?? "", body.Payload, body.DeadlineSeconds);
            return Results.Json(result, RcProtocol.Json, statusCode: 202);
        });
        app.MapGet(Prefix + "/operations/{id}", (HttpContext c, string id) => runtime.Operations.Get(Auth(runtime, c), id));
        app.MapDelete(Prefix + "/operations/{id}", (HttpContext c, string id) => { runtime.Operations.Cancel(Auth(runtime, c), id); return Results.NoContent(); });
        app.MapGet(Prefix + "/events", (HttpContext c) =>
        {
            if (!long.TryParse(c.Request.Query["after"], out var cursor)) cursor = 0;
            return runtime.Operations.Events(Auth(runtime, c, "monitor"), cursor);
        });
        app.MapGet(Prefix + "/schedules", (HttpContext c) => new { schedules = runtime.Operations.Schedules(Auth(runtime, c, "schedules")) });
        app.MapPost(Prefix + "/schedules", async (HttpContext c) =>
        {
            var p = Auth(runtime, c, "schedules"); var b = await Body<ScheduleRequest>(c);
            var schedule = new RcSchedule("", p.BindingId, b.Name ?? "", b.Operation ?? "", b.Payload, b.TimeZone ?? "", b.LocalTime ?? "", b.Days ?? [], b.MissedRun ?? "skip", b.Enabled, 0, default, b.GraceMinutes);
            return Results.Json(runtime.Operations.SaveSchedule(p, schedule, null), RcProtocol.Json, statusCode: 201);
        });
        app.MapPut(Prefix + "/schedules/{id}", async (HttpContext c, string id) =>
        {
            var p = Auth(runtime, c, "schedules"); var b = await Body<ScheduleRequest>(c);
            return Results.Json(runtime.Operations.SaveSchedule(p, new(id, p.BindingId, b.Name ?? "", b.Operation ?? "", b.Payload, b.TimeZone ?? "", b.LocalTime ?? "", b.Days ?? [], b.MissedRun ?? "skip", b.Enabled, 0, default, b.GraceMinutes), Revision(c)), RcProtocol.Json);
        });
        app.MapDelete(Prefix + "/schedules/{id}", (HttpContext c, string id) => { runtime.Operations.RemoveSchedule(Auth(runtime, c, "schedules"), id, Revision(c)); return Results.NoContent(); });
        app.MapPost(Prefix + "/connection-ticket", (HttpContext c) => { _ = Auth(runtime, c, "monitor"); return new { ticket = runtime.Access.IssueTicket(Token(c)) }; });
        app.MapGet(Prefix + "/channel", (HttpContext c) => Channel(c, runtime));
        MapAssets(app);
        return app;
    }
    private static async Task<T> Body<T>(HttpContext context)
    {
        if (!context.Request.HasJsonContentType()) throw new RcFault("unsupported_media_type", "Send application/json.", 415);
        return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, RcProtocol.Json, context.RequestAborted) ?? throw new RcFault("invalid_request", "JSON body is required.", 400);
    }
    private static string Token(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : "";
    }
    private static Principal Auth(RcRuntime runtime, HttpContext context, string? scope = null) => runtime.Access.Authenticate(Token(context), scope);
    private static long Revision(HttpContext context) => long.TryParse(context.Request.Headers.IfMatch.ToString().Trim('"'), out var revision) && revision > 0 ? revision : throw new RcFault("precondition_required", "If-Match revision is required.", 428);
    private static void MapAssets(WebApplication app)
    {
        // An explicit package allowlist prevents any route from serving state, logs or sibling checkouts.
        var assets = new Dictionary<string, (string File, string Type)>(StringComparer.Ordinal)
        {
            ["/"] = ("remote.html", "text/html; charset=utf-8"), ["/remote.html"] = ("remote.html", "text/html; charset=utf-8"),
            ["/remote-transport.js"] = ("remote-transport.js", "text/javascript; charset=utf-8"),
            ["/remote-hosted.js"] = ("remote-hosted.js", "text/javascript; charset=utf-8"),
            ["/remote-i18n.js"] = ("remote-i18n.js", "text/javascript; charset=utf-8"),
            ["/remote.js"] = ("remote.js", "text/javascript; charset=utf-8"), ["/remote.css"] = ("remote.css", "text/css; charset=utf-8"),
            ["/remote-sw.js"] = ("remote-sw.js", "text/javascript; charset=utf-8"), ["/remote.webmanifest"] = ("remote.webmanifest", "application/manifest+json"),
            ["/mobile-transport.js"] = ("mobile-transport.js", "text/javascript; charset=utf-8"),
            ["/mobile-workspace.js"] = ("mobile-workspace.js", "text/javascript; charset=utf-8"),
            ["/remote-response.js"] = ("remote-response.js", "text/javascript; charset=utf-8"),
            ["/remote-response.css"] = ("remote-response.css", "text/css; charset=utf-8"),
            ["/vendor/purify.min.js"] = ("vendor/purify.min.js", "text/javascript; charset=utf-8"),
            ["/mobile-download.js"] = ("mobile-download.js", "text/javascript; charset=utf-8"),
            ["/mobile-workspace.css"] = ("mobile-workspace.css", "text/css; charset=utf-8"),
            ["/icons/xt-192.png"] = ("icons/xt-192.png", "image/png"), ["/icons/xt-512.png"] = ("icons/xt-512.png", "image/png")
        };
        foreach (var (route, asset) in assets)
            app.MapMethods(route, ["GET", "HEAD"], (HttpContext c) =>
            {
                var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", asset.File.Replace('/', Path.DirectorySeparatorChar));
                StateStore.RejectProfileOrReparse(path);
                if (!File.Exists(path)) return Results.NotFound();
                if (route == "/remote-sw.js") c.Response.Headers["Service-Worker-Allowed"] = "/";
                return Results.File(path, asset.Type, enableRangeProcessing: false);
            });
    }
    private static async Task Channel(HttpContext context, RcRuntime runtime)
    {
        if (!context.WebSockets.IsWebSocketRequest || !context.WebSockets.WebSocketRequestedProtocols.Contains("tlachihuani.rc.v1") || context.Request.Headers.Origin != new Uri(runtime.Settings.Configuration.PublicOrigin).GetLeftPart(UriPartial.Authority))
            throw new RcFault("invalid_channel", "A same-origin RC WebSocket is required.", 400);
        using var socket = await context.WebSockets.AcceptWebSocketAsync("tlachihuani.rc.v1");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var buffer = new byte[1024];
            var received = await socket.ReceiveAsync(buffer.AsMemory(), deadline.Token);
            if (!received.EndOfMessage || received.MessageType != WebSocketMessageType.Text) throw new RcFault("unauthorized", "Connection authentication required.", 401);
            var proof = JsonSerializer.Deserialize<TicketRequest>(buffer.AsSpan(0, received.Count), RcProtocol.Json) ?? throw new RcFault("unauthorized", "Missing connection ticket.", 401);
            var principal = runtime.Access.ConsumeTicket(proof.Ticket ?? "");
            Operations.Permit(principal, "monitor");
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, principal.Revoked);
            long cursor = 0;
            while (socket.State == WebSocketState.Open && runtime.Operations.Accepting)
            {
                runtime.Access.EnsureSession(principal);
                var state = runtime.Store.Read(s => s.Cursor);
                if (state != cursor || cursor == 0)
                {
                    var payload = JsonSerializer.SerializeToUtf8Bytes(runtime.Operations.Events(principal, cursor), RcProtocol.Json);
                    await socket.SendAsync(payload, WebSocketMessageType.Text, true, stop.Token);
                    cursor = state;
                }
                await Task.Delay(1000, stop.Token);
            }
            if (socket.State == WebSocketState.Open) await socket.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "service_stopping", CancellationToken.None);
        }
        catch (Exception ex) when (ex is RcFault or JsonException or OperationCanceledException or WebSocketException)
        {
            if (socket.State == WebSocketState.Open)
            {
                using var close = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "session_ended", close.Token); } catch (Exception error) when (error is WebSocketException or OperationCanceledException) { socket.Abort(); }
            }
        }
    }
    private sealed record EnrollmentRequest(string? Invitation, string? Name, string? PublicKey);
    private sealed record EnrollmentPoll(string? Id, string? PollSecret);
    private sealed record DeviceRequest(string? DeviceId);
    private sealed record SessionRequest(string? DeviceId, string? ChallengeId, string? Signature);
    private sealed record TicketRequest(string? Ticket);
    private sealed record OperationRequest(string? Operation, JsonElement Payload, int DeadlineSeconds = 120);
    private sealed record ScheduleRequest(string? Name, string? Operation, JsonElement Payload, string? TimeZone, string? LocalTime, int[]? Days, string? MissedRun = "skip", bool Enabled = true, int GraceMinutes = 15);
}
