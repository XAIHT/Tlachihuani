// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (XT asks for HandGI's status every few seconds; the service writes every request that changes something)
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json.Nodes;
using Tlachihuani.Contracts;
using Tlachihuani.Windows;

namespace HandGI.App;

// The pipe XT talks to: one request, one reply, then XT's acknowledgement, like every Tlachihuani
// pipe. Only the signed-in user (and SYSTEM and Administrators) can open it; a remote client is
// refused by Windows. Requests are answered on the window thread, where the engine and the windows live.
internal sealed class ControlPipe : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly List<NamedPipeServerStream> listeners = [];
    private readonly Func<JsonObject, JsonObject> answer;
    private readonly SynchronizationContext ui;

    internal ControlPipe(Func<JsonObject, JsonObject> answer, SynchronizationContext ui)
    {
        this.answer = answer;
        this.ui = ui;
        using var user = WindowsIdentity.GetCurrent();
        string sid = user.User?.Value ?? throw new InvalidOperationException("No Windows identity.");
        string name = HandGIEndpoint.PipeName();
        for (int i = 0; i < 2; i++) listeners.Add(LocalPipe.CreateServer(name, sid, i == 0));
        foreach (var pipe in listeners) _ = Task.Run(() => ListenAsync(pipe, stop.Token));
    }

    private async Task ListenAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await pipe.WaitForConnectionAsync(token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                var request = await Protocol.ReadAsync<JsonObject>(pipe, deadline.Token, tracePayload: false);
                var reply = await OnWindowThreadAsync(request, deadline.Token);
                await Protocol.WriteAsync(pipe, reply, deadline.Token, tracePayload: false);
                await pipe.ReadExactlyAsync(new byte[1], deadline.Token);
            }
            catch (Exception) when (token.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ProtocolException or InvalidOperationException or ObjectDisposedException)
            {
                TraceLog.D($"A HandGI pipe request ended early ({ex.GetType().Name}: {ex.Message})");
            }
            finally
            {
                try { if (pipe.IsConnected) pipe.Disconnect(); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException) { }
            }
        }
    }

    private async Task<JsonObject> OnWindowThreadAsync(JsonObject request, CancellationToken token)
    {
        var done = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => done.TrySetCanceled(token));
        ui.Post(_ =>
        {
            try { done.TrySetResult(answer(request)); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                TraceLog.E("HandGI could not answer XT", ex);
                done.TrySetResult(new JsonObject { ["ok"] = false, ["error"] = ex.Message });
            }
        }, null);
        return await done.Task;
    }

    public void Dispose()
    {
        stop.Cancel();
        foreach (var pipe in listeners) pipe.Dispose();
    }
}
