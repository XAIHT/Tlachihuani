// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (diagnostic snapshots are logged explicitly after redaction)
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Tlachihuani.Contracts;
using Tlachihuani.Core;

namespace Tlachihuani.Tray;

internal sealed record ProbeObservation(string Stage, bool Passed, DateTimeOffset ObservedAt,
    double ElapsedMilliseconds, string Details, string? ExceptionDetails = null);

internal sealed record JobsState(string State, bool ApiReady = false, int Pid = 0,
    int NativeExitCode = 0, IReadOnlyList<ProbeObservation>? Probes = null);
internal sealed record RemoteControllerState(string State, bool Ready = false, bool Included = false,
    string? Error = null, int Pid = 0, int NativeExitCode = 0, IReadOnlyList<ProbeObservation>? Probes = null);

internal sealed record ServiceSnapshot(ControlReply? Main, JobsState? Jobs, RemoteControllerState? RemoteController)
{
    internal bool Ready => Main is { Ok: true, Service.ScmState: "running", Service.Health: "healthy" }
        && Jobs?.ApiReady == true && RemoteController is not null
        && (RemoteController.Ready || (!RemoteController.Included && RemoteController.Error is null
            && RemoteController.State is "stopped" or "not_installed"));
    internal bool Stopped => Main?.Service?.ScmState is "stopped" or "not_installed"
        && Jobs?.State is "stopped" or "not_installed"
        && RemoteController?.State is "stopped" or "not_installed";

    internal string Problem => Main is not { Ok: true, Service.ScmState: "running", Service.Health: "healthy" }
        ? "Tlachihuani: " + (Main?.Error?.Message ?? $"SCM {Main?.Service?.ScmState ?? "unknown"}; health {Main?.Service?.Health ?? "unknown"}.")
        : Jobs?.ApiReady != true ? "Tlachihuani.Jobs: " + (Jobs?.Probes?.LastOrDefault(p => !p.Passed)?.Details ?? $"SCM {Jobs?.State ?? "unknown"}; readiness unconfirmed.")
        : "Remote-Controller: " + (RemoteController?.Probes?.LastOrDefault(p => !p.Passed)?.Details ?? RemoteController?.Error ?? $"SCM {RemoteController?.State ?? "unknown"}; readiness unconfirmed.");
}

internal sealed class ServiceReadinessException(string message, ServiceSnapshot? snapshot,
    IReadOnlyList<ServiceSnapshot> attempts, Exception? inner = null) : Exception(message, inner)
{
    internal ServiceSnapshot? Snapshot { get; } = snapshot;
    internal IReadOnlyList<ServiceSnapshot> Attempts { get; } = attempts;
}

internal static class ServiceReadiness
{
    // One bounded confirmation of the whole service group. A failed observation is
    // retried, not reported as a failed Start. There is no second one-shot verdict.
    internal static async Task<ServiceSnapshot> WaitAsync(Func<CancellationToken, Task<ServiceSnapshot>> observe,
        TimeSpan budget, CancellationToken token, TimeSpan? retryDelay = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(budget);
        var attempts = new Queue<ServiceSnapshot>();
        ServiceSnapshot? latest = null;
        try
        {
            while (true)
            {
                latest = await observe(deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                if (latest.Ready) return latest;
                attempts.Enqueue(latest);
                if (attempts.Count > 8) attempts.Dequeue();
                await Task.Delay(retryDelay ?? TimeSpan.FromMilliseconds(400), deadline.Token);
            }
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        {
            throw new ServiceReadinessException($"Service readiness was not confirmed within {budget.TotalSeconds:0.#} seconds. "
                + (latest?.Problem ?? "The first observation did not finish."), latest, attempts.ToArray(), ex);
        }
    }
}

internal sealed record ServiceErrorReport(string Id, string Summary, string Text)
{
    internal static ServiceErrorReport Create(string action, Exception error, ServiceSnapshot? snapshot = null,
        DateTimeOffset? startedAt = null, string? context = null)
    {
        string id = Guid.NewGuid().ToString("N")[..12];
        var now = DateTimeOffset.Now;
        var text = new StringBuilder().AppendLine($"Tlachihuani service error report {id}")
            .AppendLine($"Action: {action}").AppendLine($"Reported: {now:O}")
            .AppendLine($"Started: {startedAt?.ToString("O") ?? "not recorded"}")
            .AppendLine($"Elapsed: {(startedAt is { } start ? (now - start).TotalSeconds.ToString("0.###") + " seconds" : "not recorded")}")
            .AppendLine($"XT version: {Application.ProductVersion}")
            .AppendLine($"XT executable: {Environment.ProcessPath}")
            .AppendLine($"XT PID: {Environment.ProcessId}; .NET: {Environment.Version}; OS: {Environment.OSVersion}")
            .AppendLine($"Machine: {Environment.MachineName}; Windows user: {Environment.UserDomainName}\\{Environment.UserName}")
            .AppendLine().AppendLine("Original failure:").AppendLine(ExceptionText(error));
        if (context is not null) text.AppendLine().AppendLine("Action context:").AppendLine(context);
        void AddFailureObservations(Exception failure)
        {
            if (failure is ServiceReadinessException readiness)
            {
                AddSnapshot(text, "Observation at the original failure", readiness.Snapshot);
                int number = 0;
                foreach (var attempt in readiness.Attempts)
                    AddSnapshot(text, $"Readiness attempt {++number} (last eight retained)", attempt);
            }
            if (failure is AggregateException aggregate) foreach (var inner in aggregate.InnerExceptions) AddFailureObservations(inner);
            else if (failure.InnerException is { } inner) AddFailureObservations(inner);
        }
        AddFailureObservations(error);
        AddSnapshot(text, "Final observation (does not replace the original failure)", snapshot);
        text.AppendLine().AppendLine("Log locations:").AppendLine(TlachihuaniLog.FilePath);
        string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "XAIHT");
        foreach (string service in new[] { "Tlachihuani", "Tlachihuani.Jobs", "Tlachihuani.RemoteController" })
            text.AppendLine(Path.Combine(data, service, "logs"));
        text.AppendLine().AppendLine("Next: inspect the first failed probe and its exception above. SCM Running alone does not prove API readiness.")
            .AppendLine("Timeout means readiness was not confirmed; it does not prove the service stopped. Refresh before repeating an action.");
        return new(id, TraceLog.Redact(error.Message), TraceLog.Redact(text.ToString()));
    }

    internal static string ExceptionText(Exception error)
    {
        var detail = new StringBuilder(error.ToString()); // Includes stack and all inner exceptions.
        void Codes(Exception ex)
        {
            detail.AppendLine().Append($"{ex.GetType().FullName}: HResult=0x{ex.HResult:X8}");
            if (ex is Win32Exception win32) detail.Append($"; Windows error={win32.NativeErrorCode}");
            if (ex is LifecycleException lifecycle) detail.Append($"; code={lifecycle.Code}");
            if (ex is ProtocolException protocol) detail.Append($"; code={protocol.Code}");
            if (ex is Tlachihuani.RC.RcFault rc) detail.Append($"; code={rc.Code}; status={rc.Status}");
            if (ex is HttpRequestException http) detail.Append($"; HTTP status={http.StatusCode?.ToString() ?? "no response"}");
            if (ex is AggregateException aggregate) foreach (var inner in aggregate.InnerExceptions) Codes(inner);
            else if (ex.InnerException is { } inner) Codes(inner);
        }
        Codes(error);
        return TraceLog.Redact(detail.ToString());
    }

    private static void AddSnapshot(StringBuilder text, string title, ServiceSnapshot? snapshot)
    {
        text.AppendLine().AppendLine(title + ":");
        if (snapshot is null) { text.AppendLine("No service observation available."); return; }
        text.AppendLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(Protocol.Json) { WriteIndented = true }));
    }

    internal string Save()
    {
        // This is the user's persistent last error report, not development scratch.
        string path = Path.Combine(TlachihuaniLog.Folder, "Last-service-error.txt");
        try
        {
            Directory.CreateDirectory(TlachihuaniLog.Folder);
            File.WriteAllText(path, Text, new UTF8Encoding(false));
            TlachihuaniLog.Error("services", $"Error report {Id}: {Summary}", Text);
            return "Saved: " + path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return "Report could not be saved: " + TraceLog.Redact(ex.Message) + ". Use Copy report."; }
    }
}
