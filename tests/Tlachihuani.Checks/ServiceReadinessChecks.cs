// Tlachihuani — Angela López Mendoza · XAIHT
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using Microsoft.Extensions.Logging.Abstractions;
using Tlachihuani.Contracts;
using Tlachihuani.Core;
using Tlachihuani.Jobs;
using Tlachihuani.Tray;

internal static class ServiceReadinessChecks
{
    private static int passed;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Check(string name, Func<Task> body) { await body(); passed++; Console.WriteLine("PASS " + name); }
    private static JsonObject Provider() => new() { ["enabled"] = true, ["engine"] = true, ["web"] = true, ["port"] = 12064 };
    private static readonly NativeStatus Running = new("running", 12345);
    private static readonly ControlReply Main = new(1, Guid.NewGuid(), true, "status", "observed", DateTimeOffset.Now, "fixture", Guid.NewGuid(),
        new("Tlachihuani", "running", "healthy", 123, Guid.NewGuid(), "0.39.0", 10, DateTimeOffset.Now));
    private static ServiceSnapshot Snapshot(JobsState? jobs = null) => new(Main, jobs ?? new("running", true), new("running", true, true));
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private static HttpClient Client(string content = "{\"api_version\":1,\"engine\":\"ready\"}", HttpStatusCode code = HttpStatusCode.OK) =>
        new(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(content) })));
    private static Task<JobsState> Probe(HttpClient http, Func<JsonObject, CancellationToken, Task<JsonObject>>? call = null) =>
        ServiceProbes.JobsAsync(() => Running, call ?? ((_, _) => Task.FromResult(Provider())), http, default);

    internal static int Run(string directory)
    {
        int exit = 1;
        var thread = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                Checks(directory).GetAwaiter().GetResult();
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var failure = new TimeoutException("Tlachihuani.Jobs: provider status timed out after 3 seconds. Startup readiness is unconfirmed.",
                    new IOException("Synthetic diagnostic evidence; no installed service was changed."));
                var report = ServiceErrorReport.Create("Start Services (verification fixture)", failure,
                    Snapshot(new("running", false, 12345, 0, [new("Jobs status", false, DateTimeOffset.Now, 3000,
                        "Authenticated pipe reply timed out; HTTP health was not attempted.", failure.ToString())])), DateTimeOffset.Now.AddSeconds(-40));
                using var form = new ServiceErrorForm(null, report);
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        var wait = Stopwatch.StartNew();
                        while (GetForegroundWindow() != form.Handle && wait.Elapsed < TimeSpan.FromSeconds(45)) await Task.Delay(100);
                        Require(form.Visible && GetForegroundWindow() == form.Handle && !IsIconic(form.Handle), "Error report must be visibly foreground before UI verification.");
                        await Task.Delay(300);
                        Require(form.Details.ReadOnly && form.Details.Text.Contains("Original failure:") && form.Details.Text.Contains("HTTP health was not attempted"), "Report lost details or is editable.");
                        Require(form.CopyReport.Visible && form.CopyReport.Parent!.ClientRectangle.Contains(form.CopyReport.Bounds), "Copy report is clipped.");
                        var clipboard = Clipboard.GetDataObject();
                        try
                        {
                            form.CopyReport.PerformClick();
                            Require(Clipboard.GetText() == report.Text && form.CopyStatus == "Report copied", "Copy report did not copy the entire report.");
                        }
                        finally { if (clipboard is not null) Clipboard.SetDataObject(clipboard, true); else Clipboard.Clear(); }
                        Require(File.ReadAllText(Path.Combine(TlachihuaniLog.Folder, "Last-service-error.txt")) == report.Text, "Saved report differs from copied report.");
                        await Task.Delay(150); // Let the copied confirmation paint before capture.
                        using var image = new Bitmap(form.Width, form.Height);
                        using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(form.Location, Point.Empty, form.Size);
                        image.Save(Path.Combine(directory, "service-error-report.png"));
                        passed++; Console.WriteLine("PASS Visible report layout, full copy, and persistent report");
                        await Task.Delay(1800);
                        exit = 0;
                    }
                    catch (Exception ex) { Console.WriteLine("FAIL " + ex); }
                    finally { form.Close(); }
                };
                Application.Run(form);
                Console.WriteLine($"RESULT: {passed} service readiness checks passed; exit={exit}");
            }
            catch (Exception ex) { Console.WriteLine("FAIL " + ex); File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString()); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); return exit;
    }

    private static async Task Checks(string directory)
    {
        await Check("Jobs readiness bypasses a busy lifecycle and leaves normal status behavior intact", async () =>
        {
            var provider = new TorrenterProvider(new("fixture", "fixture", directory, 12064, true), NullLogger.Instance);
            var gate = (SemaphoreSlim)typeof(TorrenterProvider).GetField("lifecycle", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(provider)!;
            await gate.WaitAsync();
            Task<JsonObject>? normal = null;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                normal = provider.CommandAsync(new() { ["op"] = "status" }, "fixture", deadline.Token);
                Require(!normal.IsCompleted, "Normal status bypassed the lifecycle unexpectedly.");
                var fast = provider.CommandAsync(new() { ["op"] = "status", ["include_impact"] = false }, "fixture", deadline.Token);
                var reply = await fast.WaitAsync(TimeSpan.FromSeconds(1));
                Require(!reply.ContainsKey("impact") && (bool?)reply["engine"] == false && (int?)reply["port"] == 12064,
                    "Readiness included statistics or fabricated engine health.");
            }
            finally { gate.Release(); if (normal is not null) await normal; await provider.Halt(); }
        });
        await Check("Readiness uses the lightweight authenticated request and checks actual HTTP health", async () =>
        {
            using var http = Client(); bool called = false;
            var result = await Probe(http, (request, _) => { called = (string?)request["op"] == "status" && (bool?)request["include_impact"] == false; return Task.FromResult(Provider()); });
            Require(called && result.ApiReady && result.Probes?.Count == 3 && result.Pid == 12345, "Ready observation lost checks or PID.");
        });
        await Check("Non-running SCM records native exit status without sending requests", async () =>
        {
            using var http = Client(); int calls = 0;
            var result = await ServiceProbes.JobsAsync(() => new("stopped", 0, 1067), (_, _) => { calls++; return Task.FromResult(Provider()); }, http, default);
            Require(!result.ApiReady && result.NativeExitCode == 1067 && calls == 0, "Stopped service was contacted or native code lost.");
        });
        await Check("Missing provider fields cannot pass readiness", async () =>
        {
            using var http = Client();
            foreach (string field in new[] { "enabled", "engine", "web", "port" })
            {
                var result = await Probe(http, (_, _) => { var value = Provider(); value.Remove(field); return Task.FromResult(value); });
                Require(!result.ApiReady && result.Probes!.Last().Details.Contains("Required:"), "Missing field accepted: " + field);
            }
        });
        await Check("Slow pipe and slow HTTP each get their own bounded deadline", async () =>
        {
            using var http = new HttpClient(new Handler(async (_, t) => { await Task.Delay(170, t); return new(HttpStatusCode.OK) { Content = new StringContent("{\"api_version\":1,\"engine\":\"ready\"}") }; }));
            var result = await ServiceProbes.JobsAsync(() => Running, async (_, t) => { await Task.Delay(170, t); return Provider(); }, http, default, TimeSpan.FromMilliseconds(300));
            Require(result.ApiReady, "HTTP inherited time already spent on the pipe.");
        });
        await Check("Pipe timeout preserves its stage and exception instead of returning an unexplained false", async () =>
        {
            using var http = Client();
            var result = await ServiceProbes.JobsAsync(() => Running, async (_, t) => { await Task.Delay(Timeout.Infinite, t); return Provider(); }, http, default, TimeSpan.FromMilliseconds(80));
            Require(!result.ApiReady && result.Probes!.Last() is { Stage: "Tlachihuani.Jobs / authenticated provider status", Passed: false, ExceptionDetails: not null }
                && result.Probes!.Last().Details.Contains("timed out"), "Timeout diagnostics lost.");
        });
        await Check("HTTP failure, malformed JSON, wrong version and oversized bodies retain diagnostics", async () =>
        {
            foreach (var (body, code) in new[] { ("backend unavailable", HttpStatusCode.ServiceUnavailable), ("broken", HttpStatusCode.OK),
                ("{\"api_version\":2,\"engine\":\"ready\"}", HttpStatusCode.OK), (new string('a', 5000), HttpStatusCode.OK) })
            {
                using var http = Client(body, code); var result = await Probe(http);
                Require(!result.ApiReady && result.Probes!.Last().Details.Contains("HTTP " + (int)code), "HTTP failure was accepted or lost its status.");
            }
        });
        await Check("Caller cancellation propagates rather than becoming a health failure", async () =>
        {
            using var stop = new CancellationTokenSource(); stop.Cancel(); using var http = Client();
            try { await ServiceProbes.JobsAsync(() => Running, (_, _) => Task.FromResult(Provider()), http, stop.Token); throw new InvalidOperationException("Cancellation swallowed."); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
        await Check("Installed failure sequence recovers from one timed-out Jobs probe without a second verdict", async () =>
        {
            int attempts = 0;
            var result = await ServiceReadiness.WaitAsync(_ => Task.FromResult(++attempts == 1 ? Snapshot(new("running", false)) : Snapshot()),
                TimeSpan.FromSeconds(2), default, TimeSpan.FromMilliseconds(10));
            Require(result.Ready && attempts == 2, "Transient failure was final or success triggered another observation.");
        });
        await Check("Persistent failure stops at its deadline and preserves bounded observation history", async () =>
        {
            try
            {
                await ServiceReadiness.WaitAsync(_ => Task.FromResult(Snapshot(new("running", false, Probes: [new("HTTP", false, DateTimeOffset.Now, 2, "HTTP 503")]))),
                    TimeSpan.FromMilliseconds(180), default, TimeSpan.FromMilliseconds(5));
                throw new InvalidOperationException("Permanent failure passed.");
            }
            catch (ServiceReadinessException ex)
            {
                Require(ex.Attempts.Count is > 0 and <= 8 && ex.Message.Contains("HTTP 503") && ex.Snapshot is { Ready: false }, "Timeout discarded evidence or grew without bound.");
            }
        });
        await Check("Optional RC is allowed off; broken configuration and unhealthy main service are not ready", async () =>
        {
            Require((Snapshot() with { RemoteController = new("stopped") }).Ready, "Optional stopped RC blocked startup.");
            Require(!(Snapshot() with { Main = Main with { Ok = false, Error = new("timeout", "No heartbeat") } }).Ready, "Unconfirmed main service passed.");
            var rc = await ServiceProbes.RemoteAsync(() => new("stopped"), () => throw new InvalidDataException("Malformed configuration"), _ => Task.FromResult(new JsonObject()), default);
            Require(!(Snapshot() with { RemoteController = rc }).Ready && rc.Probes!.Last().ExceptionDetails!.Contains("InvalidDataException"), "RC configuration failure was treated as optional off.");
        });
        await Check("Full report preserves original errors, native codes and redacts credentials", () =>
        {
            var ex = new AggregateException("Original action failure", new System.ComponentModel.Win32Exception(5, "Access denied"),
                new IOException("Authorization: Bearer syntheticsecrettoken\nCookie: session=secretcookie\nxrc_SYNTHETIC_ONLY_12345\n-----BEGIN PRIVATE KEY-----\nsecretpem\n-----END PRIVATE KEY-----"));
            var report = ServiceErrorReport.Create("Start", ex, Snapshot(), DateTimeOffset.Now.AddSeconds(-4));
            Require(report.Text.Contains("Windows error=5") && report.Text.Contains("Original action failure") && report.Text.Contains("Final observation")
                && !new[] { "syntheticsecrettoken", "secretcookie", "SYNTHETIC_ONLY_12345", "secretpem" }.Any(report.Text.Contains), "Report lost root cause or leaked a credential.");
            File.WriteAllText(Path.Combine(directory, "redacted-report.txt"), report.Text);
            return Task.CompletedTask;
        });
        await Check("Escaped JSON secrets are fully masked and original failure snapshots survive recovery", () =>
        {
            var secret = new JsonObject { ["password"] = "begin\"escaped-secret-end", ["private_key"] = "private-key-fixture" }.ToJsonString();
            string masked = TraceLog.Redact(secret);
            Require(!masked.Contains("escaped-secret-end") && !masked.Contains("private-key-fixture"), "Escaped JSON secret was only partly masked.");
            var original = Snapshot() with { Main = Main with { Ok = false, Error = new("access_denied", "Original permission failure", 5) } };
            var report = ServiceErrorReport.Create("Start", new AggregateException("Action and refresh failed",
                new ServiceReadinessException("Original permission failure", original, []), new IOException("Final refresh failed")), Snapshot());
            Require(report.Text.Contains("Original permission failure") && report.Text.Contains("native_code") && report.Text.Contains("access_denied")
                && report.Text.Contains("Final observation"), "Later recovery erased original error details.");
            return Task.CompletedTask;
        });
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
}
