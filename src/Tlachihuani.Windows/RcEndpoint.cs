// Tlachihuani — Angela López Mendoza · XAIHT
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tlachihuani.Contracts;
using Tlachihuani.RC;

namespace Tlachihuani.Windows;

public sealed record RcInstallation(Guid InstallationId, string OperatorSid)
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "XAIHT", "Tlachihuani.RemoteController");
    public static string Image => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "XAIHT", "Tlachihuani", "RCServer", "Tlachihuani.RC.Server.exe");
    public static RcInstallation Load()
    {
        var path = Path.Combine(Root, "installation.json");
        StateStore.RejectProfileOrReparse(path);
        using var file = File.OpenRead(path);
        if (file.Length > 4096) throw new InvalidDataException("Invalid RC installation manifest.");
        var value = JsonSerializer.Deserialize<RcInstallation>(file, RcProtocol.Json);
        if (value is null || value.InstallationId == Guid.Empty) throw new InvalidDataException("Invalid RC installation identity.");
        _ = new SecurityIdentifier(value.OperatorSid);
        return value;
    }
    public static RcConfiguration Configuration()
    {
        var path = Path.Combine(Root, "settings", "configuration.json");
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Invalid RC configuration size.");
        StateStore.RejectProfileOrReparse(path);
        return JsonSerializer.Deserialize<RcConfiguration>(File.ReadAllBytes(path), RcProtocol.Json) ?? throw new InvalidDataException("Empty RC configuration.");
    }
}

public static class RcEndpoint
{
    public static string Pipe(RcInstallation installation) => $"XAIHT.Tlachihuani.RC.v1.{installation.InstallationId:N}";
    public static async Task<JsonObject> CallInstalledAsync(JsonObject request, CancellationToken token)
    {
        var install = RcInstallation.Load();
        var scm = ScmBackend.ForRemoteController();
        scm.ValidateRemoteControllerInstallation();
        return await CallAsync(Pipe(install), scm.Query().Pid, RcInstallation.Image, request, token);
    }
    public static async Task<JsonObject> CallAsync(string pipeName, int pid, string image, JsonObject request, CancellationToken token)
    {
        using var pipe = await LocalPipe.ConnectAsync(pipeName, token);
        using var server = LocalPipe.VerifyServer(pipe, pid, image);
        await Protocol.WriteAsync(pipe, request, token);
        var reply = await Protocol.ReadAsync<JsonObject>(pipe, token);
        await pipe.WriteAsync(new byte[] { 6 }, token);
        if (reply["error"] is JsonObject error)
            throw new RcFault((string?)error["code"] ?? "control_failed", (string?)error["message"] ?? "RC refused the request.", (int?)error["status"] ?? 409);
        return reply;
    }
}
