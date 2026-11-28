// Tlachihuani — Angela López Mendoza · XAIHT
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using HandGI.App;

internal static class ControlPipeChecks
{
    internal static async Task<int> Run()
    {
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var pipe = RuntimeHelpers.GetUninitializedObject(typeof(ControlPipe));
            var context = new QueuedContext();
            int actions = 0;
            Action duringAction = () => { };
            typeof(ControlPipe).GetField("ui", flags)!.SetValue(pipe, context);
            typeof(ControlPipe).GetField("answer", flags)!.SetValue(pipe, (Func<JsonObject, JsonObject>)(_ =>
            {
                actions++;
                duringAction();
                return new JsonObject { ["ok"] = true };
            }));
            Task<JsonObject> Send(CancellationToken token) => (Task<JsonObject>)typeof(ControlPipe)
                .GetMethod("OnWindowThreadAsync", flags)!.Invoke(pipe, [new JsonObject { ["op"] = "arm" }, token])!;

            var normal = Send(default);
            context.Pump();
            Require((bool?)(await normal)["ok"] == true && actions == 1, "Valid command must execute once");
            Console.WriteLine("PASS HandGI: valid queued command executes once");

            using var canceled = new CancellationTokenSource();
            var expired = Send(canceled.Token);
            canceled.Cancel();
            await Canceled(expired);
            context.Pump();
            Require(actions == 1, "Expired queued command executed after cancellation");
            Console.WriteLine("PASS HandGI: canceled queued command has no side effects");

            var alreadyExpired = Send(canceled.Token);
            await Canceled(alreadyExpired);
            context.Pump();
            Require(actions == 1, "Pre-canceled command executed");
            Console.WriteLine("PASS HandGI: pre-canceled command has no side effects");

            using var executing = new CancellationTokenSource();
            duringAction = executing.Cancel;
            var claimed = Send(executing.Token);
            context.Pump();
            Require((bool?)(await claimed)["ok"] == true && actions == 2, "An already executing command must retain its actual result");
            Console.WriteLine("PASS HandGI: cancellation after execution starts does not misreport it as unexecuted");
            Console.WriteLine("RESULT: 4 HandGI dispatch checks passed; no camera, pipes or input actions opened.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL " + ex); return 1; }
    }

    private static async Task Canceled(Task<JsonObject> task)
    {
        try { await task; }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Caller did not observe cancellation");
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => queue.Enqueue((callback, state));
        internal void Pump() { while (queue.TryDequeue(out var item)) item.Callback(item.State); }
    }
}
