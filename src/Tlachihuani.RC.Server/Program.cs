// Tlachihuani — Angela López Mendoza · XAIHT
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting.WindowsServices;
using Tlachihuani.RC;
using Tlachihuani.Contracts;
using Tlachihuani.RC.Server;
using Tlachihuani.Shared;
using Tlachihuani.Windows;

if (args.FirstOrDefault() == "--console") NativeConsole.Attach();
if (args is ["--control"] or ["--dev-control", _, _])
{
    // XT starts this tool every few seconds: a run identical to the previous run of the same
    // request is not written (TraceLog.Pass across runs); a different answer is written in full.
    using var quietRun = TraceLog.Pass("rc-control", acrossRuns: true);
    LogPaths.UseUserLog("rc-control");
    // Native JSON is UTF-8 regardless of the interactive Windows code page.
    Console.InputEncoding = new System.Text.UTF8Encoding(false, true);
    Console.OutputEncoding = new System.Text.UTF8Encoding(false);
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var line = await Console.In.ReadLineAsync(timeout.Token);
        if (line is null || line.Length > RcProtocol.MaxMessageBytes) return 2;
        var request = JsonNode.Parse(line)?.AsObject() ?? throw new InvalidDataException("Expected a JSON object.");
        quietRun.Rename("rc-control " + (string?)request["op"]);
        JsonObject reply;
        if (args.Length == 1) reply = await RcEndpoint.CallInstalledAsync(request, timeout.Token);
        else if (Guid.TryParseExact(args[1], "N", out var dev) && int.TryParse(args[2], out var pid))
            reply = await RcEndpoint.CallAsync("XAIHT.RC.Dev." + dev.ToString("N"), pid, Environment.ProcessPath!, request, timeout.Token);
        else return 2;
        Console.WriteLine(reply.ToJsonString(RcProtocol.Json));
        return 0;
    }
    catch (Exception ex) when (ex is not OutOfMemoryException)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { error = new { code = ex is RcFault fault ? fault.Code : "control_unavailable", message = ex.Message } }, RcProtocol.Json));
        return 1;
    }
}

RcHostSettings settings;
bool installed = WindowsServiceHelpers.IsWindowsService();
bool acceptance = false;
if (installed && args is ["--scm-acceptance", var qaDirectory, var qaId, var qaPort, var qaSid]
    && Guid.TryParseExact(qaId, "N", out var qaGuid) && int.TryParse(qaPort, out var qaNumber))
{
    // Closed acceptance mode: an elevated native harness alone registers this fixed
    // sibling service. It never loads the production installation or account data.
    acceptance = true;
    var qaRoot = Path.GetFullPath(qaDirectory);
    if (!Path.GetFileName(qaRoot).StartsWith("rc-scm-", StringComparison.Ordinal)
        || !qaRoot.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "XAIHT", "Acceptance") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Invalid isolated acceptance root.");
    _ = new SecurityIdentifier(qaSid);
    OperatorProcessAccess.AllowOperatorStop(qaSid);
    settings = new(qaRoot, "XAIHT.RC.Dev." + qaGuid.ToString("N"), qaSid,
        new RcConfiguration { Port = qaNumber, PublicOrigin = $"http://127.0.0.1:{qaNumber}" }, true);
}
else if (installed && args.Length == 0)
{
    var installation = RcInstallation.Load();
    if (!string.Equals(Environment.ProcessPath, RcInstallation.Image, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("RC image differs from the installed service.");
    // The operator may check this process's identity, and XT's Stop Services may end it.
    OperatorProcessAccess.AllowOperatorStop(installation.OperatorSid);
    settings = new(RcInstallation.Root, RcEndpoint.Pipe(installation), installation.OperatorSid, RcInstallation.Configuration(), true);
}
else if (!installed && args is ["--console", var directory, var id, var portText] && Guid.TryParseExact(id, "N", out var devId) && int.TryParse(portText, out var port))
{
    var config = new RcConfiguration { Port = port, PublicOrigin = $"http://127.0.0.1:{port}" };
    var configPath = Path.Combine(Path.GetFullPath(directory), "settings", "configuration.json");
    if (File.Exists(configPath)) config = JsonSerializer.Deserialize<RcConfiguration>(File.ReadAllBytes(configPath), RcProtocol.Json) ?? throw new InvalidDataException("Invalid development configuration.");
    settings = new(Path.GetFullPath(directory), "XAIHT.RC.Dev." + devId.ToString("N"), WindowsIdentity.GetCurrent().User!.Value, config, false);
}
else { Console.Error.WriteLine("Run through SCM, or --console <isolated data directory> <guid> <port>."); return 2; }
StateStore.RejectProfileOrReparse(settings.Root);
// Verbose trace log: %ProgramData%\XAIHT\Tlachihuani.RemoteController\logs\rc.log (installed),
// <data>\logs\rc.log for an acceptance or console run.
if (installed && !acceptance) LogPaths.UseServiceLog("rc", "rc.log", settings.OperatorSid, LogPaths.RcFolder, LogPaths.RcStateFolder);
else LogPaths.UseServiceLog("rc", "rc.log", settings.OperatorSid, Path.Combine(settings.Root, "logs"), Path.Combine(settings.Root, "state", "logs"));
TraceLog.I($"Remote-Controller starting: root {settings.Root}, pipe {settings.Pipe}, operator {settings.OperatorSid}, port {settings.Configuration.Port}, origin {settings.Configuration.PublicOrigin}, installed {settings.Installed}, acceptance {acceptance}");
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], ContentRootPath = AppContext.BaseDirectory });
builder.Services.AddWindowsService(o => o.ServiceName = acceptance ? "Tlachihuani.RemoteController.Acceptance" : RcProtocol.ServiceName);
builder.Logging.ClearProviders();
if (installed) builder.Logging.AddEventLog(o => o.SourceName = ".NET Runtime");
else builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
builder.Logging.AddTlachihuaniTrace();
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<RcRuntime>();
builder.Services.AddHostedService<RcService>();
using var host = builder.Build();
await host.RunAsync();
return 0;

namespace Tlachihuani.RC.Server
{
    internal static class NativeConsole
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool AttachConsole(uint pid);
        internal static void Attach() => AttachConsole(uint.MaxValue);
    }
    internal sealed record RcHostSettings(string Root, string Pipe, string OperatorSid, RcConfiguration Configuration, bool Installed);
}
