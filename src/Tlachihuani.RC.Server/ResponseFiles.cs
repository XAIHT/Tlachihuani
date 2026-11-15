// Tlachihuani — Angela López Mendoza · XAIHT
using System.Net;
using System.Net.Http.Headers;

namespace Tlachihuani.RC.Server;

/// <summary>Read one signed, account-bound response attachment from the trusted adapter endpoint.</summary>
internal sealed class ResponseFiles(RcRuntime runtime) : IDisposable
{
    private const long Maximum = 64L * 1024 * 1024;
    private readonly SemaphoreSlim slots = new(4, 4);
    private readonly HttpClient client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5)
    }) { Timeout = Timeout.InfiniteTimeSpan };

    internal async Task Download(HttpContext context, Principal principal)
    {
        Operations.Permit(principal, "chat");
        var ticket = context.Request.Query["ticket"].ToString();
        if (ticket.Length is < 1 or > 4096 || ticket.Any(c => c < 33 || c > 126))
            throw new RcFault("invalid_ticket", "Reload the conversation to refresh this file link.", 400);
        if (!await slots.WaitAsync(0, context.RequestAborted))
            throw new RcFault("download_busy", "Wait for a current download to finish, then try again.", 429);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, principal.Revoked);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            var endpoint = runtime.WorkspaceEndpoint(principal.BindingId);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(endpoint, "agent/remote-controller/response-file/?ticket=" + Uri.EscapeDataString(ticket)));
            request.Headers.Add("X-Tlachihuani-Binding", principal.BindingId);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var (code, message) = status switch
                {
                    403 or 401 => ("file_forbidden", "This file is not available to the connected account."),
                    404 => ("file_missing", "This file is no longer available in Tlamatini."),
                    409 => ("file_changed", "The file changed after this reply. Ask Tlamatini to create a new attachment."),
                    410 => ("file_expired", "This file link expired. Reload the conversation and try again."),
                    413 => ("file_too_large", "This file exceeds the 64 MiB download limit."),
                    _ => ("file_unavailable", "Tlamatini could not provide the file. Try again when it is ready.")
                };
                throw new RcFault(code, message, status is 401 or 403 or 404 or 409 or 410 or 413 ? (status == 401 ? 403 : status) : 502);
            }
            var length = response.Content.Headers.ContentLength;
            var disposition = response.Content.Headers.ContentDisposition;
            if (length is null || length < 0 || length > Maximum || disposition?.DispositionType != "attachment")
                throw new RcFault("invalid_file", "The application returned an invalid file response.", 502);
            var digest = response.Headers.TryGetValues("X-Content-SHA256", out var hashes) ? hashes.SingleOrDefault() : null;
            if (digest is not { Length: 64 } || digest.Any(c => !Uri.IsHexDigit(c)))
                throw new RcFault("invalid_file", "The application could not verify this file's integrity. Update Tlamatini.", 502);
            // Never forward cookies, redirects or script execution headers from a response file.
            runtime.Access.EnsureSession(principal);
            if (runtime.WorkspaceEndpoint(principal.BindingId) != endpoint)
                throw new RcFault("account_changed", "Reconnect to the selected Tlamatini account.", 403);
            context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            context.Response.ContentLength = length;
            context.Response.Headers.ContentDisposition = disposition.ToString();
            context.Response.Headers["X-Content-SHA256"] = digest;
            context.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
            await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
            var buffer = new byte[65536];
            long total = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, deadline.Token);
                if (read == 0) break;
                total += read;
                if (total > length) throw new RcFault("invalid_file", "The file size changed during download.", 502);
                runtime.Access.EnsureSession(principal);
                await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
            }
            if (total != length) throw new RcFault("invalid_file", "The file download was incomplete.", 502);
        }
        catch (HttpRequestException) { throw new RcFault("application_unavailable", "Start Tlamatini before downloading this file.", 503); }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        { throw new RcFault("download_interrupted", "The download was interrupted. Reconnect and try again.", 409); }
        finally { slots.Release(); }
    }

    internal async Task Content(HttpContext context, Principal principal)
    {
        Operations.Permit(principal, "chat");
        var ticket = context.Request.Query["ticket"].ToString();
        if (ticket.Length is < 1 or > 4096 || ticket.Any(c => c < 33 || c > 126))
            throw new RcFault("invalid_ticket", "Reload the conversation to refresh this response.", 400);
        if (!await slots.WaitAsync(0, context.RequestAborted))
            throw new RcFault("download_busy", "Wait for the current response to load.", 429);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, principal.Revoked);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var endpoint = runtime.WorkspaceEndpoint(principal.BindingId);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(endpoint, "agent/remote-controller/response-content/?ticket=" + Uri.EscapeDataString(ticket)));
            request.Headers.Add("X-Tlachihuani-Binding", principal.BindingId);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
                throw new RcFault("response_unavailable", "Reload the conversation to refresh this response.", response.StatusCode == HttpStatusCode.Gone ? 410 : 403);
            if (response.Content.Headers.ContentType?.MediaType != "application/json" || response.Content.Headers.ContentLength is not { } length || length is < 2 or > 8 * 1024 * 1024)
                throw new RcFault("invalid_response", "The formatted response exceeds the supported limit.", 502);
            await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var output = new MemoryStream();
            var buffer = new byte[65536];
            while (true)
            {
                var read = await input.ReadAsync(buffer, deadline.Token);
                if (read == 0) break;
                if (output.Length + read > length) throw new RcFault("invalid_response", "Invalid response size.", 502);
                output.Write(buffer, 0, read);
            }
            if (output.Length != length) throw new RcFault("invalid_response", "The response was incomplete.", 502);
            runtime.Access.EnsureSession(principal);
            if (runtime.WorkspaceEndpoint(principal.BindingId) != endpoint)
                throw new RcFault("account_changed", "Reconnect to the selected Tlamatini account.", 403);
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.Body.WriteAsync(output.ToArray(), deadline.Token);
        }
        catch (HttpRequestException) { throw new RcFault("application_unavailable", "Start Tlamatini to load this response.", 503); }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        { throw new RcFault("response_interrupted", "The response was interrupted. Reconnect and reload the conversation.", 409); }
        finally { slots.Release(); }
    }

    public void Dispose() { client.Dispose(); }
}
