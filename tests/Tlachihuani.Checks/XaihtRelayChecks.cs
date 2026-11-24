// Tlachihuani — Angela López Mendoza · XAIHT
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tlachihuani.RC;
using Tlachihuani.Tray;
using Tlachihuani.Windows;

internal static class XaihtRelayChecks
{
    // Visible acceptance runner only. No production settings, credentials,
    // sessions or services are changed. It uses the real XT command processor.
    internal static async Task<int> Run(string[] args)
    {
        if (args.Length != 14) throw new ArgumentException("Expected --server --key-file --pipe --pid --image --bindings-file --output pairs.");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2) options.Add(args[i], args[i + 1]);
        var server = XaihtLinkStore.NormalizeServer(options["--server"]) ?? throw new ArgumentException("Invalid local server.");
        if (!new Uri(server).IsLoopback) throw new ArgumentException("Acceptance relay must be loopback.");
        var pipe = options["--pipe"];
        if (!pipe.StartsWith("XAIHT.RC.Dev.", StringComparison.Ordinal) || !RcProtocol.IsId(pipe["XAIHT.RC.Dev.".Length..]))
            throw new ArgumentException("Acceptance relay requires the isolated development pipe.");
        var output = Path.GetFullPath(options["--output"]); StateStore.RejectProfileOrReparse(output); Directory.CreateDirectory(output);
        var keyPath = Path.GetFullPath(options["--key-file"]); StateStore.RejectProfileOrReparse(keyPath);
        var bindingsPath = Path.GetFullPath(options["--bindings-file"]); StateStore.RejectProfileOrReparse(bindingsPath);
        var key = File.ReadAllText(keyPath).Trim();
        if (!XaihtLinkStore.LooksLikeKey(key)) throw new ArgumentException("Invalid fixture key.");
        var bindings = JsonSerializer.Deserialize<string[]>(File.ReadAllText(bindingsPath)) ?? [];
        if (bindings.Length is < 1 or > 2 || bindings.Any(id => !RcProtocol.IsId(id))) throw new ArgumentException("One or two exact fixture bindings required.");
        var settings = new XaihtLinkSettings(server, key);
        var pid = int.Parse(options["--pid"], System.Globalization.CultureInfo.InvariantCulture);
        var image = Path.GetFullPath(options["--image"]); StateStore.RejectProfileOrReparse(image);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; lifetime.Cancel(); };
        var token = lifetime.Token;
        Task<JsonObject> Native(JsonObject request, CancellationToken cancel) => RcEndpoint.CallAsync(pipe, pid, image, request, cancel);
        var relay = new XaihtCommands(Native);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(40) };
        XaihtIdentity? approved = null;
        Console.WriteLine("Real XT command relay: isolated QA accounts; prompts/answers are not logged.");
        try
        {
            while (!token.IsCancellationRequested && !File.Exists(Path.Combine(output, "stop")))
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, server + "/api/rc/poll?wait=1");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                request.Headers.TryAddWithoutValidation("X-Tlachihuani-Command-Protocol", "1");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                var body = JsonNode.Parse(await XaihtCommands.ReadBounded(response.Content, 512 * 1024, token))!.AsObject();
                var identity = XaihtAccess.Validate(new XaihtIdentity(RcProtocol.Hash(server + "\n" + key), server,
                    (long)body["computer"]!["id"]!, (long)body["computer"]!["user_id"]!));
                if (approved is null)
                {
                    await Native(new JsonObject { ["op"] = "xaiht-set-accounts", ["identity"] = JsonSerializer.SerializeToNode(identity, RcProtocol.Json),
                        ["binding_ids"] = JsonSerializer.SerializeToNode(bindings) }, token);
                    approved = identity;
                    File.WriteAllText(Path.Combine(output, "ready.json"), JsonSerializer.Serialize(new { ready = true, bindings, computer_id = identity.ComputerId }));
                    Console.WriteLine("QA website account grants ready for " + bindings.Length + " bound accounts.");
                }
                else if (approved != identity) throw new InvalidDataException("Fixture computer identity changed.");
                relay.Receive(http, settings, identity, body["commands"] as JsonArray, token, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { lifetime.Cancel(); relay.ClearSnapshots(); }
        Console.WriteLine("QA command relay stopped.");
        return 0;
    }
}
