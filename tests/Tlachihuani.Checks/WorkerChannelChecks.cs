// Tlachihuani — Angela López Mendoza · XAIHT
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Tlachihuani.Jobs;

internal static class WorkerChannelChecks
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static int Echo()
    {
        while (Console.ReadLine() is { } line)
        {
            var request = JsonNode.Parse(line)!;
            Thread.Sleep((int?)request["delay_ms"] ?? 0);
            Console.WriteLine(request.ToJsonString());
        }
        return 0;
    }

    internal static async Task Run(bool realIdle = false)
    {
        await LateReply(realIdle);
        if (realIdle) return;
        await HungWorker();
    }

    private static (WorkerChannel Worker, Process Child, Channel<string> Replies) Open()
    {
        var start = new ProcessStartInfo(Path.ChangeExtension(typeof(WorkerChannelChecks).Assembly.Location, ".exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("--worker-channel-echo");
        var child = Process.Start(start)!;
        var worker = new WorkerChannel();
        var replies = (Channel<string>)typeof(WorkerChannel).GetMethod("ReadReplies", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [child])!;
        Set(worker, "process", child);
        Set(worker, "replies", replies);
        return (worker, child, replies);
    }

    private static void Set(WorkerChannel worker, string name, object value)
        => typeof(WorkerChannel).GetField(name, Fields)!.SetValue(worker, value);

    private static async Task LateReply(bool realIdle)
    {
        var (worker, child, replies) = Open();
        using (child)
        using (worker)
        {
            using var cancel = new CancellationTokenSource(200);
            try
            {
                await worker.Request(new JsonObject { ["id"] = "old", ["delay_ms"] = 600 }, cancel.Token);
                throw new InvalidOperationException("Expected canceled first caller");
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            var wait = Stopwatch.StartNew();
            while (!replies.Reader.TryPeek(out _) && wait.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(25);
            Require(replies.Reader.TryPeek(out _) && !child.HasExited, "A healthy worker's late reply must already be queued");
            if (realIdle)
            {
                long since = (long)typeof(WorkerChannel).GetField("owedSince", Fields)!.GetValue(worker)!;
                while (Stopwatch.GetElapsedTime(since) < WorkerChannel.HungAfter + TimeSpan.FromSeconds(2))
                {
                    Console.WriteLine($"Real idle regression: {Stopwatch.GetElapsedTime(since).TotalSeconds:0}s; worker alive={!child.HasExited}; reply queued={replies.Reader.TryPeek(out _)}");
                    await Task.Delay(5000);
                }
            }
            else Set(worker, "owedSince", Stopwatch.GetTimestamp() - (long)((WorkerChannel.HungAfter.TotalSeconds + 1) * Stopwatch.Frequency));
            var result = await worker.Request(new JsonObject { ["id"] = "new" }, default);
            Require((string?)result["id"] == "new" && worker.Running, "Discard the old reply and answer the current request without closing the worker");
            var next = await worker.Request(new JsonObject { ["id"] = "next" }, default);
            Require((string?)next["id"] == "next", "Reply ownership must remain synchronized");
            Console.WriteLine("PASS worker: queued late response after hung threshold preserves worker and reply ownership");
        }
    }

    private static async Task HungWorker()
    {
        var (worker, child, _) = Open();
        using (child)
        using (worker)
        {
            Set(worker, "owed", 1);
            Set(worker, "owedSince", Stopwatch.GetTimestamp() - (long)((WorkerChannel.HungAfter.TotalSeconds + 1) * Stopwatch.Frequency));
            try
            {
                await worker.Request(new JsonObject { ["id"] = "must-not-send" }, default);
                throw new InvalidOperationException("Expected genuinely missing reply to close the worker");
            }
            catch (IOException) { }
            Require(!worker.Running, "A genuinely overdue missing response must still close the worker");
            Console.WriteLine("PASS worker: genuinely missing overdue response still closes worker");
        }
    }

    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
