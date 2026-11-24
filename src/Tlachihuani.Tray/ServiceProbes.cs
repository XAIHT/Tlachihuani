// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (the bounded, redacted probe records are the diagnostic output)
using System.Diagnostics;
using System.Text.Json.Nodes;
using Tlachihuani.Contracts;
using Tlachihuani.Core;

namespace Tlachihuani.Tray;

internal static class ServiceProbes
{
    internal const string HealthAddress = "http://127.0.0.1:12064/api/v1/health";
    internal static readonly TimeSpan ProbeBudget = TimeSpan.FromSeconds(3);

    internal static async Task<JobsState> JobsAsync(Func<NativeStatus> query,
        Func<JsonObject, CancellationToken, Task<JsonObject>> call, HttpClient http, CancellationToken token,
        TimeSpan? probeBudget = null)
    {
        var observations = new List<ProbeObservation>();
        var clock = Stopwatch.StartNew();
        string stage = "Tlachihuani.Jobs / Windows SCM";
        NativeStatus native = new("unknown");
        var budget = probeBudget ?? ProbeBudget;
        string details = "SCM query did not complete.";
        void Record(bool passed, Exception? error = null)
        {
            observations.Add(new(stage, passed, DateTimeOffset.Now, clock.Elapsed.TotalMilliseconds,
                TraceLog.Redact(details), error is null ? null : ServiceErrorReport.ExceptionText(error)));
            clock.Restart();
        }
        JobsState Result(bool ready = false) => new(native.State, ready, native.Pid, native.NativeExitCode, observations);
        try
        {
            token.ThrowIfCancellationRequested();
            native = query();
            details = $"SCM={native.State}; PID={native.Pid}; Windows exit code={native.NativeExitCode}.";
            Record(native.State == "running");
            if (native.State != "running") return Result();

            stage = "Tlachihuani.Jobs / authenticated provider status";
            details = $"Provider status; timeout={budget.TotalSeconds:0.###} seconds; transfer statistics excluded.";
            JsonObject provider;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(budget);
                provider = await call(new() { ["op"] = "status", ["include_impact"] = false }, deadline.Token);
            }
            details = "Provider reply: " + TraceLog.Json(provider);
            bool enabled = (bool?)provider["enabled"] == true, engine = (bool?)provider["engine"] == true,
                web = (bool?)provider["web"] == true;
            bool providerReady = enabled && engine && web && (int?)provider["port"] == 12064;
            if (!providerReady) details += " Required: enabled=true, engine=true, web=true, port=12064.";
            Record(providerReady);
            if (!providerReady) return Result();

            // Each transport gets its own budget. A slow pipe must not consume HTTP's time.
            stage = "Torrenter / HTTP health";
            details = $"GET {HealthAddress}; timeout={budget.TotalSeconds:0.###} seconds; no response yet.";
            using var healthDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            healthDeadline.CancelAfter(budget);
            using var response = await http.GetAsync(HealthAddress, HttpCompletionOption.ResponseHeadersRead, healthDeadline.Token);
            details = $"GET {HealthAddress}; HTTP {(int)response.StatusCode} {response.ReasonPhrase}.";
            // Read at most 4097 bytes even when a server omits or lies about Content-Length.
            await using var stream = await response.Content.ReadAsStreamAsync(healthDeadline.Token);
            var bytes = new byte[4097];
            int used = 0;
            while (used < bytes.Length)
            {
                int count = await stream.ReadAsync(bytes.AsMemory(used), healthDeadline.Token);
                if (count == 0) break;
                used += count;
            }
            if (used > 4096) throw new InvalidDataException("Health response exceeds the 4096-byte limit.");
            details += " Body: " + TraceLog.Redact(System.Text.Encoding.UTF8.GetString(bytes, 0, used));
            if (!response.IsSuccessStatusCode) { Record(false); return Result(); }
            var health = JsonNode.Parse(bytes.AsSpan(0, used));
            bool ready = (int?)health?["api_version"] == 1 && (string?)health?["engine"] == "ready";
            if (!ready) details += " Required: api_version=1, engine=ready.";
            Record(ready);
            return Result(ready);
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is not OutOfMemoryException)
        {
            details += " " + (ex is OperationCanceledException ? "Probe timed out." : ex.Message);
            Record(false, ex);
            return Result();
        }
    }

    internal static async Task<RemoteControllerState> RemoteAsync(Func<NativeStatus> query, Func<bool> included,
        Func<CancellationToken, Task<JsonObject>> call, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var observations = new List<ProbeObservation>();
        NativeStatus native = new("unknown");
        bool useRc = false;
        string stage = "Remote-Controller / Windows SCM", details = "SCM query did not complete.";
        void Record(bool passed, Exception? ex = null)
        {
            observations.Add(new(stage, passed, DateTimeOffset.Now, clock.Elapsed.TotalMilliseconds,
                TraceLog.Redact(details), ex is null ? null : ServiceErrorReport.ExceptionText(ex)));
            clock.Restart();
        }
        RemoteControllerState Result(bool ready = false, string? error = null) =>
            new(native.State, ready, useRc, error, native.Pid, native.NativeExitCode, observations);
        try
        {
            token.ThrowIfCancellationRequested();
            native = query();
            details = $"SCM={native.State}; PID={native.Pid}; Windows exit code={native.NativeExitCode}.";
            Record(native.State is "running" or "stopped" or "not_installed");
            if (native.State == "not_installed") return Result();
            stage = "Remote-Controller / installation and startup configuration";
            details = "Could not validate the installation or read its startup configuration.";
            useRc = included();
            details = "Include in Start Services=" + useRc;
            Record(true);
            if (native.State != "running") return Result();
            stage = "Remote-Controller / authenticated listener status";
            details = "Listener status; timeout=3 seconds; no response yet.";
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(ProbeBudget);
            var status = await call(deadline.Token);
            details = "Listener reply: " + TraceLog.Json(status);
            bool ready = (bool?)status["listener"] == true && (string?)status["health"] == "ready";
            Record(ready);
            return Result(ready, (string?)status["error"]);
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is not OutOfMemoryException)
        {
            details += " " + (ex is OperationCanceledException ? "Probe timed out." : ex.Message);
            Record(false, ex);
            return Result(error: TraceLog.Redact(details));
        }
    }
}
