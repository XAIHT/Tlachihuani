// Tlachihuani — Angela López Mendoza · XAIHT
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Features;

namespace Tlachihuani.RC.Server;

/// <summary>
/// A device/account-bound gateway to the installed application's existing web
/// behavior. Normal Django login remains mandatory. No caller chooses a host,
/// port, executable or another account. Upstream session cookies stay in memory.
/// </summary>
internal sealed class MobileWorkspace(RcRuntime runtime) : IDisposable
{
    private const string CookieName = "tlm_rc_mobile";
    private readonly object gate = new();
    private readonly ConcurrentDictionary<string, Workspace> sessions = new(StringComparer.Ordinal);
    private sealed class Workspace(string token, Principal owner, Uri endpoint, AccountBinding binding) : IDisposable
    {
        internal readonly string Id = RcProtocol.Id(), Cookie = RcProtocol.Token();
        internal string Token = token;
        internal readonly string DeviceId = owner.DeviceId;
        internal readonly Uri Endpoint = endpoint;
        internal readonly AccountBinding Binding = binding;
        internal readonly CookieContainer Cookies = new();
        internal HttpClient Client { get; private set; } = null!;
        internal readonly SemaphoreSlim LoginGate = new(1);
        internal string Prefix => "/workspace/" + Id;
        internal string CsrfCookieName => Binding.Edition == "spanish" ? "tlm_csrf_es_mx" : "csrftoken";
        internal void Initialize() => Client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false, CookieContainer = Cookies,
            AutomaticDecompression = DecompressionMethods.All, ConnectTimeout = TimeSpan.FromSeconds(5)
        }) { BaseAddress = Endpoint, Timeout = TimeSpan.FromMinutes(5) };
        // Requests already holding the managed gate must still be able to
        // release it when an expired session's HTTP client is cancelled.
        public void Dispose() { Client?.Dispose(); }
    }

    internal object Open(HttpContext context, string token)
    {
        var principal = runtime.Access.Authenticate(token);
        Operations.Permit(principal, "workspace");
        var endpoint = runtime.WorkspaceEndpoint(principal.BindingId);
        var binding = runtime.Store.Read(s => s.Bindings[principal.BindingId]);
        Workspace workspace;
        lock (gate)
        {
            foreach (var old in sessions.Values)
            {
                try { runtime.Access.Authenticate(old.Token); }
                catch (RcFault) { if (sessions.TryRemove(old.Id, out var expired)) expired.Dispose(); }
            }
            workspace = sessions.Values.FirstOrDefault(w => w.Token == token && w.Endpoint == endpoint)
                ?? new Workspace(token, principal, endpoint, binding);
            if (!sessions.ContainsKey(workspace.Id))
            {
                if (sessions.Count >= runtime.Settings.Configuration.MaxSessions * 2)
                    throw new RcFault("workspace_limit", "Close an unused mobile workspace before opening another.", 429);
                workspace.Initialize();
                if (!sessions.TryAdd(workspace.Id, workspace)) { workspace.Dispose(); throw new InvalidOperationException(); }
            }
        }
        context.Response.Cookies.Append(CookieName, workspace.Cookie, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps,
            Path = workspace.Prefix + "/", IsEssential = true
        });
        return new { url = workspace.Prefix + "/agent/agent/", edition = binding.Edition };
    }

    private Workspace CookieWorkspace(HttpContext context, string id)
    {
        if (!RcProtocol.IsId(id) || !sessions.TryGetValue(id, out var workspace)
            || !context.Request.Cookies.TryGetValue(CookieName, out var proof)
            || proof.Length != workspace.Cookie.Length
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(proof), Encoding.ASCII.GetBytes(workspace.Cookie)))
            throw new RcFault("unauthorized", "Open this workspace from your approved RC connection.", 401);
        return workspace;
    }

    internal object Renew(HttpContext context, string id, string token)
    {
        var workspace = CookieWorkspace(context, id);
        var principal = runtime.Access.Authenticate(token);
        Operations.Permit(principal, "workspace");
        if (principal.BindingId != workspace.Binding.Id || principal.DeviceId != workspace.DeviceId
            || runtime.WorkspaceEndpoint(principal.BindingId) != workspace.Endpoint)
            throw new RcFault("account_mismatch", "Reconnect with this workspace's approved device and account.", 403);
        lock (gate)
        {
            var previous = workspace.Token;
            workspace.Token = token;
            if (previous != token) runtime.Access.Close(previous);
        }
        return new { renewed = true };
    }

    private Workspace Authorize(HttpContext context, string id)
    {
        var workspace = CookieWorkspace(context, id);
        var principal = runtime.Access.Authenticate(workspace.Token);
        Operations.Permit(principal, "workspace");
        if (principal.BindingId != workspace.Binding.Id || principal.DeviceId != workspace.DeviceId
            || runtime.WorkspaceEndpoint(principal.BindingId) != workspace.Endpoint)
            throw new RcFault("account_changed", "Reconnect to the selected Tlamatini account.", 403);
        return workspace;
    }

    private static bool LoginPath(string path) => path is "/" or "/agent/" or "/agent/logout/";
    private static async Task<bool> VerifyAccount(Workspace workspace, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, "agent/remote-controller/session/");
        using var response = await workspace.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return false;
        if (!response.IsSuccessStatusCode) throw new RcFault("identity_unavailable", "The installed application cannot verify this mobile account. Update Tlamatini.", 503);
        var bytes = await ReadBounded(response.Content, 4096, deadline.Token);
        try
        {
            using var identity = JsonDocument.Parse(bytes);
            var value = identity.RootElement;
            if (!value.GetProperty("authenticated").GetBoolean()) return false;
            if (!value.GetProperty("active").GetBoolean() || !value.GetProperty("enabled").GetBoolean()
                || value.GetProperty("account_id").GetString() != workspace.Binding.AccountId
                || value.GetProperty("binding_id").GetString() != workspace.Binding.Id
                || value.GetProperty("edition").GetString() != workspace.Binding.Edition)
                throw new RcFault("account_mismatch", "Sign in with the exact Tlamatini account selected for this approved device.", 403);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new RcFault("invalid_identity", "Tlamatini returned an invalid account identity. Reconnect after updating the application.", 502); }
    }

    internal async Task Handle(HttpContext context, string id, string? route)
    {
        try { await Forward(context, id, route); }
        catch (HttpRequestException) { throw new RcFault("application_unavailable", "Tlamatini is not responding. Check its status in RC.", 503); }
        catch (WebSocketException) { throw new RcFault("application_unavailable", "Tlamatini's live connection is unavailable. Reconnect from RC.", 503); }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        { throw new RcFault("application_timeout", "Tlamatini did not respond before the deadline. Check its status before retrying.", 504); }
    }

    private async Task Forward(HttpContext context, string id, string? route)
    {
        // Ordinary same-origin HTML forms must retain a verifiable Origin.
        // no-referrer can make browser navigation POSTs send Origin: null.
        // same-origin still suppresses referrers to every external destination.
        context.Response.Headers["Referrer-Policy"] = "same-origin";
        var workspace = Authorize(context, id);
        var path = "/" + (route ?? "");
        if (!MobileRoutes.Allows(path, context.Request.Method, context.WebSockets.IsWebSocketRequest))
            throw new RcFault("unsupported_mobile_route", "This action is outside the mobile workspace.", 404);
        var query = context.Request.QueryString.Value ?? "";
        if (query.Length > 8192) throw new RcFault("invalid_request", "The request is too long.", 400);
        if (context.WebSockets.IsWebSocketRequest)
        {
            await Socket(context, workspace, path + query);
            return;
        }
        // Allow the app's real login page and its immutable assets. Every other
        // request verifies the currently logged-in immutable identity first.
        var asset = path.StartsWith("/static/", StringComparison.Ordinal);
        var login = LoginPath(path);
        HttpResponseMessage? response = null;
        // A login response can replace the shared upstream cookie. Serialize
        // account verification through response headers with login/logout, so
        // no request can be sent as an account that changed after verification.
        // Static assets carry no account operations and do not take this gate.
        if (!asset) await workspace.LoginGate.WaitAsync(context.RequestAborted);
        try
        {
            if (!asset && !login && !await VerifyAccount(workspace, context.RequestAborted))
            {
                context.Response.Redirect(workspace.Prefix + "/?next=%2Fagent%2Fagent%2F");
                return;
            }
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
                size.MaxRequestBodySize = 64 * 1024 * 1024;
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), path + query);
            if (context.Request.ContentLength is > 0 || context.Request.Headers.TransferEncoding.Count > 0)
            {
                request.Content = new StreamContent(context.Request.Body);
                // Django's multipart parser requires a known content length.
                // StreamContent otherwise changes an ordinary browser upload
                // into chunked HTTP and the application sees no uploaded file.
                if (context.Request.ContentLength is { } length)
                    request.Content.Headers.ContentLength = length;
                else
                {
                    using var incoming = request.Content;
                    request.Content = new ByteArrayContent(await ReadBounded(incoming, 64 * 1024 * 1024, context.RequestAborted, requestBody: true));
                }
                if (context.Request.ContentType is { } contentType) request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            }
            foreach (var name in new[] { "Accept", "Range", "If-Match", "Idempotency-Key", "X-Torrenter-Profile", "X-Requested-With" })
                if (context.Request.Headers.TryGetValue(name, out var header)) request.Headers.TryAddWithoutValidation(name, header.ToArray());
            // The outer gateway has already required the exact RC Origin. The
            // upstream normal CSRF checks still receive their own cookie/token.
            request.Headers.TryAddWithoutValidation("Origin", workspace.Endpoint.GetLeftPart(UriPartial.Authority));
            request.Headers.Referrer = workspace.Endpoint;
            var csrf = workspace.Cookies.GetCookies(workspace.Endpoint)[workspace.CsrfCookieName]?.Value;
            if (!string.IsNullOrEmpty(csrf)) request.Headers.TryAddWithoutValidation("X-CSRFToken", csrf);
            response = await workspace.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            if (login && context.Request.Method == "POST") await VerifyAccount(workspace, context.RequestAborted);
        }
        catch { response?.Dispose(); throw; }
        finally { if (!asset) workspace.LoginGate.Release(); }
        using (response)
        {
            context.Response.StatusCode = (int)response.StatusCode;
            // Django's JavaScript reads its non-secret CSRF cookie. Ports share
            // a browser cookie host, so a prior direct app login can otherwise
            // supply another session's token to multipart forms. Scope the
            // upstream token to this workspace; session cookies stay private.
            if (!asset && workspace.Cookies.GetCookies(workspace.Endpoint)[workspace.CsrfCookieName]?.Value is { Length: > 0 } browserCsrf)
                context.Response.Cookies.Append(workspace.CsrfCookieName, browserCsrf, new CookieOptions
                {
                    HttpOnly = false, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps,
                    Path = workspace.Prefix + "/", IsEssential = true
                });
            if (response.Headers.Location is { } location)
            {
                var destination = new Uri(workspace.Endpoint, location);
                if (destination.Scheme != workspace.Endpoint.Scheme || destination.Authority != workspace.Endpoint.Authority
                    || !MobileRoutes.Allows(destination.AbsolutePath, "GET", false))
                    throw new RcFault("invalid_redirect", "The application returned an unsupported destination.", 502);
                context.Response.Headers.Location = workspace.Prefix + destination.PathAndQuery + destination.Fragment;
                return;
            }
            var type = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? type;
            context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https:; media-src 'self' blob: https:; connect-src 'self'; worker-src 'self' blob:; font-src 'self' data:; frame-src 'self' blob:; object-src 'none'; base-uri 'self'; frame-ancestors 'self'; form-action 'self'";
            foreach (var name in new[] { "Content-Disposition", "Content-Range", "Accept-Ranges", "X-Canvas-Revision", "X-Content-SHA256" })
                if (response.Content.Headers.TryGetValues(name, out var header) || response.Headers.TryGetValues(name, out header)) context.Response.Headers[name] = header.ToArray();
            if (type is "text/html" or "text/css" && !string.Equals(response.Content.Headers.ContentDisposition?.DispositionType, "attachment", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = await ReadBounded(response.Content, 8 * 1024 * 1024, context.RequestAborted);
                var content = Encoding.UTF8.GetString(bytes);
                // Only root-relative attributes/CSS resources are rebased. User
                // text and model answers are not evaluated or regex-parsed as HTML.
                content = Regex.Replace(content, "(?<lead>(?:src|href|action|data-viewer-url)=[\"']|url\\([\"']?)/(?!(?:/|workspace/))", m => m.Groups["lead"].Value + workspace.Prefix + "/", RegexOptions.IgnoreCase);
                if (type == "text/html")
                {
                    var bootstrap = "<script src=\"/mobile-transport.js\" data-workspace=\"" + workspace.Prefix + "\" data-edition=\"" + workspace.Binding.Edition + "\" data-upstream=\"" + workspace.Endpoint.GetLeftPart(UriPartial.Authority) + "\"></script>";
                    // Match the document head, never ordinary <header> UI.
                    // Repeated transport wrappers would also rebase RC renewal
                    // requests into the application instead of the RC service.
                    content = new Regex("<head\\b[^>]*>", RegexOptions.IgnoreCase).Replace(content, m => m.Value + bootstrap, 1);
                    content = new Regex("</head\\s*>", RegexOptions.IgnoreCase).Replace(content, "<link rel=\"stylesheet\" href=\"/mobile-workspace.css\"><script defer src=\"/mobile-download.js\"></script><script defer src=\"/mobile-workspace.js\"></script></head>", 1);
                }
                await context.Response.WriteAsync(content, context.RequestAborted);
            }
            else { if (response.Content.Headers.ContentLength is { } contentLength) context.Response.ContentLength = contentLength; await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted); }
        }
    }

    private static async Task<byte[]> ReadBounded(HttpContent content, int limit, CancellationToken token, bool requestBody = false)
    {
        RcFault TooLarge() => requestBody
            ? new RcFault("request_too_large", "The upload exceeds the mobile limit.", 413)
            : new RcFault("response_too_large", "The application response exceeds the mobile limit.", 502);
        if (content.Headers.ContentLength > limit) throw TooLarge();
        await using var stream = await content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[Math.Min(limit + 1, 65536)];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit + 1L - output.Length)), token);
            if (read == 0) return output.ToArray();
            if (output.Length + read > limit) throw TooLarge();
            output.Write(buffer, 0, read);
        }
    }

    private async Task Socket(HttpContext context, Workspace workspace, string route)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        using var upstream = new ClientWebSocket();
        upstream.Options.Proxy = null;
        upstream.Options.Cookies = workspace.Cookies;
        upstream.Options.SetRequestHeader("Origin", workspace.Endpoint.GetLeftPart(UriPartial.Authority));
        foreach (var protocol in context.WebSockets.WebSocketRequestedProtocols) upstream.Options.AddSubProtocol(protocol);
        await workspace.LoginGate.WaitAsync(stop.Token);
        try
        {
            if (!await VerifyAccount(workspace, stop.Token)) throw new RcFault("login_required", "Sign in to Tlamatini first.", 401);
            await upstream.ConnectAsync(new UriBuilder(new Uri(workspace.Endpoint, route)) { Scheme = "ws" }.Uri, stop.Token);
        }
        finally { workspace.LoginGate.Release(); }
        using var client = await context.WebSockets.AcceptWebSocketAsync(upstream.SubProtocol);
        async Task Copy(WebSocket source, WebSocket destination)
        {
            var buffer = new byte[32768];
            long messageSize = 0;
            while (!stop.IsCancellationRequested)
            {
                var frame = await source.ReceiveAsync(buffer.AsMemory(), stop.Token);
                if (frame.MessageType == WebSocketMessageType.Close) break;
                messageSize += frame.Count;
                if (messageSize > 64 * 1024 * 1024) throw new RcFault("message_too_large", "The mobile frame exceeds the supported limit.", 413);
                await destination.SendAsync(buffer.AsMemory(0, frame.Count), frame.MessageType, frame.EndOfMessage, stop.Token);
                if (frame.EndOfMessage) messageSize = 0;
            }
        }
        async Task Authority()
        {
            var identityDue = DateTimeOffset.MinValue;
            while (!stop.IsCancellationRequested)
            {
                // Renewal replaces only the same device/account authority. A
                // revoked or expired current grant closes the channel promptly.
                var current = runtime.Access.Authenticate(workspace.Token);
                Operations.Permit(current, "workspace");
                if (!runtime.Operations.Accepting) break;
                if (DateTimeOffset.UtcNow >= identityDue)
                {
                    if (runtime.WorkspaceEndpoint(current.BindingId) != workspace.Endpoint) break;
                    await workspace.LoginGate.WaitAsync(stop.Token);
                    try { if (!await VerifyAccount(workspace, stop.Token)) break; }
                    finally { workspace.LoginGate.Release(); }
                    identityDue = DateTimeOffset.UtcNow.AddSeconds(5);
                }
                await Task.Delay(1000, stop.Token);
            }
        }
        var tasks = new[] { Copy(client, upstream), Copy(upstream, client), Authority() };
        await Task.WhenAny(tasks);
        stop.Cancel(); client.Abort(); upstream.Abort();
        try { await Task.WhenAll(tasks); } catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or HttpRequestException or RcFault) { }
    }

    public void Dispose() { foreach (var workspace in sessions.Values) workspace.Dispose(); sessions.Clear(); }
}
