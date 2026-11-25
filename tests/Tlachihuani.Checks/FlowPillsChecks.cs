// Tlachihuani — Angela López Mendoza · XAIHT
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Tlachihuani.FlowPills;

// Flow Pills: flows open and decide exactly as in Tlamatini, every log is followed on its own,
// a run ends only the processes it owns, and the machine's limits decide when a flow starts.
internal static class FlowPillsChecks
{
    // The child the job checks start and end: this executable, waiting quietly.
    internal static int Sleeper()
    {
        Thread.Sleep(TimeSpan.FromMinutes(2));
        return 0;
    }

    internal static Task Run()
    {
        Validator();
        Decisions();
        Documents();
        Tails();
        Jobs();
        Capacity();
        PillWithModel();
        return Task.CompletedTask;
    }

    // A flow pill against a stand-in Ollama on a loopback port: the prompt goes out to the chat
    // API with the configured model and token and the verdict-first hint, and YES takes the Yes branch.
    private static void PillWithModel()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requests = new ConcurrentQueue<string>();
        var server = new Thread(() =>
        {
            while (true)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException) { return; }
                using (client)
                {
                    var stream = client.GetStream();
                    requests.Enqueue(ReadRequest(stream));
                    byte[] body = Encoding.UTF8.GetBytes("{\"model\":\"check-model\",\"message\":{\"role\":\"assistant\",\"content\":\"## YES - as asked\"},\"done\":true}");
                    byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    stream.Write(head);
                    stream.Write(body);
                }
            }
        }) { IsBackground = true, Name = "Flow Pills check: stand-in Ollama" };
        server.Start();
        string folder = Path.Combine(Path.GetTempPath(), "tlachihuani-flow-pills-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        PromptFlowRun? run = null;
        try
        {
            string config = Path.Combine(folder, "config.json");
            File.WriteAllText(config, "{\"ollama_base_url\":\"http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture)
                + "\",\"ollama_token\":\"check-token\",\"chained-model\":\"check-model\",\"llm_client_timeout_seconds\":30}");
            var install = new TlamatiniInstall(folder, folder, "", config, folder, folder, 0, "check");
            string text = Pill(string.Join(',',
                """{"id":"q","type":"prompt","config":{"text":"Reply with the single word YES and nothing else."}}""",
                """{"id":"d","type":"decision","config":{"comparison":"equals","value":"YES"}}""",
                """{"id":"y","type":"clean_history"}""", """{"id":"n","type":"flush_embeddings"}"""),
                """{"id":"e1","source":"q","target":"d"},{"id":"e2","source":"d","target":"y","branch":"yes"},{"id":"e3","source":"d","target":"n","branch":"no"}""",
                "\"q\"").ToJsonString();
            run = new PromptFlowRun(FlowDocument.Parse(Path.Combine(folder, "ask.fpmt"), text), install);
            using var finished = new ManualResetEventSlim();
            run.Finished += _ => finished.Set();
            Require(run.TryBegin() && finished.Wait(TimeSpan.FromSeconds(30)), "The flow pill must finish");
            Require(run.State == RunState.Completed, "The flow pill must complete: " + run.Status);
            Require(run.NodeStateOf("y") == NodeState.Done && run.NodeStateOf("n") == NodeState.Idle, "A YES answer takes the Yes branch");
            string sent = requests.TryDequeue(out var request) ? request : "";
            Require(sent.StartsWith("POST /api/chat ", StringComparison.Ordinal), "The prompt goes to Ollama's chat API");
            Require(sent.Contains("Authorization: Bearer check-token", StringComparison.OrdinalIgnoreCase)
                && sent.Contains("\"model\":\"check-model\"", StringComparison.Ordinal), "Tlamatini's configured model and token are used");
            Require(sent.Contains("read by an automatic Decision", StringComparison.Ordinal), "A prompt before an equals Decision asks for its verdict first");
        }
        finally
        {
            listener.Stop();
            if (run is not null)
            {
                run.Dispose();
                try { Directory.Delete(run.Folder, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            Directory.Delete(folder, recursive: true);
        }
    }

    // One HTTP request as text: its head, then exactly Content-Length bytes of body.
    private static string ReadRequest(NetworkStream stream)
    {
        var data = new List<byte>();
        var buffer = new byte[8192];
        int headEnd = -1, length = 0;
        while (headEnd < 0 || data.Count < headEnd + 4 + length)
        {
            int got = stream.Read(buffer, 0, buffer.Length);
            if (got == 0) break;
            data.AddRange(buffer.AsSpan(0, got).ToArray());
            if (headEnd >= 0) continue;
            string seen = Encoding.ASCII.GetString(data.ToArray());
            headEnd = seen.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headEnd < 0) continue;
            foreach (var line in seen[..headEnd].Split("\r\n"))
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    length = int.Parse(line["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture);
        }
        return Encoding.UTF8.GetString(data.ToArray());
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static string Refusal(Action body)
    {
        try { body(); }
        catch (FlowPillsException ex) { return ex.Message; }
        throw new InvalidOperationException("Expected the flow to be refused");
    }

    private static JsonObject Pill(string nodes, string edges, string start) => JsonNode.Parse(
        "{\"format\":\"tlamatini-prompting-flow\",\"version\":2,\"name\":\"Check\",\"start\":" + start
        + ",\"nodes\":[" + nodes + "],\"edges\":[" + edges + "]}")!.AsObject();

    private static void Validator()
    {
        const string prompt = """{"id":"a","type":"prompt","label":"Ask","x":10,"y":20,"config":{"text":"Say {{last_output}}"}}""";
        const string decision = """{"id":"d","type":"decision","config":{"comparison":"equals","value":"YES"}}""";
        const string yes = """{"id":"y","type":"clean_history"}""", no = """{"id":"n","type":"flush_embeddings"}""";
        const string wired = """{"id":"e1","source":"a","target":"d"},{"id":"e2","source":"d","target":"y","branch":"yes"}""";
        var good = PromptFlowValidator.Validate(Pill(string.Join(',', prompt, decision, yes, no),
            wired + """,{"id":"e3","source":"d","target":"n","branch":"no"}""", "\"a\""), playable: true);
        Require(good["nodes"]!.AsArray().Count == 4 && PromptFlowValidator.S(good["start"]) == "a", "A complete flow pill must validate");
        Require(Refusal(() => PromptFlowValidator.Validate(Pill(string.Join(',', prompt, decision, yes), wired, "\"a\""), true))
            .Contains("Yes and No", StringComparison.Ordinal), "A decision with one branch must be refused");
        Require(Refusal(() => PromptFlowValidator.Validate(Pill(prompt + "," + yes, "", "\"a\""), true))
            .Contains("unreachable", StringComparison.Ordinal), "An operation Start cannot reach must be refused");
        Require(Refusal(() => PromptFlowValidator.Validate(Pill("""{"id":"a","type":"shell"}""", "", "\"a\""), false))
            .Contains("Unknown operation", StringComparison.Ordinal), "An operation Tlamatini does not have must be refused");
        Require(Refusal(() => PromptFlowValidator.Validate(Pill("""{"id":"a","type":"prompt","config":{"text":"x","acpx":true}}""", "", "\"a\""), false))
            .Contains("ACPX requires Multi-Turn", StringComparison.Ordinal), "ACPX without Multi-Turn must be refused");
        var legacy = JsonNode.Parse("""{"format":"tlamatini-prompting-flow","version":1,"start":"c","nodes":[{"id":"c","type":"user_commentary","label":"User Commentary","config":{"text":"Your name?"}}],"edges":[]}""")!.AsObject();
        var migrated = PromptFlowValidator.Validate(legacy, playable: true);
        var node = migrated["nodes"]![0]!;
        Require(PromptFlowValidator.S(node["type"]) == "user_input" && PromptFlowValidator.S(node["label"]) == "User Input"
            && migrated["version"]!.GetValue<int>() == 2, "A version 1 commentary step becomes User Input, as in Tlamatini");
    }

    private static void Decisions()
    {
        Require(PromptDecision.EqualsVerdict("## YES - the file exists", "YES"), "An answer that opens with the value equals it");
        Require(!PromptDecision.EqualsVerdict("Yesterday", "Yes"), "A longer word is not the value");
        var equals = new JsonObject { ["comparison"] = "equals", ["value"] = "YES" };
        Require(PromptDecision.Decide(equals, "**yes** - done") && !PromptDecision.Decide(equals, "Yesterday it worked"), "equals ignores case and Markdown, not words");
        var contains = new JsonObject { ["comparison"] = "contains", ["value"] = "error" };
        Require(PromptDecision.Decide(contains, "An ERROR happened") && !PromptDecision.Decide(contains, "all fine"), "contains ignores case unless asked");
        Require(PromptDecision.Decide(new JsonObject { ["comparison"] = "is_empty" }, "  \n"), "is_empty sees a blank answer");
        Require(PromptDecision.Expand("{{last_output}} and {{last_output}}", "hi") == "hi and hi", "{{last_output}} is replaced everywhere");
    }

    private static void Documents()
    {
        const string flw = """
            {"nodes":[{"text":"Starter","left":"40px","top":"60px"},{"text":"Crawler","left":"220px","top":"60px","configData":{"url":"https://example.org"}},
             {"text":"Crawler","left":"400px","top":"60px"},{"text":"Ender","left":"580px","top":"60px"}],
             "connections":[{"sourceIndex":0,"targetIndex":1},{"sourceIndex":1,"targetIndex":2},{"sourceIndex":2,"targetIndex":3}]}
            """;
        var agents = FlowDocument.Parse(@"C:\flows\crawl.flw", flw);
        Require(agents.Kind == FlowKind.Agents && agents.Nodes.Count == 4 && agents.Edges.Count == 3, "A .flw opens with every agent and wire");
        var second = agents.Nodes[2];
        Require(second.Id == "crawler-2" && second.PoolName == "crawler_2" && second.Label == "Crawler (2)", "A repeated agent is numbered as Tlamatini numbers it");
        Require(agents.Nodes[1].X == 220 && PromptFlowValidator.S(agents.Nodes[1].Config["url"]) == "https://example.org", "Positions and settings are kept");
        Require(agents.Edges[1].Source == "crawler-1" && agents.Edges[1].Target == "crawler-2", "Wires join the agents they name");
        Require(AcpPalette.Outputs("forker") == 2 && AcpPalette.Outputs("flowhypervisor") == 0, "Every agent has the outputs it has in the canvas");
        Require(Refusal(() => FlowDocument.Parse(@"C:\flows\broken.flw", "{\"nodes\": ["))
            .Contains("not valid JSON", StringComparison.Ordinal), "A damaged file is refused with its reason");
        string text = Pill("""{"id":"a","type":"prompt","x":300,"y":120,"config":{"text":"Hello"}}""", "", "\"a\"").ToJsonString();
        var pill = FlowDocument.Parse(@"C:\flows\hello.fpmt", text);
        Require(pill.Kind == FlowKind.Prompts && pill.Start == "a" && pill.Nodes[0].Width == 200 && pill.MaxSteps == 500, "A .fpmt opens as the Prompt Flow Panel opens it");
    }

    private static void Tails()
    {
        string folder = Path.Combine(Path.GetTempPath(), "tlachihuani-flow-pills-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "agent.log");
            var tail = new LogTail("agent", file);
            Require(!tail.Poll() && !tail.Exists, "A log that does not exist yet is simply not there");
            File.WriteAllBytes(file, Encoding.UTF8.GetBytes("first line\nsecond "));
            Require(tail.Poll() && tail.Lines().SequenceEqual(["first line", "second "]), "Complete and unfinished lines are both shown");
            byte[] name = Encoding.UTF8.GetBytes("López\n");
            using (var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) stream.Write(name, 0, 2);
            tail.Poll();
            using (var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) stream.Write(name, 2, name.Length - 2);
            tail.Poll();
            Require(tail.Lines()[^1] == "second López", "A character split between two reads arrives whole");
            File.WriteAllText(file, "again\n");
            Require(tail.Poll() && tail.Lines()[^1] == "again" && tail.Lines().Any(line => line.Contains("started again", StringComparison.Ordinal)),
                "A log that starts over is followed from its new beginning");
            var other = new LogTail("other", Path.Combine(folder, "other.log"));
            File.WriteAllText(other.FilePath, "independent\n");
            Require(other.Poll() && other.Lines().SequenceEqual(["independent"]) && tail.Lines()[^1] == "again", "Each log is followed on its own");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static void Jobs()
    {
        // The checks' own executable, also when they were started as "dotnet Tlachihuani.Checks.dll".
        string self = Path.ChangeExtension(typeof(FlowPillsChecks).Assembly.Location, ".exe");
        if (!File.Exists(self)) self = Environment.ProcessPath ?? throw new InvalidOperationException("The checks need their own executable path");
        string[] sleep = ["--flow-pills-sleeper"];
        var quiet = new Dictionary<string, string> { ["TLACHIHUANI_FLOW_RUN"] = "check" };
        using (var job = new RunJob())
        {
            int first = job.Launch(self, sleep, AppContext.BaseDirectory, quiet);
            int second = job.Launch(self, sleep, AppContext.BaseDirectory, quiet);
            using var one = Process.GetProcessById(first);
            using var two = Process.GetProcessById(second);
            var inside = job.ProcessIds();
            Require(inside is not null && inside.Contains(first) && inside.Contains(second), "Both agents belong to their run");
            var start = new ProcessStartInfo(self) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(sleep[0]);
            using var outsider = Process.Start(start) ?? throw new InvalidOperationException("The outside process did not start");
            try { Require(!job.Terminate(outsider.Id) && !outsider.WaitForExit(500), "A process outside the run is never ended"); }
            finally { outsider.Kill(); outsider.WaitForExit(10_000); }
            Require(job.Terminate(first) && one.WaitForExit(10_000) && !two.HasExited, "Ending one agent ends exactly that agent");
            job.TerminateAll();
            Require(two.WaitForExit(10_000), "Ending the run ends every agent in it");
        }
        var keeper = new RunJob();
        int kept = keeper.Launch(self, sleep, AppContext.BaseDirectory, quiet);
        using var survivor = Process.GetProcessById(kept);
        keeper.Release();
        keeper.Dispose();
        try { Require(!survivor.WaitForExit(1500), "What a completed flow started keeps running, as under Tlamatini"); }
        finally { if (!survivor.HasExited) { survivor.Kill(); survivor.WaitForExit(10_000); } }
        var guard = new RunJob();
        int guarded = guard.Launch(self, sleep, AppContext.BaseDirectory, quiet);
        using var orphan = Process.GetProcessById(guarded);
        guard.Dispose();
        Require(orphan.WaitForExit(10_000), "A run that is not released takes its agents with it when it closes");
    }

    private static void Capacity()
    {
        var two = new FlowCapacity(new FlowPillsSettings(2, 0, "", ""));
        Require(two.MaxRuns == 2 && two.MaxRunsSource == "settings.json" && two.WhyNot(1, FlowKind.Agents) is null, "A setting is obeyed exactly");
        Require(two.WhyNot(2, FlowKind.Prompts) is { } reason && reason.Contains("limit is 2", StringComparison.Ordinal), "A flow over the limit waits, and says why");
        Require(new FlowCapacity(new FlowPillsSettings(null, 0, "", "")).MaxRuns == FlowCapacity.DefaultMaxRuns && FlowCapacity.DefaultMaxRuns == 256, "Up to 256 flows by default");
        Require(FlowCapacity.AutomaticReserve(4UL << 30) == 512L << 20 && FlowCapacity.AutomaticReserve(64UL << 30) == 2L << 30
            && FlowCapacity.AutomaticReserve(16UL << 30) == (long)((16UL << 30) * 0.05), "Windows keeps 5% of the memory, at least 512 MB and at most 2 GB");
        var tight = new FlowCapacity(new FlowPillsSettings(null, long.MaxValue / 2, "", ""));
        Require(tight.WhyNot(0, FlowKind.Agents) is null, "On a machine short of memory the first flow still starts");
        Require(tight.WhyNot(1, FlowKind.Agents) is { } memory && memory.StartsWith("Waiting for memory", StringComparison.Ordinal),
            "A second flow that does not fit in memory waits, with the measurement");
        Require(tight.WhyNot(5, FlowKind.Prompts) is null && two.WhyNot(2, FlowKind.Prompts) is not null,
            "Memory never holds a flow pill back; the count limit does");
    }
}
