// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (requests carry the user's prompts, answers and tokens; the run writes one line per call)
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tlachihuani.FlowPills;

public readonly record struct ChatTurn(string Role, string Content);

// A flow pill's model calls, made with what Tlamatini itself is configured with: the Ollama
// server, token and models of its config.json (ollama_base_url, ollama_token, chained-model,
// embeding-model, llm_client_timeout_seconds), and each model's own default settings. The
// token goes only to that server, in the Authorization header, and is never written anywhere.
public sealed class OllamaGateway
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly string token;

    private OllamaGateway(Uri server, string token, string chatModel, string embeddingModel, TimeSpan deadline)
    {
        Server = server;
        this.token = token;
        ChatModel = chatModel;
        EmbeddingModel = embeddingModel;
        Deadline = deadline;
    }

    public Uri Server { get; }
    public string ChatModel { get; }
    public string EmbeddingModel { get; }
    public TimeSpan Deadline { get; }

    public static OllamaGateway FromConfig(string configPath)
    {
        JsonObject config;
        try
        {
            var info = new FileInfo(configPath);
            if (!info.Exists) throw new FlowPillsException("config_missing", $"Tlamatini's {configPath} was not found, so this flow pill's models are unknown.");
            if (info.Length > 8 * 1024 * 1024) throw new FlowPillsException("config_invalid", $"{configPath} is larger than 8 MB.");
            config = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject
                ?? throw new FlowPillsException("config_invalid", $"{configPath} does not hold a JSON object.");
        }
        catch (JsonException ex) { throw new FlowPillsException("config_invalid", $"{configPath} is not valid JSON: {ex.Message}"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new FlowPillsException("config_unreadable", $"{configPath} could not be read: {ex.Message}"); }

        string url = Str(config["ollama_base_url"]);
        if (url.Length == 0) url = "http://127.0.0.1:11434";
        if (!Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var server) || server.Scheme is not ("http" or "https"))
            throw new FlowPillsException("config_invalid", $"ollama_base_url in {configPath} is not an http or https address.");
        string secret = Str(config["ollama_token"]).Trim();
        if (secret.StartsWith('<') || secret.Any(char.IsControl)) secret = "";   // a push-able placeholder is no token
        string chat = Str(config["chained-model"]);
        if (chat.Length == 0) chat = Str(config["unified_agent_model"]);
        if (chat.Length == 0) throw new FlowPillsException("model_missing", $"No chat model is configured in {configPath} (chained-model).");
        double seconds = config["llm_client_timeout_seconds"] is JsonValue limit && limit.GetValueKind() == JsonValueKind.Number ? limit.GetValue<double>() : 600;
        return new OllamaGateway(server, secret, chat, Str(config["embeding-model"]), TimeSpan.FromSeconds(Math.Clamp(seconds, 30, 7200)));
    }

    public string Chat(IReadOnlyList<ChatTurn> messages, CancellationToken cancel)
    {
        var list = new JsonArray();
        foreach (var message in messages) list.Add(new JsonObject { ["role"] = message.Role, ["content"] = message.Content });
        var reply = Post("api/chat", new JsonObject { ["model"] = ChatModel, ["stream"] = false, ["messages"] = list }, cancel);
        return Str((reply["message"] as JsonObject)?["content"]);
    }

    public float[][] Embed(IReadOnlyList<string> inputs, CancellationToken cancel)
    {
        if (EmbeddingModel.Length == 0)
            throw new FlowPillsException("model_missing", "No embedding model is configured in Tlamatini's config.json (embeding-model).");
        var list = new JsonArray();
        foreach (var input in inputs) list.Add((JsonNode?)JsonValue.Create(input));
        var reply = Post("api/embed", new JsonObject { ["model"] = EmbeddingModel, ["input"] = list }, cancel);
        if (reply["embeddings"] is not JsonArray rows || rows.Count != inputs.Count)
            throw new FlowPillsException("model_error", $"{EmbeddingModel} returned no embeddings.");
        var vectors = new float[rows.Count][];
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] is not JsonArray row) throw new FlowPillsException("model_error", $"{EmbeddingModel} returned an unreadable embedding.");
            var vector = new float[row.Count];
            for (int j = 0; j < row.Count; j++)
                vector[j] = row[j] is JsonValue number && number.GetValueKind() == JsonValueKind.Number ? (float)number.GetValue<double>() : 0f;
            vectors[i] = vector;
        }
        return vectors;
    }

    private JsonObject Post(string path, JsonObject body, CancellationToken cancel)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(Deadline);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Server, path))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (token.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage response;
        try { response = Http.Send(request, HttpCompletionOption.ResponseContentRead, deadline.Token); }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        { throw new FlowPillsException("model_timeout", $"{Server.Host} did not answer within {Deadline.TotalSeconds:0} seconds (llm_client_timeout_seconds)."); }
        catch (HttpRequestException ex) { throw new FlowPillsException("model_unreachable", $"Ollama is not reachable at {Server}: {ex.Message}"); }
        using (response)
        {
            string text;
            using (var stream = response.Content.ReadAsStream(deadline.Token))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                text = reader.ReadToEnd();
            if (!response.IsSuccessStatusCode)
            {
                string detail = (text.Length > 400 ? text[..400] : text).Trim();
                throw new FlowPillsException("model_error", $"Ollama answered {(int)response.StatusCode} {response.ReasonPhrase}: {detail}");
            }
            try
            {
                return JsonNode.Parse(text) as JsonObject ?? throw new FlowPillsException("model_error", "Ollama's answer is not a JSON object.");
            }
            catch (JsonException ex) { throw new FlowPillsException("model_error", $"Ollama's answer is not JSON: {ex.Message}"); }
        }
    }

    private static string Str(JsonNode? node)
        => node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : "";
}

// Terminater steps type into Tlamatini's visible terminals through its terminal hub, exactly as
// Tlamatini's Terminater agent does: a loopback address and the token Tlamatini publishes in
// its Temp folder (.terminater_hub.json). Without a running Tlamatini, the step says so.
public static class TerminaterBridge
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static JsonObject Run(string tlamatiniTemp, JsonObject parameters, TimeSpan deadline, CancellationToken cancel)
    {
        string file = Path.Combine(tlamatiniTemp, ".terminater_hub.json");
        JsonObject hub;
        try
        {
            var info = new FileInfo(file);
            hub = info.Exists && info.Length < 64 * 1024 ? JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? new JsonObject() : new JsonObject();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { hub = new JsonObject(); }
        string url = Str(hub["url"]), token = Str(hub["token"]);
        if (url.Length == 0 || token.Length == 0)
            throw new FlowPillsException("terminater_unavailable",
                "Terminater types into Tlamatini's visible terminals, and Tlamatini's terminal hub was not found. Start Tlamatini, then run this flow pill again.");
        if (!Uri.TryCreate(url.TrimEnd('/') + "/agent/terminater/api/", UriKind.Absolute, out var endpoint) || endpoint.Scheme != "http" || !endpoint.IsLoopback)
            throw new FlowPillsException("terminater_unavailable", "Tlamatini's terminal hub address is not a local http address, so nothing was sent.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(deadline);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(parameters.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-Terminater-Token", token);
        try
        {
            using var response = Http.Send(request, HttpCompletionOption.ResponseContentRead, limit.Token);
            using var stream = response.Content.ReadAsStream(limit.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return JsonNode.Parse(reader.ReadToEnd()) as JsonObject
                ?? throw new FlowPillsException("terminater_error", "Tlamatini's terminal hub gave an empty answer.");
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        { throw new FlowPillsException("terminater_timeout", $"Tlamatini's terminal hub did not finish within {deadline.TotalSeconds:0} seconds."); }
        catch (HttpRequestException ex)
        { throw new FlowPillsException("terminater_unavailable", $"Tlamatini's terminal hub did not answer ({ex.Message}). Is Tlamatini running?"); }
        catch (JsonException ex)
        { throw new FlowPillsException("terminater_error", $"Tlamatini's terminal hub answered something unreadable: {ex.Message}"); }
    }

    private static string Str(JsonNode? node)
        => node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : "";
}
