// Tlachihuani — Angela López Mendoza · XAIHT
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json.Nodes;
using Tlachihuani.Contracts;
using Tlachihuani.Windows;

namespace Tlachihuani.RC.Server;

internal sealed class RcService(RcRuntime runtime, ILogger<RcService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var listeners = new List<NamedPipeServerStream>();
        WebApplication? web = null;
        try
        {
            for (int i = 0; i < 4; i++) listeners.Add(LocalPipe.CreateServer(runtime.Settings.Pipe, runtime.Settings.OperatorSid, i == 0));
            try
            {
                runtime.Settings.Configuration.Validate();
                web = RcWeb.Create(runtime);
                await web.StartAsync(token);
                runtime.ListenerReady = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && !token.IsCancellationRequested)
            {
                runtime.StartupError = ex is RcFault fault ? fault.Code : "listener_start_failed";
                log.LogWarning("RC listener unavailable: {Code}", runtime.StartupError);
                if (web is not null) { await web.DisposeAsync(); web = null; }
            }
            log.LogInformation("Remote-Controller local control ready; listener {Ready}; security {Profile}", runtime.ListenerReady, runtime.Settings.Configuration.SecurityProfile);
            await Task.WhenAll(listeners.Select(p => Listen(p, token)).Append(Scheduler(token)).Append(runtime.Hosted.Run(token)));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            runtime.Operations.BeginStop(); runtime.ListenerReady = false;
            runtime.Hosted.Dispose();
            foreach (var listener in listeners) listener.Dispose();
            runtime.Access.Dispose();
            if (web is not null)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try { await web.StopAsync(deadline.Token); } finally { await web.DisposeAsync(); }
            }
            log.LogInformation("Remote-Controller stopped; Torrenter and Tlamatini were not stopped.");
        }
    }
    public override async Task StopAsync(CancellationToken token)
    {
        runtime.Operations.BeginStop();
        await base.StopAsync(token);
    }
    private async Task Scheduler(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(token))
        {
            if (!runtime.ListenerReady) continue;
            try
            {
                runtime.ObserveDisconnectedAdapters();
                if (runtime.Store.Read(s => s.Schedules.Values.Any(x => x.Enabled && x.NextRun <= runtime.Clock.GetUtcNow()))) runtime.Operations.Tick();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RcFault)
            { runtime.StartupError = "scheduler_storage_unavailable"; runtime.Operations.BeginStop(); log.LogError("RC scheduling stopped because durable storage is unavailable."); }
        }
    }
    private async Task Listen(NamedPipeServerStream pipe, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await pipe.WaitForConnectionAsync(token);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                var req = await Protocol.ReadAsync<JsonObject>(pipe, deadline.Token);
                string? sid = null;
                pipe.RunAsClient(() => { using var identity = WindowsIdentity.GetCurrent(true); sid = identity?.User?.Value; });
                JsonObject reply;
                try { reply = runtime.Native(req, sid ?? ""); }
                catch (Exception ex) when (ex is RcFault or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
                { reply = new() { ["error"] = new JsonObject { ["code"] = ex is RcFault f ? f.Code : "invalid_request", ["message"] = ex is RcFault ? ex.Message : "Invalid RC control message.", ["status"] = ex is RcFault r ? r.Status : 400 } }; }
                await Protocol.WriteAsync(pipe, reply, deadline.Token);
                await pipe.ReadExactlyAsync(new byte[1], deadline.Token);
            }
            catch (Exception ex) when (!token.IsCancellationRequested && ex is not OutOfMemoryException)
            { log.LogDebug("RC native connection ended: {Type}", ex.GetType().Name); }
            finally { if (pipe.IsConnected) pipe.Disconnect(); }
        }
    }
}
