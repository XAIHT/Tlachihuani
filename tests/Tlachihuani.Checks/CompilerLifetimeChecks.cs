// Tlachihuani — Angela López Mendoza · XAIHT
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tlachihuani.FlowPills;

internal static class CompilerLifetimeChecks
{
    private static string Self => Path.ChangeExtension(typeof(CompilerLifetimeChecks).Assembly.Location, ".exe");

    // Stand-in for Python: uses the real compiler's stdin/stdout contract. It never runs agents.
    internal static int Compiler()
    {
        var request = JsonNode.Parse(Console.In.ReadToEnd())!;
        var fixture = JsonNode.Parse(File.ReadAllText((string)request["flw"]!))!;
        string folder = (string)fixture["fixture"]!, mode = (string)fixture["mode"]!;
        if (mode == "hang")
        {
            using var descendant = Spawn("--compiler-descendant", Path.Combine(folder, "descendant.pid"));
            File.WriteAllText(Path.Combine(folder, "compiler.pid"), Environment.ProcessId.ToString());
            Thread.Sleep(TimeSpan.FromMinutes(2));
        }
        if (mode == "invalid") { Console.Write("not JSON"); return 1; }
        if (mode == "refused") { Console.Write("{\"ok\":false,\"code\":\"fixture_refused\",\"error\":\"Expected refusal\"}"); return 1; }
        Console.Error.Write(new string('x', 100_000)); // Exercises output larger than a pipe buffer.
        Console.Write("{\"ok\":true,\"nodes\":[],\"starters\":[],\"hypervisors\":[],\"warnings\":[]}");
        return 0;
    }

    internal static int Descendant(string marker)
    {
        File.WriteAllText(marker, Environment.ProcessId.ToString());
        Thread.Sleep(TimeSpan.FromMinutes(2));
        return 0;
    }

    internal static int Parent(string folder)
    {
        Environment.SetEnvironmentVariable("TLACHIHUANI_USER_DIR", Path.Combine(folder, "user-state"));
        using var run = Create(folder, "hang");
        Require(run.TryBegin(), "Parent fixture could not begin");
        Thread.Sleep(TimeSpan.FromMinutes(2));
        return 0;
    }

    internal static async Task Run(string root)
    {
        Directory.CreateDirectory(root);
        string? original = Environment.GetEnvironmentVariable("TLACHIHUANI_USER_DIR");
        Environment.SetEnvironmentVariable("TLACHIHUANI_USER_DIR", Path.Combine(root, "user-state"));
        try
        {
            foreach (string stop in new[] { "force", "graceful", "dispose" })
            {
                string folder = NewFolder(root, stop);
                using var run = Create(folder, "hang");
                Require(run.TryBegin(), "Run must enter Preparing");
                using var compiler = await Observe(folder, "compiler.pid");
                using var descendant = await Observe(folder, "descendant.pid");
                try
                {
                    if (stop == "dispose") run.Dispose(); else run.Stop(force: stop == "force");
                    Require(run.Settle(TimeSpan.FromSeconds(5)), $"{stop}: run did not settle promptly while compiling");
                    Require(run.State == RunState.Stopped, $"{stop}: cancellation must end as Stopped, not Failed");
                    Require(compiler.WaitForExit(2000) && descendant.WaitForExit(2000), $"{stop}: compiler or descendant survived");
                    Console.WriteLine($"PASS Flow Pills: {stop} ends compiler and descendant, settles as Stopped");
                }
                finally { End(compiler); End(descendant); }
            }
            foreach (string mode in new[] { "success", "invalid", "refused" })
            {
                string folder = NewFolder(root, mode);
                using var run = Create(folder, mode);
                Require(run.TryBegin() && run.Settle(TimeSpan.FromSeconds(10)), "Compiler result must settle");
                Require(run.State == (mode == "success" ? RunState.Completed : RunState.Failed), $"Incorrect compiler result: {mode}: {run.Status}");
                if (mode == "refused") Require(run.Status.Contains("Expected refusal", StringComparison.Ordinal), "Compiler refusal detail lost");
                Console.WriteLine($"PASS Flow Pills: compiler {mode} result and large stderr handled");
            }
            string parentFolder = NewFolder(root, "parent-exit");
            using var parent = Spawn("--compiler-parent", parentFolder);
            Process? child = null, grandchild = null;
            try
            {
                child = await Observe(parentFolder, "compiler.pid");
                grandchild = await Observe(parentFolder, "descendant.pid");
                parent.Kill(); // Only the owned parent; Windows must clean the job's descendants.
                Require(parent.WaitForExit(5000) && child.WaitForExit(5000) && grandchild.WaitForExit(5000),
                    "Compiler tree survived the parent's exit");
                Console.WriteLine("PASS Flow Pills: parent exit closes job and ends compiler tree");
            }
            finally
            {
                End(parent);
                if (child is not null) { End(child); child.Dispose(); }
                if (grandchild is not null) { End(grandchild); grandchild.Dispose(); }
            }
        }
        finally { Environment.SetEnvironmentVariable("TLACHIHUANI_USER_DIR", original); }
    }

    internal static void RealPython(string root, string python)
    {
        string folder = NewFolder(root, "real Python López");
        string agents = Path.Combine(folder, "agents"), starter = Path.Combine(agents, "starter");
        Directory.CreateDirectory(starter);
        string marker = Path.Combine(folder, "started.txt");
        File.WriteAllText(Path.Combine(starter, "starter.py"),
            "from pathlib import Path\nPath(" + JsonSerializer.Serialize(marker) + ").write_text('verified', encoding='utf-8')\n");
        File.WriteAllText(Path.Combine(starter, "config.yaml"), "target_agents: []\n");
        string? original = Environment.GetEnvironmentVariable("TLACHIHUANI_USER_DIR");
        Environment.SetEnvironmentVariable("TLACHIHUANI_USER_DIR", Path.Combine(folder, "user-state"));
        try
        {
            var doc = FlowDocument.Parse(Path.Combine(folder, "real.flw"), """{"nodes":[{"text":"Starter"}],"connections":[]}""");
            var install = new TlamatiniInstall(agents, folder, python, Path.Combine(folder, "config.json"), Path.Combine(folder, "Temp"), folder, 1, "isolated real Python compiler check");
            using var run = new AgentFlowRun(doc, install);
            try
            {
                Require(run.TryBegin() && run.Settle(TimeSpan.FromSeconds(25)), "Real Python compilation/run did not settle");
                Require(run.State == RunState.Completed && File.ReadAllText(marker) == "verified", "Real Python compiler/Starter failed: " + run.Status);
                Console.WriteLine("PASS Flow Pills: actual Python compiler and harmless Starter complete with spaced/Unicode paths");
            }
            finally { run.Stop(force: true); run.Settle(TimeSpan.FromSeconds(5)); }
        }
        finally { Environment.SetEnvironmentVariable("TLACHIHUANI_USER_DIR", original); }
    }

    private static AgentFlowRun Create(string folder, string mode)
    {
        var doc = new FlowDocument
        {
            FilePath = Path.Combine(folder, "fixture.flw"), Name = "Compiler lifetime fixture",
            Kind = FlowKind.Agents, Nodes = [], Edges = [],
            Text = new JsonObject { ["fixture"] = folder, ["mode"] = mode }.ToJsonString(),
        };
        var install = new TlamatiniInstall(folder, folder, Self, Path.Combine(folder, "config.json"), folder, folder, 0, "isolated test executable");
        return new AgentFlowRun(doc, install);
    }
    private static string NewFolder(string root, string name)
    {
        string folder = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
    private static Process Spawn(params string[] args)
    {
        var start = new ProcessStartInfo(Self) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start)!;
    }
    private static async Task<Process> Observe(string folder, string name)
    {
        string marker = Path.Combine(folder, name);
        var wait = Stopwatch.StartNew();
        while (wait.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (File.Exists(marker) && int.TryParse(File.ReadAllText(marker), out int pid))
            {
                var process = Process.GetProcessById(pid);
                Require(string.Equals(process.MainModule?.FileName, Self, StringComparison.OrdinalIgnoreCase), "Unexpected test child identity");
                _ = process.Handle; // Retain the exact process identity for all cleanup.
                return process;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException("Compiler did not publish " + marker);
    }
    private static void End(Process process)
    { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); } }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
