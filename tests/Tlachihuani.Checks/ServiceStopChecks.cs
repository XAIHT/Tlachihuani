// Tlachihuani — Angela López Mendoza · XAIHT
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Tlachihuani.Core;
using Tlachihuani.FlowPills;
using Tlachihuani.Tray;
using Tlachihuani.Windows;
using static DesktopChecks;

// Stop Services ends every Tlachihuani service; these checks prove how, without touching the
// user's installed services or their HandGI and Flow Pills: Windows services are fakes, and the
// real HandGI and Flow Pills run in a test folder of their own (TLACHIHUANI_USER_DIR), with their
// own names, records and pipes. HandGI runs with its camera off.
internal static class ServiceStopChecks
{
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(150), Denied = TimeSpan.FromMilliseconds(400);
    private const string Image = @"C:\Program Files\XAIHT\Tlachihuani\Jobs\Tlachihuani.Jobs.exe";

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static Task<StopOutcome> Stop(StopBackend backend, FakeProcess? process, string image = Image)
        => ForcedStop.ServiceAsync("Test service", backend, () => image, _ => process, Grace, Denied, CancellationToken.None);

    // ---- Windows services (fakes) -----------------------------------------------------------

    internal static async Task Graceful()
    {
        var process = new FakeProcess(Image, canTerminate: true);
        var backend = new StopBackend { OnStop = () => process.HasExited = true };
        var outcome = await Stop(backend, process);
        Require(outcome is { Result: "stopped", Down: true }, $"Expected stopped, got {outcome.Result}: {outcome.Message}");
        Require(process.Terminations == 0 && backend.Actions.SequenceEqual(["stop"]), "A service that stopped on its own was ended");
    }

    internal static async Task Ended()
    {
        foreach (bool refuses in new[] { false, true })
        {
            var process = new FakeProcess(Image, canTerminate: true);
            var backend = new StopBackend { Obeys = false, Refuses = refuses };
            var clock = Stopwatch.StartNew();
            var outcome = await Stop(backend, process);
            Require(outcome is { Result: "ended", Down: true }, $"Expected ended, got {outcome.Result}: {outcome.Message}");
            Require(process.Terminations == 1, "The process was not ended exactly once");
            Require(clock.Elapsed >= Grace, "The process was ended before its grace ran out");
            Require(backend.Actions.SequenceEqual(["stop"]), "The stop request was not sent first");
        }
    }

    internal static async Task Refusals()
    {
        // Installed before XT could end it: waited for, then named, with the way out.
        var old = new FakeProcess(Image, canTerminate: false);
        var outcome = await Stop(new StopBackend { Obeys = false }, old);
        Require(outcome is { Result: "end_denied", Down: false } && old.Terminations == 0, "An old installation was not reported as still running");
        Require(outcome.Message.Contains("installing Tlachihuani again", StringComparison.Ordinal), "The way out is not named: " + outcome.Message);
        // A process that is not this installation's executable is neither asked nor ended.
        var foreign = new FakeProcess(@"C:\Elsewhere\Tlachihuani.Jobs.exe", canTerminate: true);
        var guarded = new StopBackend();
        outcome = await Stop(guarded, foreign);
        Require(outcome.Result == "failed" && guarded.Actions.Count == 0 && foreign.Terminations == 0, "A foreign process was touched");
        // With no process to watch, Windows' own state decides, and a stuck service is named.
        outcome = await Stop(new StopBackend { Obeys = false }, null);
        Require(outcome.Result == "failed" && outcome.Message.Contains("no process", StringComparison.Ordinal), "A stuck service without a process was not reported");
        outcome = await Stop(new StopBackend(), null);
        Require(outcome.Result == "stopped", "A service that stopped without a visible process was not reported stopped");
    }

    internal static async Task AlreadyDown()
    {
        foreach (var (state, expected) in new[] { ("stopped", "already_stopped"), ("not_installed", "not_installed") })
        {
            var backend = new StopBackend { State = state };
            bool opened = false;
            var outcome = await ForcedStop.ServiceAsync("Test service", backend, () => Image, _ => { opened = true; return null; }, Grace, Denied, CancellationToken.None);
            Require(outcome.Result == expected && outcome.Down && !opened && backend.Actions.Count == 0, $"{state}: the service was touched");
        }
    }

    internal static async Task Guards()
    {
        // Windows restarts an ended service (its recovery setting): the guard stops it again, once.
        var restarted = new StopBackend { State = "stopped" };
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var guard = ForcedStop.GuardAsync("Test service", restarted, TimeSpan.FromSeconds(10), limit.Token);
        await Task.Delay(1200);
        restarted.State = "running";
        Require(await Task.WhenAny(guard, Task.Delay(TimeSpan.FromSeconds(8))) == guard, "The guard did not stop the restarted service");
        Require(restarted.State == "stopped" && restarted.Actions.SequenceEqual(["stop"]), "The restarted service was not stopped exactly once");
        // A start in XT cancels the guard: a service started on purpose keeps running.
        var wanted = new StopBackend { State = "stopped" };
        ForcedStop.GuardAgainstRestart("Test service", wanted);
        ForcedStop.CancelRestartGuards();
        wanted.State = "running";
        await Task.Delay(1600);
        Require(wanted.Actions.Count == 0 && wanted.State == "running", "A cancelled guard still stopped the service");
        var clock = Stopwatch.StartNew();
        ForcedStop.FinishGuards(TimeSpan.FromSeconds(5));
        Require(clock.Elapsed < TimeSpan.FromSeconds(3), "Closing XT waited for a cancelled guard");
    }

    // ---- processes of the user's session --------------------------------------------------------

    internal static Task Records()
    {
        string folder = TempFolder("records"), path = Path.Combine(folder, "service.json");
        using var child = Sleeper();
        try
        {
            using var identity = ProcessIdentity.Open(child.Id);
            string name = Path.GetFileName(identity.ImagePath);
            var record = new UserProcessRecord(child.Id, identity.CreationTime, identity.ImagePath, DateTimeOffset.Now, "7");
            record.Publish(path);
            var read = UserProcessRecord.Read(path);
            Require(read is not null && read.Pid == child.Id && read.Detail == "7", "The record did not read back");
            using (var verified = read!.OpenVerified(name)) Require(verified is not null, "The live process was not verified");
            using (var other = read.OpenVerified("Tlachihuani.HandGI.exe")) Require(other is null, "A process of another executable was accepted");
            using (var replaced = (read with { Created = read.Created + 1 }).OpenVerified(name))
                Require(replaced is null, "A record whose process was replaced (another start time) was accepted");
            using (var self = UserProcessRecord.ForThisProcess().OpenVerified(Path.GetFileName(Environment.ProcessPath!)))
                Require(self is null, "A process may never end itself through its own record");
            File.WriteAllText(path, "{ not json");
            Require(UserProcessRecord.Read(path) is null, "A damaged record was accepted");
            File.WriteAllText(path, new string(' ', 5000));
            Require(UserProcessRecord.Read(path) is null, "An oversized record was accepted");
            record.Publish(path);
            UserProcessRecord.Clear(path, child.Id + 1);
            Require(File.Exists(path), "Another process's record was removed");
            UserProcessRecord.Clear(path, child.Id);
            Require(!File.Exists(path), "The record was not removed");
        }
        finally { End(child); TryDelete(folder); }
        return Task.CompletedTask;
    }

    internal static async Task EndsRecordedProcess()
    {
        string folder = TempFolder("end"), path = Path.Combine(folder, "service.json");
        using var child = Sleeper();
        try
        {
            string name;
            using (var identity = ProcessIdentity.Open(child.Id))
            {
                name = Path.GetFileName(identity.ImagePath);
                new UserProcessRecord(child.Id, identity.CreationTime, identity.ImagePath, DateTimeOffset.Now).Publish(path);
            }
            var clock = Stopwatch.StartNew();
            var outcome = await ForcedStop.UserProcessAsync("Test pill", path, name, CancellationToken.None);
            Require(outcome is { Result: "ended", Down: true }, $"Expected ended, got {outcome.Result}: {outcome.Message}");
            Require(child.WaitForExit(5000) && clock.Elapsed < TimeSpan.FromSeconds(5), "The recorded process still runs");
            Require(!File.Exists(path), "The record of an ended process was left behind");
            outcome = await ForcedStop.UserProcessAsync("Test pill", path, name, CancellationToken.None);
            Require(outcome is { Result: "not_running", Down: true }, "A missing record was not reported as not running");
        }
        finally { End(child); TryDelete(folder); }
    }

    // ---- the real HandGI, in a test folder, camera off ---------------------------------------

    internal static async Task<int> HandGILifecycle(string executable)
    {
        int passed = 0;
        void Pass(string name) { Console.WriteLine("PASS " + name); passed++; }
        string folder = TempFolder("handgi");
        executable = Path.GetFullPath(executable);
        Environment.SetEnvironmentVariable("TLACHIHUANI_USER_DIR", folder);
        Environment.SetEnvironmentVariable("TLACHIHUANI_HANDGI_EXE", executable);
        try
        {
            Require(HandGIEndpoint.FindExecutable() == executable, "HandGI was not found where the test put it");
            Require(!HandGIEndpoint.IsRunning(), "A HandGI already runs in the test folder");
            Require((await HandGIControl.ObserveAsync(default)).Word == "stopped", "The panel does not see HandGI as stopped");
            Console.WriteLine("STEP HandGI starts as a service of this user, camera off");
            var status = await StartCameraOffAsync(executable);
            Require((string?)status["state"] == "camera_off" && (string?)status["hand_control"] == "paused", "Unexpected first status: " + status.ToJsonString());
            int pid;
            using (var process = HandGIEndpoint.OpenRunning())
            {
                Require(process is not null && Path.GetFileName(process.ImagePath) == HandGIEndpoint.ExecutableName, "The record does not name HandGI");
                pid = process!.Id;
            }
            var seen = await HandGIControl.ObserveAsync(default);
            Require(seen is { Running: true, Answering: true, Word: "camera_off" }, "The panel does not see the running HandGI");
            Pass("HandGI runs as a service of this user: one process, its record and a pipe only this user can open");

            var second = Launch(executable, "--service", "--camera-off");
            Require(second.WaitForExit(15000), "A second HandGI did not leave");
            Require(UserProcessRecord.Read(HandGIEndpoint.RecordPath)?.Pid == pid, "A second HandGI replaced the first");
            Pass("A second HandGI launch hands over to the first one and leaves");

            Console.WriteLine("STEP XT opens HandGI's Signs, Camera and Try it windows");
            foreach (string page in new[] { "signs", "camera", "try" })
            {
                await HandGIControl.ShowAsync(page, default);
                await Task.Delay(1200);
                Photo("handgi-" + page);
            }
            await Reject<ArgumentException>(() => HandGIEndpoint.CallAsync("show", default, "nowhere"));
            await Reject<ArgumentException>(() => HandGIEndpoint.CallAsync("format-disk", default));
            Pass("XT opens HandGI's windows by name; any other request is refused before it is sent");

            // HandGI lists the cameras when it turns one on. Started with the camera off, its Camera
            // window lists them itself, and listing never turns a camera on.
            var still = await HandGIEndpoint.CallAsync("status", default);
            Require((string?)still["state"] == "camera_off", "Opening HandGI's windows turned the camera on: " + still.ToJsonString());
            Require(ReadShared(Path.Combine(folder, "Tlachihuani.log")).Contains(" camera stream(s): ", StringComparison.Ordinal),
                "The Camera window did not list the cameras");
            Pass("The Camera window lists the cameras with the camera still off");

            Console.WriteLine("STEP Stop from the Services panel");
            using (var running = Process.GetProcessById(pid))
            {
                await HandGIControl.StopAsync(default);
                Require(running.WaitForExit(10000), "HandGI did not leave after Stop");
            }
            Require(!File.Exists(HandGIEndpoint.RecordPath), "HandGI left its record behind");
            Require((await HandGIControl.ObserveAsync(default)).Word == "stopped", "The panel still sees HandGI");
            Pass("Stop in the Services panel: HandGI closes its windows, removes its record and exits");

            Console.WriteLine("STEP Stop Services ends HandGI with no mercy");
            await StartCameraOffAsync(executable, "signs");
            await Task.Delay(1500);
            Photo("handgi-before-stop-services");
            pid = UserProcessRecord.Read(HandGIEndpoint.RecordPath)!.Pid;
            using (var victim = Process.GetProcessById(pid))
            {
                var clock = Stopwatch.StartNew();
                var outcome = await ForcedStop.UserProcessAsync("HandGI", HandGIEndpoint.RecordPath, HandGIEndpoint.ExecutableName, default);
                Require(outcome.Result == "ended" && victim.WaitForExit(5000), $"HandGI was not ended: {outcome.Message}");
                Require(clock.Elapsed < TimeSpan.FromSeconds(3), "Ending HandGI waited for it");
            }
            Require(!HandGIEndpoint.IsRunning(), "HandGI still appears to run");
            await Reject<LifecycleException>(() => HandGIEndpoint.CallAsync("status", default));
            await Task.Delay(600);
            Photo("handgi-after-stop-services");
            Pass("Stop Services ends HandGI at once, without asking, and nothing answers afterwards");

            string log = ReadShared(Path.Combine(folder, "Tlachihuani.log"));
            foreach (string line in new[] { "HandGI starts (version", "HandGI started with the camera off", "XT asks HandGI to show signs",
                "HandGI shows its camera window", "XT asks HandGI to stop", "HandGI stopped" })
                Require(log.Contains(line, StringComparison.Ordinal), $"Tlachihuani.log lacks \"{line}\"");
            Pass("Everything HandGI did is in Tlachihuani.log");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Console.Error.WriteLine($"RESULT: failed after {passed} checks."); return 1; }
        finally
        {
            // Whatever this test started in its own folder ends with it.
            try { await ForcedStop.UserProcessAsync("HandGI", HandGIEndpoint.RecordPath, HandGIEndpoint.ExecutableName, default); }
            catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException) { Console.WriteLine("CLEANUP " + ex.Message); }
            Environment.SetEnvironmentVariable("TLACHIHUANI_USER_DIR", null);
            Environment.SetEnvironmentVariable("TLACHIHUANI_HANDGI_EXE", null);
            TryDelete(folder);
        }
        Console.WriteLine($"RESULT: {passed} checks passed."); return 0;
    }

    // ---- the real Flow Pills window, in a test folder -----------------------------------------

    internal static async Task<int> FlowPillsLifecycle(string trayExecutable)
    {
        int passed = 0;
        void Pass(string name) { Console.WriteLine("PASS " + name); passed++; }
        string folder = TempFolder("flowpills");
        trayExecutable = Path.GetFullPath(trayExecutable);
        Environment.SetEnvironmentVariable("TLACHIHUANI_USER_DIR", folder);
        try
        {
            Require(!FlowPillsLauncher.Observe().Running, "A Flow Pills already runs in the test folder");
            Console.WriteLine("STEP Flow Pills opens in its own process");
            using (var window = Launch(trayExecutable, "--flow-pills"))
            {
                Require(await WaitAsync(() => FlowPillsLauncher.Observe().Running, TimeSpan.FromSeconds(30)), "Flow Pills did not publish its process");
                Require(FlowPillsLauncher.Observe().Runs == 0 && UserProcessRecord.Read(FlowPillsPaths.ProcessRecord)?.Pid == window.Id,
                    "The record does not name the Flow Pills window");
                Pass("Flow Pills publishes its process and how many flows run, for the Services panel");
                await Task.Delay(1500);
                Photo("flowpills-open");
                Console.WriteLine("STEP Stop from the Services panel");
                await FlowPillsLauncher.StopAsync(default);
                Require(window.WaitForExit(10000), "Flow Pills did not close after Stop");
            }
            Require(!File.Exists(FlowPillsPaths.ProcessRecord), "Flow Pills left its record behind");
            await Task.Delay(600);
            Photo("flowpills-after-panel-stop");
            Pass("Stop in the Services panel stops every flow, closes Flow Pills and removes its record");

            Console.WriteLine("STEP Stop Services ends Flow Pills with no mercy");
            using (var again = Launch(trayExecutable, "--flow-pills"))
            {
                Require(await WaitAsync(() => FlowPillsLauncher.Observe().Running, TimeSpan.FromSeconds(30)), "Flow Pills did not open again");
                await Task.Delay(1500);
                Photo("flowpills-open-again");
                var clock = Stopwatch.StartNew();
                var outcome = await ForcedStop.UserProcessAsync("Flow Pills", FlowPillsPaths.ProcessRecord, FlowPillsSignals.ExecutableName, default);
                Require(outcome.Result == "ended" && again.WaitForExit(5000) && clock.Elapsed < TimeSpan.FromSeconds(3), $"Flow Pills was not ended: {outcome.Message}");
            }
            Require(!FlowPillsLauncher.Observe().Running, "Flow Pills still appears to run");
            await Task.Delay(600);
            Photo("flowpills-after-stop-services");
            Pass("Stop Services ends Flow Pills at once, without asking");

            string log = ReadShared(Path.Combine(folder, "Tlachihuani.log"));
            foreach (string line in new[] { "Flow Pills opens (version", "Flow Pills was asked to stop by the Services panel" })
                Require(log.Contains(line, StringComparison.Ordinal), $"Tlachihuani.log lacks \"{line}\"");
            Pass("What Flow Pills did is in Tlachihuani.log");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Console.Error.WriteLine($"RESULT: failed after {passed} checks."); return 1; }
        finally
        {
            try { await ForcedStop.UserProcessAsync("Flow Pills", FlowPillsPaths.ProcessRecord, FlowPillsSignals.ExecutableName, default); }
            catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException) { Console.WriteLine("CLEANUP " + ex.Message); }
            Environment.SetEnvironmentVariable("TLACHIHUANI_USER_DIR", null);
            TryDelete(folder);
        }
        Console.WriteLine($"RESULT: {passed} checks passed."); return 0;
    }

    // ---- helpers -----------------------------------------------------------------------------------

    // Starts HandGI the way XT does (HandGIEndpoint.Launch), but with its camera off, then waits
    // until its pipe answers.
    private static async Task<JsonObject> StartCameraOffAsync(string executable, string? page = null)
    {
        using var launched = page is null ? Launch(executable, "--service", "--camera-off") : Launch(executable, "--service", "--camera-off", "--show", page);
        var clock = Stopwatch.StartNew();
        while (true)
        {
            try { return await HandGIEndpoint.CallAsync("status", default); }
            catch (Exception ex) when ((ex is LifecycleException or TimeoutException or IOException or Win32Exception) && clock.Elapsed < TimeSpan.FromSeconds(20))
            { await Task.Delay(250); }
        }
    }

    private static async Task Reject<T>(Func<Task> body) where T : Exception
    {
        try { await body(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }

    private static async Task<bool> WaitAsync(Func<bool> condition, TimeSpan limit)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < limit)
        {
            if (condition()) return true;
            await Task.Delay(200);
        }
        return condition();
    }

    private static Process Launch(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Windows did not start " + executable);
    }

    // The child these checks start and end: this executable, waiting quietly.
    private static Process Sleeper()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--flow-pills-sleeper");
        return Process.Start(start) ?? throw new InvalidOperationException("The sleeper did not start.");
    }

    // A check ends only the child it started, through the handle it owns.
    private static void End(Process child)
    {
        if (child.HasExited) return;
        child.Kill();
        child.WaitForExit(5000);
    }

    private static string TempFolder(string name)
    {
        string folder = Path.Combine(Path.GetTempPath(), "tlachihuani-checks", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void TryDelete(string folder)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Thread.Sleep(300); }
        }
        Console.WriteLine("CLEANUP could not remove " + folder);
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

// A Windows service as Stop Services sees it.
internal sealed class StopBackend : IServiceBackend
{
    public string State = "running";
    public bool Obeys = true, Refuses;
    public Action? OnStop;
    public List<string> Actions { get; } = [];
    public NativeStatus Query() => new(State, State == "running" ? 4242 : 0);
    public void Start() { Actions.Add("start"); State = "running"; }
    public void Stop()
    {
        Actions.Add("stop");
        if (Refuses) throw new Win32Exception(1061, "The service cannot accept control messages at this time.");
        OnStop?.Invoke();
        if (Obeys) State = "stopped";
    }
}

// The process Windows names for a service.
internal sealed class FakeProcess(string image, bool canTerminate) : IEndableProcess
{
    public string ImagePath { get; } = image;
    public bool HasExited { get; set; }
    public bool CanTerminate { get; } = canTerminate;
    public int Terminations { get; private set; }
    public void Terminate(uint exitCode) { Terminations++; HasExited = true; }
    public void Dispose() { }
}
