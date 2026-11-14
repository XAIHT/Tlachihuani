// Tlachihuani — Angela López Mendoza · XAIHT
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Tlachihuani.RC;
using Tlachihuani.Windows;

namespace Tlachihuani.Tray;

/// <summary>
/// Launches only the installed English or Spanish application, in XT's own
/// unelevated interactive session. RC owns authorization and durable receipts;
/// this broker never starts services. Stop uses the application's cleanup signal.
/// </summary>
internal sealed class RcApplicationBroker
{
    private readonly Func<JsonObject, CancellationToken, Task<JsonObject>> call;

    internal RcApplicationBroker() : this(RcEndpoint.CallInstalledAsync) { }
    internal RcApplicationBroker(Func<JsonObject, CancellationToken, Task<JsonObject>> call) => this.call = call;

    internal async Task RunAsync(CancellationToken token)
    {
        JsonObject? completion = null;
        while (!token.IsCancellationRequested)
        {
            try
            {
                // Keep an observed outcome until acknowledged. A transport failure
                // must not cause the application launch to run a second time.
                if (completion is not null)
                {
                    await RequestAsync(completion, token);
                    completion = null;
                }
                var next = await RequestAsync(new() { ["op"] = "broker-next" }, token);
                if (next["operation"] is JsonObject item)
                {
                    var operation = item.Deserialize<OperationReceipt>(RcProtocol.Json)
                        ?? throw new InvalidDataException("Missing RC operation.");
                    var binding = next["binding"]?.Deserialize<AccountBinding>(RcProtocol.Json)
                        ?? throw new InvalidDataException("Missing RC account binding.");
                    JsonObject? result = null;
                    string? error = null;
                    try { result = await StartAsync(operation, binding, token); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (RcFault fault) { error = fault.Code; }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        error = operation.Kind == "application.stop" ? "application_stop_failed" : "application_launch_failed";
                        Trace.TraceWarning("RC application launch failed: {0}", ex.GetType().Name);
                    }
                    completion = new()
                    {
                        ["op"] = "broker-complete", ["operation_id"] = operation.Id,
                        ["result"] = result, ["error"] = error
                    };
                    continue;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (RcFault fault) when (completion is not null && fault.Code is "not_found" or "operation_conflict")
            {
                // The service has already finalized/removed this receipt. Never
                // repeat its side effect, even after an unknown transport outcome.
                completion = null;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // RC may be stopped or not installed. XT remains usable and the
                // application is never terminated because its controller went away.
                Trace.TraceInformation("RC application broker waiting: {0}", ex.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(2), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }

    private async Task<JsonObject> RequestAsync(JsonObject request, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        return await call(request, deadline.Token);
    }

    private async Task<bool> ReadyAsync(string bindingId, ProcessIdentity process, CancellationToken token)
    {
        if (process.HasExited) return false;
        var status = await RequestAsync(new() { ["op"] = "broker-application-status", ["binding_id"] = bindingId }, token);
        return !process.HasExited && ReadyForProcess(status, process.CreationTime);
    }

    internal static bool ReadyForProcess(JsonObject status, long processCreationTime) =>
        (bool?)status["connected"] == true && (bool?)status["ready"] == true
        && DateTimeOffset.TryParse((string?)status["observed_at"],
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var observed)
        && observed >= DateTimeOffset.FromFileTime(processCreationTime);

    private async Task<JsonObject> StartAsync(OperationReceipt operation, AccountBinding binding, CancellationToken token)
    {
        if (operation.Kind is not ("application.start" or "application.stop") || operation.BindingId != binding.Id || !binding.Enabled)
            throw new RcFault("invalid_launch", "This receipt does not authorize an application launch.");
        RcProtocol.RequireId(operation.Id, "operation identifier");
        RcProtocol.Edition(binding.Edition);
        if (operation.Deadline <= DateTimeOffset.UtcNow)
            throw new RcFault("deadline_exceeded", "The application launch deadline has passed.");
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.Value != binding.WindowsSid)
            throw new RcFault("wrong_windows_account", "The application belongs to another Windows account.");
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new RcFault("unelevated_session_required", "Open XT normally to launch Tlamatini.");
        RequireInteractiveSession();

        // Semaphore ownership is not thread-affine, so the readiness await can
        // safely continue on a different thread. One launch per account/edition.
        using var launchGate = new Semaphore(1, 1,
            @"Global\XAIHT.Tlachihuani.RC.Launch." + binding.WindowsSid + "." + binding.Edition);
        if (!launchGate.WaitOne(0)) throw new RcFault("launch_in_progress", "An application launch is already in progress.");
        try
        {
            if (operation.Kind == "application.stop") return await StopAsync(binding, token);
            // A heartbeat can outlive a stopped process by its lease interval.
            // Always verify the installed process before trusting adapter status;
            // otherwise an immediate stop/start can report success without launch.
            var target = DiscoverInstalled(binding.Edition);
            using var running = FindRunning(target, binding.WindowsSid);
            if (running is not null)
                return await ObserveReadyAsync(binding, running, false, token);

            token.ThrowIfCancellationRequested();
            RequireInteractiveSession();
            var scratch = Path.Combine(Path.GetDirectoryName(target)!, "Temp");
            try
            {
                // The main application also uses this exact directory. Do not
                // silently inherit Windows' user-profile TEMP during early boot.
                StateStore.RejectProfileOrReparse(scratch);
                Directory.CreateDirectory(scratch);
                StateStore.RejectProfileOrReparse(scratch);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RcFault)
            { throw new RcFault("temporary_storage_unavailable", "Tlamatini needs writable application storage outside Windows user profiles."); }

            // Keep the verified executable open without write/delete sharing
            // through CreateProcess. No shell, registry command, argument string
            // or browser-provided path is ever executed.
            RejectReparse(target);
            using var executable = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (executable.Length < 64 || executable.ReadByte() != 'M' || executable.ReadByte() != 'Z')
                throw new RcFault("invalid_installation", "The installed application executable is invalid.");
            var start = new ProcessStartInfo(target)
            {
                UseShellExecute = false, CreateNoWindow = false,
                WorkingDirectory = Path.GetDirectoryName(target)!, WindowStyle = ProcessWindowStyle.Normal
            };
            foreach (var variable in new[] { "TEMP", "TMP", "TMPDIR", "TLAMATINI_TEMP" }) start.Environment[variable] = scratch;
            using var process = Process.Start(start)
                ?? throw new RcFault("application_launch_failed", "Windows did not start the application.");
            using var started = ProcessIdentity.Open(process.Id);
            if (!Path.GetFullPath(started.ImagePath).Equals(target, StringComparison.OrdinalIgnoreCase))
                throw new RcFault("application_identity_changed", "The started application identity could not be verified.");
            return await ObserveReadyAsync(binding, started, true, token);
        }
        finally { launchGate.Release(); }
    }

    private static async Task<JsonObject> StopAsync(AccountBinding binding, CancellationToken token)
    {
        var target = DiscoverInstalled(binding.Edition);
        using var process = FindRunning(target, binding.WindowsSid);
        if (process is null) return Result(binding.Edition, "already_stopped", false);
        // A separate windowless helper attaches to the existing application
        // console. XT never detaches its own console or broadcasts to the desktop.
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--rc-stop-console");
        start.ArgumentList.Add(binding.Edition);
        start.ArgumentList.Add(process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add(process.CreationTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var helper = Process.Start(start) ?? throw new RcFault("application_stop_failed", "Windows could not request application shutdown.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try { await helper.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new RcFault("application_stop_timeout", "Application shutdown has not been confirmed."); }
        if (helper.ExitCode != 0)
            throw new RcFault(helper.ExitCode == 3 ? "shared_console_refused" : "application_stop_failed",
                "Application shutdown could not be verified. Close Tlamatini normally and start it through XT.");
        if (!process.HasExited) throw new RcFault("application_stop_unconfirmed", "The application has not exited.");
        return Result(binding.Edition, "stopped", false);
    }

    private async Task<JsonObject> ObserveReadyAsync(AccountBinding binding, ProcessIdentity process, bool launched, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (process.HasExited) throw new RcFault("application_exited", "Tlamatini exited before its account adapter became ready.");
            try
            {
                if (await ReadyAsync(binding.Id, process, token)) return Result(binding.Edition, launched ? "started" : "already_running", true);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Losing RC after CreateProcess is not evidence that launch
                // failed. Retain the observed process result for reconciliation.
                return Result(binding.Edition, launched ? "starting" : "running_adapter_unavailable", false);
            }
            await Task.Delay(500, token);
        }
        // Process creation and account readiness are separate observations.
        return Result(binding.Edition, launched ? "starting" : "running_adapter_unavailable", false);
    }

    private static JsonObject Result(string edition, string status, bool ready) => new()
    {
        ["edition"] = edition, ["status"] = status, ["ready"] = ready,
        ["observed_at"] = DateTimeOffset.UtcNow.ToString("O")
    };

    internal static string DiscoverInstalled(string edition)
    {
        var name = edition switch
        {
            "english" => "Tlamatini", "spanish" => "Tlamatini-Spanish",
            _ => throw new RcFault("invalid_edition", "Unknown Tlamatini edition.")
        };
        // Companion discovery deliberately survives preserved-agent uninstall
        // and may describe source code. Only the Installed Apps registration is
        // an installation locator; its fixed executable basename is ours.
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + name, false);
        if (key is null || key.GetValue("InstallLocation", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string location
            || key.GetValueKind("InstallLocation") != RegistryValueKind.String)
            throw new RcFault("application_not_installed", "The selected Tlamatini edition is not installed for this Windows account.");
        return ValidateInstalledPath(edition, location);
    }

    internal static string ValidateInstalledPath(string edition, string location)
    {
        var filename = edition switch
        {
            "english" => "Tlamatini.exe", "spanish" => "Tlamatini-Spanish.exe",
            _ => throw new RcFault("invalid_edition", "Unknown Tlamatini edition.")
        };
        if (string.IsNullOrWhiteSpace(location) || location.Length > 30000 || location.Any(char.IsControl)
            || location.Contains('"') || !Path.IsPathFullyQualified(location) || location.StartsWith(@"\", StringComparison.Ordinal))
            throw new RcFault("invalid_installation", "The installed application location is invalid.");
        var directory = Path.GetFullPath(location);
        var target = Path.GetFullPath(Path.Combine(directory, filename));
        if (!Directory.Exists(directory) || !File.Exists(target))
            throw new RcFault("application_not_installed", "The installed application executable is missing.");
        RejectReparse(target);
        return target;
    }

    private static void RejectReparse(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new RcFault("invalid_installation", "Application launch does not follow redirected installation paths.");
        }
    }

    private static ProcessIdentity? FindRunning(string target, string sid)
    {
        using var self = Process.GetCurrentProcess();
        var candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(target));
        try
        {
            foreach (var candidate in candidates)
            {
                ProcessIdentity? owned = null;
                try
                {
                    owned = ProcessIdentity.Open(candidate.Id);
                    if (owned.HasExited || !Path.GetFullPath(owned.ImagePath).Equals(target, StringComparison.OrdinalIgnoreCase)) continue;
                    using var handle = OpenProcess(0x1000, false, candidate.Id);
                    if (handle.IsInvalid || !OpenProcessToken(handle, 8, out var accessToken))
                        throw new RcFault("application_identity_unverified", "A running application could not be verified.");
                    using (accessToken)
                    using (var identity = new WindowsIdentity(accessToken.DangerousGetHandle()))
                    {
                        if (identity.User?.Value != sid || candidate.SessionId != self.SessionId)
                            throw new RcFault("application_in_other_session", "Tlamatini is already running in another Windows session.");
                    }
                    var verified = owned;
                    owned = null;
                    return verified;
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode is 87 or 1168) { /* process exited during observation */ }
                finally { owned?.Dispose(); }
            }
            return null;
        }
        finally { foreach (var candidate in candidates) candidate.Dispose(); }
    }

    private static void RequireInteractiveSession()
    {
        using var self = Process.GetCurrentProcess();
        if (!Environment.UserInteractive || self.SessionId == 0)
            throw new RcFault("interactive_session_required", "An interactive XT session is required.");
        if (!WTSQuerySessionInformation(IntPtr.Zero, self.SessionId, 8, out var state, out var bytes))
            throw new RcFault("session_state_unavailable", "The Windows session could not be verified.");
        try
        {
            if (bytes < sizeof(int) || Marshal.ReadInt32(state) is not (0 or 4))
                throw new RcFault("interactive_session_required", "The Windows user session is not available.");
        }
        finally { WTSFreeMemory(state); }
        // A locked or disconnected signed-in desktop may receive authorized
        // remote work. Development foreground rules never constrain this broker.
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool WTSQuerySessionInformation(IntPtr server, int session, int information, out IntPtr buffer, out int bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr buffer);
}
