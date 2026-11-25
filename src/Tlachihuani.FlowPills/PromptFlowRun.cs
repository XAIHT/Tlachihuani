// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (one thread per flow pill; each operation, answer and decision is written explicitly)
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tlachihuani.FlowPills;

/// <summary>A question a running flow pill asks the person at the window.</summary>
public sealed record InputRequest(string Id, string NodeId, string Kind, string Message);

// A .fpmt run with the rules of Tlamatini's FlowRunner (agent/services/prompt_flow_panel.py):
// the same step limit, the same pause and stop gate, delays that count only active time,
// absolute scheduled times, {{last_output}}, decisions, user input and history. Prompts go to
// the model Tlamatini is configured with; Feed and Flush embeddings, Clean history and
// Terminater work as there. Multi-Turn and ACPX steps need Tlamatini's own tool loop, so a pill
// that uses them is refused at its start, by name, before any operation runs.
public sealed class PromptFlowRun : FlowRun
{
    private const int HistoryMessages = 16, ContextLimit = 2_000_000, ChunkChars = 1200, ChunkOverlap = 200, TopChunks = 4, MaxReply = 100_000;
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly ManualResetEventSlim resumed = new(true), replied = new(false);
    private readonly CancellationTokenSource ending = new();
    private readonly object inputGate = new(), transcriptGate = new();
    private readonly List<ChatTurn> history = [];
    private readonly List<string> context = [];
    private readonly List<(string Text, float[] Vector)> index = [];
    private readonly LogTail transcript;
    private InputRequest? request;
    private string reply = "";
    private OllamaGateway? gateway;
    private string lastOutput = "";
    private int step;

    public PromptFlowRun(FlowDocument document, TlamatiniInstall install) : base(document, install)
    {
        transcript = new LogTail("Transcript", Path.Combine(Folder, "transcript.log"));
        AddLog(transcript);
    }

    public override bool CanPause => State == RunState.Running;
    public override bool CanResume => State == RunState.Paused;
    public InputRequest? PendingInput { get { lock (inputGate) return request; } }
    public string Model => gateway?.ChatModel ?? "";

    // Tlamatini's pause: the operation in progress finishes, the next one waits.
    public override void Pause()
    {
        if (State != RunState.Running) return;
        resumed.Reset();
        SetState(RunState.Paused, "Paused: the next operation waits");
    }

    public override void Resume()
    {
        if (State != RunState.Paused) return;
        SetState(RunState.Running, "Resumed");
        resumed.Set();
    }

    protected override void OnStop(bool force)
    {
        ending.Cancel();
        resumed.Set();
        replied.Set();
    }

    // The answer to the window's question. A stale, repeated or oversized answer is refused.
    public bool Reply(string requestId, string text)
    {
        lock (inputGate)
        {
            if (request is null || request.Id != requestId || replied.IsSet || text.Length > MaxReply) return false;
            reply = text;
            replied.Set();
            return true;
        }
    }

    protected override void Execute()
    {
        SetState(RunState.Preparing, "Checking the flow pill…");
        try { File.WriteAllText(Path.Combine(Folder, "flow.fpmt"), Document.Text, Utf8); }   // the pill exactly as it ran
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var flow = PromptFlowValidator.Validate(JsonNode.Parse(Document.Text) as JsonObject
            ?? throw new FlowPillsException("invalid_fpmt", "The flow pill is not a JSON object."), playable: true);
        var nodes = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var item in (JsonArray)flow["nodes"]!)
        {
            var node = (JsonObject)item!;
            nodes[S(node["id"])] = node;
        }
        var edges = new Dictionary<(string, string), JsonObject>();
        foreach (var item in (JsonArray)flow["edges"]!)
        {
            var edge = (JsonObject)item!;
            edges[(S(edge["source"]), S(edge["branch"]))] = edge;
        }
        foreach (var node in nodes.Values)
        {
            var config = (JsonObject)node["config"]!;
            if (Flag(config["multi_turn"]) || Flag(config["acpx"]))
                throw new FlowPillsException("needs_tlamatini", $"\"{S(node["label"])}\" uses Multi-Turn{(Flag(config["acpx"]) ? " and ACPX" : "")}, "
                    + "which runs inside Tlamatini's own tool loop. Run this pill in Tlamatini's Prompt Flow Panel, or turn Multi-Turn off for that step.");
        }
        if (nodes.Values.Any(node => S(node["type"]) is "prompt" or "programmed_prompt" or "feed_embeddings"))
            gateway = OllamaGateway.FromConfig(Install.ConfigPath);
        int maxSteps = (int)Num(flow["max_steps"]);
        string? current = S(flow["start"]);
        SetState(RunState.Running, gateway is null ? "Running" : $"Running with {gateway.ChatModel}");
        while (current is { Length: > 0 })
        {
            Gate();
            step++;
            if (step > maxSteps) throw new FlowPillsException("step_limit", "Step limit reached. Inspect the loop or raise the flow's step limit.");
            var node = nodes[current];
            string kind = S(node["type"]), label = S(node["label"]);
            var config = (JsonObject)node["config"]!;
            SetNode(current, NodeState.Running);
            Event($"Step {step}: {label} ({kind.Replace('_', ' ')})");
            string branch = "next";
            string? output = null;
            try
            {
                if (kind == "programmed_prompt") WaitForSchedule(current, config);
                Gate();
                string text = PromptDecision.Expand(S(config["text"]), lastOutput);
                switch (kind)
                {
                    case "prompt":
                    case "programmed_prompt":
                        if (edges.TryGetValue((current, "next"), out var following) && nodes.TryGetValue(S(following["target"]), out var next)
                            && S(next["type"]) == "decision" && S(((JsonObject)next["config"]!)["comparison"]) == "equals")
                            text += PromptDecision.VerdictFirstHint;
                        output = Prompt(text, label);
                        break;
                    case "feed_embeddings":
                        Feed(text);
                        break;
                    case "flush_embeddings":
                        context.Clear();
                        index.Clear();
                        Event("Embeddings flushed");
                        break;
                    case "clean_history":
                        history.Clear();
                        lastOutput = "";
                        Event("History cleaned");
                        break;
                    case "terminater":
                        output = Terminal(text, config);
                        break;
                    case "user_input":
                        output = Ask(current, "input", text);
                        history.Add(new ChatTurn("user", output));
                        break;
                    case "decision":
                        if (S(config["comparison"]) == "user")
                        {
                            string answer = Ask(current, "decision", text);
                            if (answer is not ("yes" or "no")) throw new FlowPillsException("invalid_reply", "A decision reply must be Yes or No.");
                            branch = answer;
                        }
                        else branch = PromptDecision.Decide(config, lastOutput) ? "yes" : "no";
                        Event($"Decision \"{label}\": {branch.ToUpperInvariant()}");
                        break;
                }
                Gate();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SetNode(current, NodeState.Failed);
                throw;
            }
            if (output is not null)
            {
                lastOutput = output;
                Transcript(label, output);
            }
            SetNode(current, NodeState.Done);
            if (edges.TryGetValue((current, branch), out var edge))
            {
                Traverse(S(edge["id"]));
                current = S(edge["target"]);
            }
            else current = null;
        }
        Finish(RunState.Completed, $"Completed in {step} step(s)");
    }

    // The gate before every operation: a paused pill waits here; a stopped pill ends here.
    private void Gate()
    {
        while (!resumed.Wait(200) && !ending.IsCancellationRequested) { }
        if (ending.IsCancellationRequested || StopRequested) throw new OperationCanceledException();
    }

    private void WaitForSchedule(string nodeId, JsonObject config)
    {
        if (PromptFlowValidator.ScheduledTime(S(config["scheduled_at"])) is { } when)
        {
            SetNode(nodeId, NodeState.Waiting);
            Event($"Scheduled for {when:yyyy-MM-dd HH:mm:ss zzz}");
            while (DateTimeOffset.UtcNow < when) Delay(Math.Min(0.5, (when - DateTimeOffset.UtcNow).TotalSeconds));
        }
        double seconds = Num(config["delay_seconds"]);
        if (seconds > 0)
        {
            SetNode(nodeId, NodeState.Waiting);
            Event($"Waiting {seconds.ToString("0.##", CultureInfo.InvariantCulture)} active seconds");
            Delay(seconds);
        }
        SetNode(nodeId, NodeState.Running);
    }

    // A delay counts only active time: time spent paused does not count.
    private void Delay(double seconds)
    {
        double remaining = seconds;
        while (remaining > 0)
        {
            Gate();
            double slice = Math.Min(0.2, remaining);
            if (ending.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Max(0, slice)))) break;
            remaining -= slice;
        }
        Gate();
    }

    private string Prompt(string text, string label)
    {
        var messages = new List<ChatTurn> { new("system", SystemPrompt(text)) };
        messages.AddRange(history.Skip(Math.Max(0, history.Count - HistoryMessages)));
        messages.Add(new ChatTurn("user", text));
        var service = Service();
        Event($"Asking {service.ChatModel}…");
        var started = DateTime.UtcNow;
        string answer = service.Chat(messages, ending.Token);
        if (ending.IsCancellationRequested) throw new OperationCanceledException();
        if (answer.Trim().Length == 0) throw new FlowPillsException("empty_answer", "The model returned no answer.");
        history.Add(new ChatTurn("user", text));
        history.Add(new ChatTurn("assistant", answer));
        Event($"\"{label}\" answered in {(DateTime.UtcNow - started).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s "
            + $"({answer.Length.ToString("N0", CultureInfo.InvariantCulture)} characters)");
        return answer;
    }

    private string SystemPrompt(string question)
    {
        var text = new StringBuilder("You are Tlamatini (\"one who knows\", in Nahuatl), answering one step of a flow pill run by "
            + "Tlachihuani's Flow Pills service. Answer the request directly and completely.");
        if (index.Count > 0)
        {
            var relevant = Retrieve(question);
            if (relevant.Count > 0)
            {
                text.Append("\n\nUse this context when it is relevant:\n<context>\n");
                foreach (var chunk in relevant) text.Append(chunk).Append("\n---\n");
                text.Append("</context>");
            }
        }
        return text.ToString();
    }

    private void Feed(string text)
    {
        if (text.Trim().Length == 0) throw new FlowPillsException("empty_context", "There is no text to embed.");
        if (context.Sum(item => (long)item.Length) + text.Length > ContextLimit)
            throw new FlowPillsException("context_limit", "This run's embedding text exceeds 2,000,000 characters. Flush embeddings before adding more.");
        var service = Service();
        var chunks = Chunks(text);
        var added = new List<(string, float[])>();
        for (int i = 0; i < chunks.Count; i += 32)
        {
            Gate();
            var batch = chunks.GetRange(i, Math.Min(32, chunks.Count - i));
            var vectors = service.Embed(batch, ending.Token);
            for (int j = 0; j < batch.Count; j++) added.Add((batch[j], vectors[j]));
        }
        context.Add(text);
        index.AddRange(added);
        Event($"Embedded {chunks.Count} chunk(s) with {service.EmbeddingModel} ({text.Length.ToString("N0", CultureInfo.InvariantCulture)} characters)");
    }

    private OllamaGateway Service() => gateway ?? throw new FlowPillsException("model_missing", "No model is configured for this flow pill.");

    private static List<string> Chunks(string text)
    {
        var chunks = new List<string>();
        for (int start = 0; start < text.Length; start += ChunkChars - ChunkOverlap)
        {
            int length = Math.Min(ChunkChars, text.Length - start);
            string piece = text.Substring(start, length).Trim();
            if (piece.Length > 0) chunks.Add(piece);
            if (start + length >= text.Length) break;
        }
        return chunks;
    }

    private List<string> Retrieve(string question)
    {
        float[] query = Service().Embed([question.Length > 8000 ? question[..8000] : question], ending.Token)[0];
        return index.Select(entry => (entry.Text, Score: Cosine(query, entry.Vector)))
            .OrderByDescending(entry => entry.Score).Take(TopChunks).Select(entry => entry.Text).ToList();
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, left = 0, right = 0;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            dot += a[i] * b[i];
            left += a[i] * a[i];
            right += b[i] * b[i];
        }
        return left == 0 || right == 0 ? 0 : dot / Math.Sqrt(left * right);
    }

    // One Terminater action in Tlamatini's visible terminals. A command that failed or hung is
    // still a delivered result (a Decision can branch on it); only a refusal stops the pill.
    private string Terminal(string text, JsonObject config)
    {
        string action = S(config["action"]);
        var parameters = new JsonObject { ["action"] = action, ["wait_for_job"] = true };
        foreach (var (key, value) in config) if (key != "action") parameters[key] = value?.DeepClone();
        parameters["text"] = text;
        parameters["user"] = "local";
        double wait = Math.Max(Num(config["wait_seconds"]), Num(config["user_wait_seconds"]));
        Event($"Terminater {action}…");
        var result = TerminaterBridge.Run(Install.TempFolder, parameters, TimeSpan.FromSeconds(wait + 60), ending.Token);
        string status = S(result["status"]);
        if ((result["ok"] is JsonValue ok && ok.GetValueKind() == JsonValueKind.False) || status is "refused" or "error" or "unavailable")
            throw new FlowPillsException("terminater_" + (status.Length > 0 ? status : "failed"),
                S(result["message"]) is { Length: > 0 } message ? message : $"Terminater could not {action}: {(status.Length > 0 ? status : "no answer")}.");
        Event($"Terminater {action}: {(status.Length > 0 ? status : "done")}");
        string output = S(result["output"]);
        if (output.Length == 0) output = S(result["message"]);
        return output.Length > 100_000 ? output[..100_000] : output;
    }

    // User Input and a Yes/No Decision wait here for the person at the window.
    private string Ask(string nodeId, string kind, string message)
    {
        var question = new InputRequest($"{nodeId}-{step}", nodeId, kind, message);
        lock (inputGate)
        {
            request = question;
            reply = "";
            replied.Reset();
        }
        SetNode(nodeId, NodeState.Waiting);
        Event(kind == "decision" ? "Waiting for your Yes or No: " + message : "Waiting for your reply: " + message);
        try
        {
            while (!replied.Wait(250) && !ending.IsCancellationRequested) { }
            if (ending.IsCancellationRequested) throw new OperationCanceledException();
            string answer;
            lock (inputGate) answer = reply;
            Event(kind == "decision" ? $"You answered {answer.ToUpperInvariant()}" : $"You replied ({answer.Length.ToString("N0", CultureInfo.InvariantCulture)} characters)");
            return answer;
        }
        finally
        {
            lock (inputGate) request = null;
            SetNode(nodeId, NodeState.Running);
        }
    }

    private void Transcript(string label, string output)
    {
        string block = $"── {DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} · {label} ──\n{output.Replace("\r\n", "\n", StringComparison.Ordinal)}\n\n";
        lock (transcriptGate)
        {
            try { File.AppendAllText(transcript.FilePath, block, Utf8); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        transcript.Poll();
        Touch();
    }

    private static string S(JsonNode? node) => PromptFlowValidator.S(node);

    private static bool Flag(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.True;

    private static double Num(JsonNode? node)
        => node is JsonValue value && value.GetValueKind() == JsonValueKind.Number ? value.GetValue<double>() : 0;

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        ending.Cancel();
        resumed.Set();
        replied.Set();
    }
}
