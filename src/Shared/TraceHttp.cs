// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (request logging itself)
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Tlachihuani.Contracts;

namespace Tlachihuani.Shared;

// Every HTTP request and WebSocket upgrade a Tlachihuani web server receives: the caller,
// the request, the answer and the time it took. One-time codes in addresses and
// credentials in headers are never written (TraceLog masks them; headers are only
// marked present or absent).
internal static class TraceHttp
{
    public static void UseTraceHttp(this IApplicationBuilder app) => app.Use(async (HttpContext ctx, Func<Task> next) =>
    {
        var r = ctx.Request;
        // One pass per request kind: a health check repeated every few seconds is written
        // once and then only when its answer changes (see TraceLog.Pass).
        using var pass = TraceLog.Pass($"HTTP {r.Method} {r.Path}");
        var started = Stopwatch.GetTimestamp();
        string id = ctx.TraceIdentifier;
        TraceLog.I($"HTTP ⇐ #{id} {ctx.Connection.RemoteIpAddress}:{ctx.Connection.RemotePort} {r.Protocol} {r.Method} {r.Scheme}://{r.Host}{r.PathBase}{r.Path}{r.QueryString} " +
            $"websocket={ctx.WebSockets.IsWebSocketRequest} origin={r.Headers.Origin} referer={r.Headers.Referer} fetch-site={r.Headers["Sec-Fetch-Site"]} " +
            $"type={r.ContentType} length={r.ContentLength} accept={r.Headers.Accept} language={r.Headers.AcceptLanguage} agent={r.Headers.UserAgent} " +
            $"authorization={(r.Headers.Authorization.Count > 0 ? "present" : "none")} cookie={(r.Headers.Cookie.Count > 0 ? "present" : "none")}");
        ctx.Response.OnStarting(() =>
        {
            TraceLog.T($"HTTP … #{id} response starting: {ctx.Response.StatusCode} {ctx.Response.ContentType} length={ctx.Response.ContentLength}");
            return Task.CompletedTask;
        });
        try { await next(); }
        catch (Exception ex) { TraceLog.E($"HTTP #{id} {r.Method} {r.Path} failed", ex); throw; }
        finally
        {
            TraceLog.I($"HTTP ⇒ #{id} {r.Method} {r.Path} {ctx.Response.StatusCode} {ctx.Response.ContentType} " +
                $"{Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.#} ms{(ctx.RequestAborted.IsCancellationRequested ? " (client went away)" : "")}");
        }
    });
}
