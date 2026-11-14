// Tlachihuani — Angela López Mendoza · XAIHT
using System.Text.Json;
using System.Text.Json.Nodes;
using Tlachihuani.RC;
using Tlachihuani.Tray;
using Tlachihuani.Windows;

internal static class RcBrokerChecks
{
    internal static int Run(string output)
    {
        output = Path.GetFullPath(output);
        StateStore.RejectProfileOrReparse(output);
        Directory.CreateDirectory(output);
        var fixture = Path.Combine(output, "path-fixtures-" + RcProtocol.Id());
        var english = Path.Combine(fixture, "English application");
        var spanish = Path.Combine(fixture, "Aplicación española");
        Directory.CreateDirectory(english); Directory.CreateDirectory(spanish);
        // These are inert path fixtures, never executable application payloads.
        File.WriteAllText(Path.Combine(english, "Tlamatini.exe"), "RC path fixture only");
        File.WriteAllText(Path.Combine(spanish, "Tlamatini-Spanish.exe"), "RC path fixture only");
        var tests = new List<(string Name, Action Run)>();
        void Test(string name, Action run) => tests.Add((name, run));
        Test("English resolves only the fixed executable basename", () => Eq(Path.Combine(english, "Tlamatini.exe"), RcApplicationBroker.ValidateInstalledPath("english", english)));
        Test("Spanish resolves its distinct fixed executable basename", () => Eq(Path.Combine(spanish, "Tlamatini-Spanish.exe"), RcApplicationBroker.ValidateInstalledPath("spanish", spanish)));
        Test("Paths containing spaces remain a single application path", () => True(RcApplicationBroker.ValidateInstalledPath("english", english).Contains("English application")));
        Test("Unicode installation paths retain their complete identity", () => True(RcApplicationBroker.ValidateInstalledPath("spanish", spanish).Contains("Aplicación española")));
        Test("Trailing directory separator canonicalizes without adding arguments", () => Eq(Path.Combine(english, "Tlamatini.exe"), RcApplicationBroker.ValidateInstalledPath("english", english + Path.DirectorySeparatorChar)));
        Test("English cannot select a Spanish-only installation", () => Fault("application_not_installed", () => RcApplicationBroker.ValidateInstalledPath("english", spanish)));
        Test("Spanish cannot select an English-only installation", () => Fault("application_not_installed", () => RcApplicationBroker.ValidateInstalledPath("spanish", english)));
        Test("Unknown edition is refused before discovery", () => Fault("invalid_edition", () => RcApplicationBroker.DiscoverInstalled("torrenter")));
        Test("Empty installation location is refused", () => Fault("invalid_installation", () => RcApplicationBroker.ValidateInstalledPath("english", "")));
        Test("Whitespace installation location is refused", () => Fault("invalid_installation", () => RcApplicationBroker.ValidateInstalledPath("english", " \t ")));
        Test("Relative installation location is refused", () => Fault("invalid_installation", () => RcApplicationBroker.ValidateInstalledPath("english", "relative")));
        Test("Drive-relative installation location is refused", () => Fault("invalid_installation", () => RcApplicationBroker.ValidateInstalledPath("english", @"C:relative")));
        Test("UNC installation location is refused", () => Fault("invalid_installation", () => RcApplicationBroker.ValidateInstalledPath("english", @"\\server\share")));
        Test("Win32 device namespace location is refused", () => Fault("invalid_installation", () => RcApplicationBroker.ValidateInstalledPath("english", @"\\?\C:\application")));
        Test("Quoted command-line location is refused", () => Fault("invalid_installation", () => RcApplicationBroker.ValidateInstalledPath("english", "\"" + english + "\"")));
        Test("Newline injection in location is refused", () => Fault("invalid_installation", () => RcApplicationBroker.ValidateInstalledPath("english", english + "\nanything")));
        Test("NUL injection in location is refused", () => Fault("invalid_installation", () => RcApplicationBroker.ValidateInstalledPath("english", english + "\0")));
        Test("Missing installation directory is refused", () => Fault("application_not_installed", () => RcApplicationBroker.ValidateInstalledPath("english", Path.Combine(fixture, "absent"))));
        Test("An executable cannot masquerade as its installation directory", () => Fault("application_not_installed", () => RcApplicationBroker.ValidateInstalledPath("english", Path.Combine(english, "Tlamatini.exe"))));
        Test("A differently named executable is never selected", () =>
        {
            var directory = Path.Combine(fixture, "wrong basename"); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "cmd.exe"), "inert fixture");
            Fault("application_not_installed", () => RcApplicationBroker.ValidateInstalledPath("english", directory));
        });
        Test("A directory redirect is refused", () =>
        {
            var alias = Path.Combine(fixture, "redirected directory");
            Link(() => Directory.CreateSymbolicLink(alias, english));
            Fault("invalid_installation", () => RcApplicationBroker.ValidateInstalledPath("english", alias));
        });
        Test("An executable redirect is refused", () =>
        {
            var directory = Path.Combine(fixture, "redirected executable"); Directory.CreateDirectory(directory);
            Link(() => File.CreateSymbolicLink(Path.Combine(directory, "Tlamatini.exe"), Path.Combine(english, "Tlamatini.exe")));
            Fault("invalid_installation", () => RcApplicationBroker.ValidateInstalledPath("english", directory));
        });
        Test("RC and Torrenter have independent SCM identities", () => True(RcProtocol.ServiceName != JobsEndpoint.ServiceName));
        Test("RC and Torrenter have independent executable locations", () => True(!RcInstallation.Image.Equals(JobsInstallation.Image, StringComparison.OrdinalIgnoreCase)));
        Test("RC and Torrenter have independent state locations", () => True(!RcInstallation.Root.Equals(JobsInstallation.Root, StringComparison.OrdinalIgnoreCase)));
        Test("Even an equal installation UUID cannot alias native pipes", () =>
        {
            var id = Guid.NewGuid();
            True(RcEndpoint.Pipe(new(id, "S-1-5-21-1")) != JobsEndpoint.Pipe(new(id, "S-1-5-21-1")));
        });
        var created = DateTimeOffset.Parse("2026-11-14T02:24:18Z", System.Globalization.CultureInfo.InvariantCulture);
        JsonObject Observation(bool connected, bool ready, string? seen) => new()
        { ["connected"] = connected, ["ready"] = ready, ["observed_at"] = seen };
        Test("Restart ignores a ready heartbeat from the previous process", () =>
            True(!RcApplicationBroker.ReadyForProcess(Observation(true, true, created.AddSeconds(-1).ToString("O")), created.ToFileTime())));
        Test("Current process readiness requires an observation timestamp", () =>
            True(!RcApplicationBroker.ReadyForProcess(Observation(true, true, null), created.ToFileTime())));
        Test("Malformed readiness timestamp cannot certify a process", () =>
            True(!RcApplicationBroker.ReadyForProcess(Observation(true, true, "invalid"), created.ToFileTime())));
        Test("Disconnected adapter cannot certify a running process", () =>
            True(!RcApplicationBroker.ReadyForProcess(Observation(false, true, created.AddSeconds(1).ToString("O")), created.ToFileTime())));
        Test("Loading adapter remains loading after process creation", () =>
            True(!RcApplicationBroker.ReadyForProcess(Observation(true, false, created.AddSeconds(1).ToString("O")), created.ToFileTime())));
        Test("Fresh heartbeat certifies the current process", () =>
            True(RcApplicationBroker.ReadyForProcess(Observation(true, true, created.AddSeconds(1).ToString("O")), created.ToFileTime())));
        Test("Equivalent timestamp offsets preserve process readiness", () =>
            True(RcApplicationBroker.ReadyForProcess(Observation(true, true, created.AddSeconds(1).ToOffset(TimeSpan.FromHours(-6)).ToString("O")), created.ToFileTime())));

        var results = new List<object>(); int passed = 0, failed = 0, skipped = 0;
        foreach (var test in tests)
        {
            try { test.Run(); passed++; results.Add(new { name = test.Name, passed = true }); Console.WriteLine("PASS RC broker " + test.Name); }
            catch (UnavailableFixture ex) { skipped++; results.Add(new { name = test.Name, skipped = true, reason = ex.Message }); Console.WriteLine("SKIP RC broker " + test.Name + ": " + ex.Message); }
            catch (Exception ex) { failed++; results.Add(new { name = test.Name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL RC broker " + test.Name + ": " + ex.Message); }
        }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { total = tests.Count, passed, failed, skipped, results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"RC broker path checks: {passed}/{tests.Count} passed; {failed} failed; {skipped} unavailable fixtures. No application was launched and no registry value was changed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Link(Action create)
    {
        try { create(); }
        catch (UnauthorizedAccessException) { throw new UnavailableFixture("Windows did not allow a symbolic-link fixture."); }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 5 or 1314) { throw new UnavailableFixture("Windows symbolic-link privilege is unavailable."); }
    }
    private sealed class UnavailableFixture(string message) : Exception(message);
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Eq(string expected, string actual) { if (!expected.Equals(actual, StringComparison.OrdinalIgnoreCase)) throw new Exception($"Expected {expected}; received {actual}."); }
    private static void Fault(string code, Action action)
    {
        try { action(); }
        catch (RcFault fault) { if (fault.Code != code) throw new Exception($"Expected {code}; received {fault.Code}."); return; }
        throw new Exception("Expected RC refusal: " + code);
    }
}
