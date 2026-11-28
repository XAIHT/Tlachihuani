// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (one supervising thread per run, ticking twice a second; every start, end and change is written explicitly)
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tlachihuani.Contracts;

namespace Tlachihuani.FlowPills;

// A .flw run, the way Tlamatini's Agentic Control Panel runs one. The flow is compiled into a
// pool of its own with Tlamatini's wiring rules (workers\flowpills\flowpills_compile.py); every
// Starter is launched, and FlowHypervisor when the flow has one; from then on the agents start
// one another exactly as they do under Tlamatini. The run's own thread watches its job: which
// agents are alive, every agent's log, FlowHypervisor's alerts, and the moment no agent is left.
// A flow that never ends keeps running, as it does in Tlamatini.
//
//   Pause   records the running agents in paused_agents.reanim and ends them; Resume starts
//           them again with AGENT_REANIMATED=1, so they keep their logs and state (Tlamatini's
//           own pause). Stop runs the flow's Ender, as Tlamatini's Stop button does, then ends
//           whatever is left of the run. A second Stop ends everything at once.
public sealed class AgentFlowRun : FlowRun
{
    // Compiling is a short Python process; several at once must still leave the window a core.
    private static readonly SemaphoreSlim Compiles = new(Math.Clamp(Environment.ProcessorCount - 1, 1, 4));
    private static readonly UTF8Encoding Utf8 = new(false);
    private static int supervised;
    private readonly RunJob job = new();
    private readonly Dictionary<string, Agent> agents = new(StringComparer.Ordinal);
    private readonly HashSet<string> paused = new(StringComparer.Ordinal);
    private readonly AutoResetEvent wake = new(false);
    private readonly object commandGate = new();
    private Command pending = Command.None;
    private IReadOnlyList<string> starters = [], hypervisors = [];
    private string pool = "";
    private volatile string alert = "";

    private enum Command { None, Pause, Resume, Stop, ForceStop }

    private sealed class Agent(string nodeId, string poolName, string type, string home, string script, LogTail tail)
    {
        public string NodeId { get; } = nodeId;
        public string PoolName { get; } = poolName;
        public string Type { get; } = type;
        public string Home { get; } = home;
        public string Script { get; } = script;
        public LogTail Tail { get; } = tail;
        public bool IsHypervisor => Type == "flowhypervisor";
        public int Pid { get; set; }
        public bool Seen { get; set; }
        public bool Launched { get; set; }
        public bool WasRunning { get; set; }
    }

    private sealed record CompiledNode(string Id, string Pool, string Type, string Home, string Script);
    private sealed record Compiled(IReadOnlyList<CompiledNode> Nodes, IReadOnlyList<string> Starters,
        IReadOnlyList<string> Hypervisors, IReadOnlyList<string> Warnings);

    public AgentFlowRun(FlowDocument document, TlamatiniInstall install) : base(document, install) { }

    public override bool CanPause => State == RunState.Running;
    public override bool CanResume => State == RunState.Paused;
    public override ulong PeakMemory => job.PeakMemory();
    public string PoolFolder => pool;
    public string Alert => alert;

    public override void Pause() => Ask(Command.Pause);
    public override void Resume() => Ask(Command.Resume);
    protected override void OnStop(bool force)
    {
        Ask(force ? Command.ForceStop : Command.Stop);
        // Preparation has no Ender to run. Ending its owned compiler also releases a
        // blocked pipe write; forced stopping applies to every phase of this run.
        if (force || State == RunState.Preparing) job.TerminateAll();
    }

    private void Ask(Command command)
    {
        lock (commandGate)
        {
            if (pending == Command.ForceStop || (pending == Command.Stop && command != Command.ForceStop)) return;
            pending = command;
        }
        wake.Set();
    }

    private Command Take()
    {
        lock (commandGate)
        {
            var command = pending;
            pending = Command.None;
            return command;
        }
    }

    protected override void Execute()
    {
        Interlocked.Increment(ref supervised);
        try
        {
            Prepare();
            if (StopRequested)
            {
                Finish(RunState.Stopped, "Stopped before any agent started");
                return;
            }
            Start();
            Supervise();
        }
        finally { Interlocked.Decrement(ref supervised); }
    }

    private void Prepare()
    {
        SetState(RunState.Preparing, "Compiling the flow with Tlamatini's agents…");
        pool = Path.Combine(FlowPillsPaths.Pools, Id);
        string copy = Path.Combine(Folder, "flow.flw");
        File.WriteAllText(copy, Document.Text, Utf8);
        var compiled = Compile(copy);
        string root = Path.GetFullPath(pool) + Path.DirectorySeparatorChar;
        foreach (var node in compiled.Nodes)
        {
            string home = Path.GetFullPath(node.Home), script = Path.GetFullPath(node.Script);
            if (!home.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !script.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new FlowPillsException("compile_failed", $"The compiler placed {node.Pool} outside this run's pool ({pool}); nothing was started.");
            var tail = new LogTail(node.Pool, Path.Combine(home, Path.GetFileName(home) + ".log"));
            agents[node.Pool] = new Agent(node.Id, node.Pool, node.Type, home, script, tail);
            AddLog(tail);
        }
        starters = compiled.Starters;
        hypervisors = compiled.Hypervisors;
        foreach (var warning in compiled.Warnings) Event("Note: " + warning);
        Event($"Compiled {agents.Count} agents into {pool}");
    }

    private Compiled Compile(string flow)
    {
        string worker = FlowPillsPaths.CompileWorker;
        if (!File.Exists(worker)) throw new FlowPillsException("worker_missing", $"{worker} is missing. Repair or reinstall Tlachihuani.");
        while (!Compiles.Wait(250))
            if (StopRequested) throw new OperationCanceledException();
        try
        {
            if (StopRequested) throw new OperationCanceledException();
            using var inputPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            using var outputPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            using var errorPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            using var outputReader = new StreamReader(outputPipe, Utf8);
            using var errorReader = new StreamReader(errorPipe, Utf8);
            Directory.CreateDirectory(Install.TempFolder);
            using var process = job.LaunchCaptured(Install.Python, ["-I", "-B", worker], FlowPillsPaths.Root,
                new Dictionary<string, string> { ["TEMP"] = Install.TempFolder, ["TMP"] = Install.TempFolder, ["TLAMATINI_TEMP"] = Install.TempFolder },
                inputPipe.ClientSafePipeHandle, outputPipe.ClientSafePipeHandle, errorPipe.ClientSafePipeHandle);
            try
            {
                inputPipe.DisposeLocalCopyOfClientHandle();
                outputPipe.DisposeLocalCopyOfClientHandle();
                errorPipe.DisposeLocalCopyOfClientHandle();
                var output = outputReader.ReadToEndAsync();
                var errors = errorReader.ReadToEndAsync();
                if (StopRequested) throw new OperationCanceledException();
                using (var writer = new StreamWriter(inputPipe, Utf8))
                    writer.Write(new JsonObject { ["flw"] = flow, ["agents_root"] = Install.AgentsRoot, ["pool"] = pool }.ToJsonString());
                var wait = Stopwatch.StartNew();
                while (!process.WaitForExit(100))
                {
                    if (StopRequested) throw new OperationCanceledException();
                    if (wait.Elapsed >= TimeSpan.FromMinutes(2))
                        throw new FlowPillsException("compile_timeout", "Compiling the flow took more than two minutes; nothing was started.");
                }
                // A compiler must not leave children holding its output pipes open.
                job.TerminateAll();
                if (StopRequested) throw new OperationCanceledException();
                return ParseCompiled(output.GetAwaiter().GetResult(), errors.GetAwaiter().GetResult());
            }
            finally
            {
                job.TerminateAll();
                process.WaitForExit(3000);
            }
        }
        catch (Exception ex) when (StopRequested && ex is IOException or ObjectDisposedException or System.ComponentModel.Win32Exception)
        { throw new OperationCanceledException("Compilation stopped", ex); }
        finally { Compiles.Release(); }
    }

    private static Compiled ParseCompiled(string text, string problems)
    {
        JsonObject? reply = null;
        try { reply = JsonNode.Parse(text.Trim()) as JsonObject; }
        catch (JsonException) { }
        if (reply is null)
        {
            string detail = (problems.Trim().Length > 0 ? problems : text).Trim();
            if (detail.Length > 1500) detail = detail[^1500..];
            throw new FlowPillsException("compile_failed", "Tlamatini's flow compiler did not answer." + (detail.Length > 0 ? "\n" + detail : ""));
        }
        if (reply["ok"] is not JsonValue ok || ok.GetValueKind() != JsonValueKind.True)
            throw new FlowPillsException(Str(reply["code"]) is { Length: > 0 } code ? code : "compile_failed",
                Str(reply["error"]) is { Length: > 0 } error ? error : "Tlamatini's flow compiler refused the flow.");
        var nodes = new List<CompiledNode>();
        if (reply["nodes"] is JsonArray array)
            foreach (var item in array)
                if (item is JsonObject node)
                    nodes.Add(new CompiledNode(Str(node["id"]), Str(node["pool_name"]), Str(node["agent_type"]), Str(node["dir"]), Str(node["script"])));
        return new Compiled(nodes, Strings(reply["starters"]), Strings(reply["hypervisors"]), Strings(reply["warnings"]));
    }

    private void Start()
    {
        Directory.CreateDirectory(Install.TempFolder);
        SetState(RunState.Running, $"Running ({agents.Count} agents)");
        foreach (var name in starters) Launch(agents[name], reanimated: false);
        foreach (var name in hypervisors) Launch(agents[name], reanimated: false);
    }

    // The environment Tlamatini gives its agents, plus the run's id.
    private void Launch(Agent agent, bool reanimated)
    {
        string home = Install.PythonHome;
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TLAMATINI_AGENTS_ROOT"] = Install.AgentsRoot,
            ["CONFIG_PATH"] = Install.ConfigPath,
            ["PYTHON_HOME"] = home,
            ["PATH"] = home + ";" + Path.Combine(home, "Scripts") + ";" + (Environment.GetEnvironmentVariable("PATH") ?? ""),
            ["TLAMATINI_TEMP"] = Install.TempFolder,
            ["TEMP"] = Install.TempFolder,
            ["TMP"] = Install.TempFolder,
            ["TLAMATINI_TEMPLATES"] = Install.TemplatesFolder,
            ["PYTHONIOENCODING"] = "utf-8",
            ["AGENT_REANIMATED"] = reanimated ? "1" : "",
            ["TLACHIHUANI_FLOW_RUN"] = Id,
        };
        int pid = job.Launch(Install.Python, [agent.Script], agent.Home, environment);
        try { File.WriteAllText(Path.Combine(agent.Home, "agent.pid"), pid.ToString(CultureInfo.InvariantCulture)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        agent.Pid = pid;
        agent.Seen = agent.Launched = true;
        Event($"{(reanimated ? "Resumed" : "Started")} {agent.PoolName} (process {pid})");
    }

    private void Supervise()
    {
        int quiet = 0;
        while (true)
        {
            switch (Take())
            {
                case Command.ForceStop:
                    EndNow("Stopped");
                    return;
                case Command.Stop:
                    StopGracefully();
                    return;
                case Command.Pause when State == RunState.Running:
                    PauseNow();
                    break;
                case Command.Resume when State == RunState.Paused:
                    ResumeNow();
                    break;
            }
            var look = Look();
            if (State == RunState.Running && look.Agents is int busy)
            {
                quiet = busy == 0 ? quiet + 1 : 0;
                if (quiet >= 3)
                {
                    Complete(look.Hypervisors);
                    return;
                }
            }
            wake.WaitOne(Interval());
        }
    }

    // Every agent has finished: FlowHypervisor is ended, and anything the flow started on
    // purpose (an application, a server) keeps running, as it would under Tlamatini.
    private void Complete(List<int> hypervisorProcesses)
    {
        foreach (int process in hypervisorProcesses) job.Terminate(process);
        Thread.Sleep(300);
        Look();
        int left = job.ProcessIds()?.Length ?? 0;
        if (left > 0)
        {
            job.Release();
            Event($"{left} process(es) the flow started keep running, as they would under Tlamatini.");
        }
        Finish(RunState.Completed, "Completed: every agent has finished");
    }

    // One look at the job: who is alive, what every log says, what FlowHypervisor reports.
    // Agents is null when Windows could not list the job's processes (never read as "none").
    private (int? Agents, List<int> Hypervisors) Look()
    {
        int[]? members = job.ProcessIds();
        HashSet<int>? alive = members is null ? null : new HashSet<int>(members);
        var hyper = new List<int>();
        int busy = 0;
        foreach (var agent in agents.Values)
        {
            agent.Tail.Poll();
            bool ran = agent.Seen;
            if (!agent.Seen && agent.Tail.Exists) agent.Seen = true;
            if (alive is null) continue;
            int pid = ReadPid(agent);
            bool running = pid > 0 && alive.Contains(pid);
            if (running && agent.IsHypervisor) hyper.Add(pid);
            else if (running) busy++;
            if (running && !agent.WasRunning && !agent.Launched) Event($"{agent.PoolName} started (process {pid})");
            else if (!running && (agent.WasRunning || agent.Launched)) Event($"{agent.PoolName} finished");
            else if (!running && !ran && agent.Seen) Event($"{agent.PoolName} ran");
            agent.Launched = false;
            agent.WasRunning = running;
            SetNode(agent.NodeId, running ? NodeState.Running
                : paused.Contains(agent.PoolName) ? NodeState.Paused
                : agent.Seen ? NodeState.Done : NodeState.Idle);
        }
        ReadAlerts();
        return (alive is null ? null : busy, hyper);
    }

    // Tlamatini's own pause: remember who was running, then end the run's processes.
    private void PauseNow()
    {
        int[]? members = job.ProcessIds();
        if (members is null)
        {
            Event("Windows could not list this run's processes, so it was not paused.");
            return;
        }
        var alive = new HashSet<int>(members);
        paused.Clear();
        foreach (var agent in agents.Values)
            if (!agent.IsHypervisor && ReadPid(agent) is var pid && pid > 0 && alive.Contains(pid)) paused.Add(agent.PoolName);
        WritePaused();
        job.TerminateAll();
        WaitForEmptyJob();
        foreach (var agent in agents.Values) agent.WasRunning = agent.Launched = false;
        SetState(RunState.Paused, paused.Count == 0 ? "Paused (no agent was running)" : $"Paused: {paused.Count} agent(s) will resume where they were");
    }

    private void ResumeNow()
    {
        var names = paused.Order(StringComparer.Ordinal).ToList();
        paused.Clear();
        SetState(RunState.Running, names.Count == 0 ? "Resumed (no agent was paused)" : $"Resumed {names.Count} agent(s)");
        foreach (var name in names) if (agents.TryGetValue(name, out var agent)) Launch(agent, reanimated: true);
        foreach (var name in hypervisors) if (agents.TryGetValue(name, out var agent)) Launch(agent, reanimated: true);
        try { File.Delete(Path.Combine(pool, "paused_agents.reanim")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // Tlamatini's Stop button runs the flow's Ender(s): they end the agents and start the
    // Cleaners. After them (at most 20 seconds), whatever is left of the run is ended.
    private void StopGracefully()
    {
        var enders = agents.Values.Where(agent => agent.Type == "ender").ToList();
        bool viaEnder = State == RunState.Running && enders.Count > 0;
        if (viaEnder)
        {
            SetState(RunState.Stopping, "Stopping: the flow's Ender is ending its agents…");
            foreach (var ender in enders) Launch(ender, reanimated: false);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                if (Take() == Command.ForceStop) break;
                if (Look().Agents == 0) break;
                wake.WaitOne(500);
            }
        }
        EndNow(viaEnder ? "Stopped by the flow's Ender" : "Stopped");
    }

    private void EndNow(string status)
    {
        job.TerminateAll();
        WaitForEmptyJob();
        paused.Clear();
        foreach (var agent in agents.Values) agent.WasRunning = agent.Launched = false;
        Look();
        Finish(RunState.Stopped, status);
    }

    private void WaitForEmptyJob()
    {
        for (int i = 0; i < 30 && job.ProcessIds() is { Length: > 0 }; i++) Thread.Sleep(100);
    }

    // paused_agents.reanim, in the YAML Tlamatini writes for its own pause.
    private void WritePaused()
    {
        var text = new StringBuilder("paused_agents:");
        if (paused.Count == 0) text.Append(" []\n");
        else
        {
            text.Append('\n');
            foreach (var name in paused.Order(StringComparer.Ordinal)) text.Append("- ").Append(name).Append('\n');
        }
        try { File.WriteAllText(Path.Combine(pool, "paused_agents.reanim"), text.ToString(), Utf8); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // FlowHypervisor writes hypervisor_alert.json beside itself; Tlamatini reads it once and
    // removes it, and so does this run.
    private void ReadAlerts()
    {
        foreach (var name in hypervisors)
        {
            if (!agents.TryGetValue(name, out var agent)) continue;
            string path = Path.Combine(agent.Home, "hypervisor_alert.json");
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) continue;
                string text = info.Length > 64 * 1024 ? "" : File.ReadAllText(path);
                File.Delete(path);
                string message = "";
                try { message = Str((JsonNode.Parse(text) as JsonObject)?["message"]); }
                catch (JsonException) { message = text.Trim(); }
                if (message.Length == 0) continue;
                alert = message;
                Event("FlowHypervisor: " + message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static int ReadPid(Agent agent)
    {
        string path = Path.Combine(agent.Home, "agent.pid");
        try
        {
            if (!File.Exists(path)) return agent.Pid = 0;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Utf8);
            if (int.TryParse(reader.ReadToEnd().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid)) agent.Pid = pid;
            return agent.Pid;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return agent.Pid; }
    }

    // Twice a second; slower when very many flows run at once, so 256 of them stay light.
    private static int Interval() => Math.Clamp(250 + Volatile.Read(ref supervised) * 6, 500, 2000);

    private static string Str(JsonNode? node)
        => node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : "";

    private static List<string> Strings(JsonNode? node)
    {
        var list = new List<string>();
        if (node is JsonArray array) foreach (var item in array) if (Str(item) is { Length: > 0 } text) list.Add(text);
        return list;
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        Stop(force: true);
        job.Dispose();
    }
}
