// Tlachihuani — Angela López Mendoza · XAIHT
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Tlachihuani.RC.Cloud;

public static class CloudWeb
{
    public static WebApplication Create(CloudConfiguration configuration)
    {
        configuration.Validate();
        var origin = configuration.PublicOrigin;
        var certificate = configuration.LoadCertificate();
        if (!certificate.HasPrivateKey) { certificate.Dispose(); throw new InvalidDataException("The development TLS certificate requires a private key."); }
        try
        {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
        builder.Logging.SetMinimumLevel(LogLevel.Warning); // The console never shows request URLs, tickets, content or credentials.
        Tlachihuani.Shared.TraceLoggingExtensions.AddTlachihuaniTrace(builder.Logging); // rc-cloud.log, codes masked
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 8192;
            options.Limits.MaxConcurrentConnections = 128;
            options.Limits.MaxConcurrentUpgradedConnections = 64;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
            options.Listen(IPAddress.Parse(origin.Host), origin.Port, listen => listen.UseHttps(certificate));
        });
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = CloudConfiguration.Json.PropertyNamingPolicy;
            options.SerializerOptions.UnmappedMemberHandling = CloudConfiguration.Json.UnmappedMemberHandling;
            options.SerializerOptions.MaxDepth = CloudConfiguration.Json.MaxDepth;
        });
        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton<CloudSessions>();
        builder.Services.AddSingleton<CloudGrantStore>();
        CloudOidc.Add(builder, configuration);
        builder.Services.AddSingleton<CloudRelay>();
        builder.Services.AddHostedService<RelaySweeper>();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetFixedWindowLimiter("loopback", _ => new()
                { PermitLimit = 200, Window = TimeSpan.FromSeconds(1), QueueLimit = 0, AutoReplenishment = true }));
            options.AddFixedWindowLimiter("login", limiter =>
            { limiter.PermitLimit = 30; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; limiter.AutoReplenishment = true; });
        });
        var app = builder.Build();
        app.Lifetime.ApplicationStopped.Register(certificate.Dispose);
        var sessions = app.Services.GetRequiredService<CloudSessions>();
        var relay = app.Services.GetRequiredService<CloudRelay>();
        app.Lifetime.ApplicationStopping.Register(relay.Dispose);
        var passwordSlots = new SemaphoreSlim(2, 2);
        Tlachihuani.Shared.TraceHttp.UseTraceHttp(app);
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; worker-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
            context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            try
            {
                if (app.Lifetime.ApplicationStopping.IsCancellationRequested) throw new CloudFault("cloud_stopping", 503);
                if (!context.Request.IsHttps || context.Connection.RemoteIpAddress is not { } peer || !IPAddress.IsLoopback(peer))
                    throw new CloudFault("development_loopback_required", 403);
                if (!string.Equals(context.Request.Host.Value, origin.Authority, StringComparison.OrdinalIgnoreCase))
                    throw new CloudFault("invalid_host", 400);
                var requestOrigin = context.Request.Headers.Origin.ToString();
                if (requestOrigin.Length > 0 && requestOrigin != configuration.Origin)
                    throw new CloudFault("invalid_origin", 403);
                var oidcCallback = configuration.AuthenticationMode == "oidc" && context.Request.Method == "GET" && context.Request.Path == CloudOidc.CallbackPath;
                var publicLanding = context.Request.Method is "GET" or "HEAD" && (context.Request.Path == "/" || context.Request.Path == "/remote.html");
                if (!oidcCallback && !publicLanding && context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
                    throw new CloudFault("invalid_origin", 403);
                await next(context);
            }
            catch (Exception ex) when (ex is CloudFault or JsonException or BadHttpRequestException or ArgumentException)
            {
                if (context.Response.HasStarted) { context.Abort(); return; }
                var fault = ex as CloudFault;
                context.Response.StatusCode = fault?.Status ?? (ex is BadHttpRequestException bad ? bad.StatusCode : 400);
                await context.Response.WriteAsJsonAsync(new { error = new { code = fault?.Code ?? "invalid_request" } }, CloudConfiguration.Json, context.RequestAborted);
            }
        });
        app.UseRateLimiter();
        if (configuration.AuthenticationMode == "oidc") app.UseAuthentication();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20), AllowedOrigins = { configuration.Origin } });
        app.MapGet("/healthz", () => new { ready = true, profile = CloudConfiguration.DevelopmentProfile, public_remote_enabled = false });
        app.MapGet("/api/hosted/bootstrap", (HttpContext context) =>
        {
            var session = sessions.Find(context.Request.Cookies[CloudSessions.CookieName]);
            if (session is null) { session = sessions.Create(null); Cookie(context, session); }
            return Results.Json(Bootstrap(session, configuration), CloudConfiguration.Json);
        });
        app.MapPost("/api/hosted/login", async (HttpContext context) =>
        {
            if (configuration.AuthenticationMode != "fixture") throw new CloudFault("fixture_login_disabled", 409);
            var previous = Session(context, sessions, configuration, authenticated: false, csrf: true);
            var request = await Body<LoginRequest>(context);
            if (!await passwordSlots.WaitAsync(0, context.RequestAborted)) throw new CloudFault("login_busy", 429);
            CloudUser user;
            try { user = sessions.VerifyPassword(request.Username ?? "", request.Password ?? ""); }
            finally { passwordSlots.Release(); }
            var session = sessions.Create(user);
            sessions.Remove(previous.Token);
            relay.CloseSession(previous.Token);
            Cookie(context, session);
            return Results.Json(Bootstrap(session, configuration), CloudConfiguration.Json);
        }).RequireRateLimiting("login");
        app.MapPost("/api/hosted/logout", (HttpContext context) =>
        {
            var session = Session(context, sessions, configuration, authenticated: false, csrf: true);
            sessions.Remove(session.Token);
            relay.CloseSession(session.Token);
            context.Response.Cookies.Delete(CloudSessions.CookieName, CookieOptions());
            return Results.NoContent();
        });
        app.MapGet("/api/hosted/computers", (HttpContext context) =>
        {
            var session = Session(context, sessions, configuration);
            return Results.Json(relay.Computers(session.User!.Id), CloudConfiguration.Json);
        });
        app.MapPost("/api/hosted/connections", async (HttpContext context) =>
        {
            var session = Session(context, sessions, configuration, csrf: true);
            return Results.Json(relay.Invite(session, await Body<ConnectionRequest>(context)), CloudConfiguration.Json, statusCode: 201);
        });
        CloudOidc.Map(app, configuration, sessions);
        app.MapDelete("/api/hosted/computers/{installationId}/accounts/{bindingId}", (HttpContext context, string installationId, string bindingId) =>
        {
            var session = Session(context, sessions, configuration, csrf: true);
            return Results.Json(new { revoked = true, grant_revision = relay.RevokeLink(session.User!.Id, installationId, bindingId) }, CloudConfiguration.Json);
        });
        // Browser HTTP/2 WebSockets use extended CONNECT; HTTP/1.1 peers use GET.
        // Both still pass the same Origin, cookie, ticket and subprotocol checks.
        app.MapMethods("/relay/client", ["GET", "CONNECT"], (HttpContext context) =>
        {
            if (context.Request.Headers.Origin.ToString() != configuration.Origin) throw new CloudFault("invalid_origin", 403);
            return relay.Client(context, Session(context, sessions, configuration));
        });
        app.MapMethods("/relay/connector", ["GET", "CONNECT"], relay.ConnectorControl);
        app.MapMethods("/relay/connector/session", ["GET", "CONNECT"], relay.ConnectorSession);
        MapAssets(app);
        return app;
        }
        catch
        {
            certificate.Dispose();
            throw;
        }
    }

    private static object Bootstrap(CloudSession session, CloudConfiguration configuration) => new
    {
        mode = "hosted", profile = CloudConfiguration.DevelopmentProfile, post_quantum = false, public_remote_enabled = false,
        authentication_mode = configuration.AuthenticationMode, login_url = configuration.AuthenticationMode == "oidc" ? "/auth/login" : null,
        user = session.User is null ? null : new { session.User.Id, session.User.Username }, csrf_token = session.CsrfToken
    };
    private static CloudSession Session(HttpContext context, CloudSessions sessions, CloudConfiguration configuration,
        bool authenticated = true, bool csrf = false)
    {
        var session = sessions.Find(context.Request.Cookies[CloudSessions.CookieName]);
        if (session is null || (authenticated && session.User is null)) throw new CloudFault("authentication_required", 401);
        if (csrf && (context.Request.Headers.Origin.ToString() != configuration.Origin
            || !CloudSessions.SameSecret(context.Request.Headers["X-CSRF-Token"].ToString(), session.CsrfToken)))
            throw new CloudFault("csrf_rejected", 403);
        return session;
    }
    private static CookieOptions CookieOptions() => new()
    { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true };
    internal static void Cookie(HttpContext context, CloudSession session)
    {
        var options = CookieOptions();
        options.Expires = session.ExpiresAt;
        context.Response.Cookies.Append(CloudSessions.CookieName, session.Token, options);
    }
    private static async Task<T> Body<T>(HttpContext context)
    {
        if (!context.Request.HasJsonContentType()) throw new CloudFault("json_required", 415);
        return await context.Request.ReadFromJsonAsync<T>(CloudConfiguration.Json, context.RequestAborted)
            ?? throw new CloudFault("invalid_request");
    }
    private static void MapAssets(WebApplication app)
    {
        var types = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["remote.html"] = "text/html; charset=utf-8", ["remote.css"] = "text/css; charset=utf-8",
            ["remote.js"] = "text/javascript; charset=utf-8", ["remote-sw.js"] = "text/javascript; charset=utf-8",
            ["remote-transport.js"] = "text/javascript; charset=utf-8", ["remote-hosted.js"] = "text/javascript; charset=utf-8",
            ["remote-i18n.js"] = "text/javascript; charset=utf-8",
            ["remote.webmanifest"] = "application/manifest+json",
            ["remote-response.js"] = "text/javascript; charset=utf-8", ["remote-response.css"] = "text/css; charset=utf-8",
            ["mobile-download.js"] = "text/javascript; charset=utf-8", ["mobile-transport.js"] = "text/javascript; charset=utf-8",
            ["mobile-workspace.js"] = "text/javascript; charset=utf-8", ["mobile-workspace.css"] = "text/css; charset=utf-8",
            ["vendor/purify.min.js"] = "text/javascript; charset=utf-8", ["vendor/DOMPurify-LICENSE"] = "text/plain; charset=utf-8",
            ["icons/xt-192.png"] = "image/png", ["icons/xt-512.png"] = "image/png"
        };
        foreach (var pair in types)
        {
            var relative = pair.Key;
            var contentType = pair.Value;
            app.MapMethods("/" + relative, ["GET", "HEAD"], () => Asset(relative, contentType));
        }
        app.MapMethods("/", ["GET", "HEAD"], () => Asset("remote.html", "text/html; charset=utf-8"));
        static IResult Asset(string relative, string contentType)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", relative.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? Results.File(path, contentType) : Results.NotFound();
        }
    }
    private sealed record LoginRequest(string Username, string Password);
    private sealed class RelaySweeper(CloudRelay relay) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try { while (await timer.WaitForNextTickAsync(stoppingToken)) relay.Sweep(); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }
}
