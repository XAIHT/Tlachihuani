// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (a 500-node flow is read node by node; the window writes one line for every file it opens)
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tlachihuani.FlowPills;

public enum FlowKind { Agents, Prompts }

// One node as it was drawn: an agent of a .flw (Tlamatini's Agentic Control Panel), or an
// operation or a comment of a .fpmt (its Prompt Flow Panel). Coordinates are the panel's own
// unzoomed pixels, so the drawing here keeps the layout the author made.
public sealed class FlowNode
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Type { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required JsonObject Config { get; init; }
    public string PoolName { get; init; } = "";
    public string Purpose { get; init; } = "";
    public bool IsComment => Type == "user_commentary";
}

// A wire. Agent flows use the ACP's connector slots; flow pills use next, yes and no.
public sealed record FlowEdge(string Id, string Source, string Target, int OutputSlot, int InputSlot, string Branch);

public sealed class FlowDocument
{
    public const long MaxAgentFlowBytes = 16L * 1024 * 1024;

    public required string FilePath { get; init; }
    public required string Name { get; init; }
    public required FlowKind Kind { get; init; }
    public required IReadOnlyList<FlowNode> Nodes { get; init; }
    public required IReadOnlyList<FlowEdge> Edges { get; init; }
    public required string Text { get; init; }          // exactly what was opened; a run uses this copy
    public string Start { get; init; } = "";
    public int MaxSteps { get; init; } = 500;

    public string KindLabel => Kind == FlowKind.Agents ? "Agentic flow (.flw)" : "Flow pill (.fpmt)";

    public FlowNode? Find(string id)
    {
        foreach (var node in Nodes) if (node.Id == id) return node;
        return null;
    }

    public static FlowDocument Load(string path)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new FlowPillsException("bad_path", $"{path} is not a valid file path."); }
        var info = new FileInfo(full);
        if (!info.Exists) throw new FlowPillsException("missing", $"{full} does not exist.");
        bool pill = info.Extension.Equals(".fpmt", StringComparison.OrdinalIgnoreCase);
        long limit = pill ? PromptFlowValidator.MaxFileBytes : MaxAgentFlowBytes;
        if (info.Length > limit)
            throw new FlowPillsException("too_large", $"{info.Name} has {info.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes; "
                + $"a {(pill ? ".fpmt" : ".flw")} file may have at most {limit.ToString("N0", CultureInfo.InvariantCulture)}.");
        string text;
        try { text = File.ReadAllText(full, new UTF8Encoding(false, true)); }
        catch (DecoderFallbackException) { throw new FlowPillsException("not_utf8", $"{info.Name} is not UTF-8 text."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new FlowPillsException("unreadable", $"{info.Name} could not be read: {ex.Message}"); }
        return Parse(full, text);
    }

    public static FlowDocument Parse(string path, string text)
    {
        string name = Path.GetFileName(path);
        JsonNode? root;
        try { root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { MaxDepth = 256 }); }
        catch (JsonException ex) { throw new FlowPillsException("invalid_json", $"{name} is not valid JSON: {ex.Message}"); }
        if (root is not JsonObject document) throw new FlowPillsException("invalid_json", $"{name} does not hold a JSON object.");
        bool pill = Path.GetExtension(path).Equals(".fpmt", StringComparison.OrdinalIgnoreCase)
            || PromptFlowValidator.S(document["format"]) == PromptFlowValidator.Format;
        return pill ? Pill(path, text, document) : Agents(path, text, document);
    }

    // A .flw as Tlamatini's flow compiler reads it: node ids "executer-2", pool folders
    // "executer_2", connections by id (or by index in older files) with their connector slots.
    private static FlowDocument Agents(string path, string text, JsonObject document)
    {
        string name = Path.GetFileName(path);
        if (document["nodes"] is not JsonArray rawNodes)
            throw new FlowPillsException("invalid_flw", $"{name} is not a Tlamatini .flw document (it has no nodes list).");
        var nodes = new List<FlowNode>();
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in rawNodes)
        {
            if (item is not JsonObject raw) continue;
            string text0 = First(Str(raw["text"]), Str(raw["agentName"]), Str(raw["agent_name"]));
            string id = Str(raw["id"]);
            if (id.Length == 0)
            {
                string type = AgentNames.Normalize(text0);
                if (AgentNames.IsSingleton(type)) id = type.Replace('_', '-');
                else
                {
                    counters[type] = counters.GetValueOrDefault(type) + 1;
                    id = type.Replace('_', '-') + "-" + counters[type].ToString(CultureInfo.InvariantCulture);
                }
            }
            var (agentType, pool) = AgentNames.FromCanvasId(id, text0);
            var config = (raw["configData"] ?? raw["config"]) as JsonObject;
            nodes.Add(new FlowNode
            {
                Id = id,
                Label = CanvasLabel(text0.Length > 0 ? text0 : agentType, id),
                Type = agentType,
                PoolName = pool,
                X = Pixels(raw["left"]),
                Y = Pixels(raw["top"]),
                Width = 100,
                Height = 60,
                Config = config is null ? new JsonObject() : (JsonObject)config.DeepClone(),
                Purpose = Str(raw["agentPurpose"]),
            });
        }
        if (nodes.Count == 0) throw new FlowPillsException("empty_flow", $"{name} has no agents.");
        var edges = new List<FlowEdge>();
        if (document["connections"] is JsonArray rawEdges)
        {
            int index = 0;
            foreach (var item in rawEdges)
            {
                if (item is not JsonObject raw) continue;
                string source = Str(raw["sourceId"]), target = Str(raw["targetId"]);
                int sourceIndex = Whole(raw["sourceIndex"], -1), targetIndex = Whole(raw["targetIndex"], -1);
                if (source.Length == 0 && sourceIndex >= 0 && sourceIndex < nodes.Count) source = nodes[sourceIndex].Id;
                if (target.Length == 0 && targetIndex >= 0 && targetIndex < nodes.Count) target = nodes[targetIndex].Id;
                if (source.Length == 0 || target.Length == 0) continue;
                edges.Add(new FlowEdge("c" + (index++).ToString(CultureInfo.InvariantCulture), source, target,
                    Whole(raw["outputSlot"], 0), Whole(raw["inputSlot"], 0), ""));
            }
        }
        return new FlowDocument
        {
            FilePath = path, Name = Path.GetFileNameWithoutExtension(path), Kind = FlowKind.Agents,
            Nodes = nodes, Edges = edges, Text = text,
        };
    }

    // A .fpmt is read through the Prompt Flow Panel's own validator: what opens here is exactly
    // what Tlamatini would open, with version 1 documents migrated the same way.
    private static FlowDocument Pill(string path, string text, JsonObject document)
    {
        JsonObject flow = PromptFlowValidator.Validate(document, playable: false);
        var nodes = new List<FlowNode>();
        foreach (var item in (JsonArray)flow["nodes"]!)
        {
            var node = (JsonObject)item!;
            var config = (JsonObject)node["config"]!;
            string type = Str(node["type"]);
            bool comment = type == "user_commentary";
            nodes.Add(new FlowNode
            {
                Id = Str(node["id"]),
                Label = Str(node["label"]),
                Type = type,
                X = Number(node["x"]),
                Y = Number(node["y"]),
                Width = comment ? Number(config["width"]) : 200,
                Height = comment ? Number(config["height"]) : 128,
                Config = config,
            });
        }
        var edges = new List<FlowEdge>();
        foreach (var item in (JsonArray)flow["edges"]!)
        {
            var edge = (JsonObject)item!;
            edges.Add(new FlowEdge(Str(edge["id"]), Str(edge["source"]), Str(edge["target"]), 0, 0, Str(edge["branch"])));
        }
        return new FlowDocument
        {
            FilePath = path, Name = Path.GetFileNameWithoutExtension(path), Kind = FlowKind.Prompts,
            Nodes = nodes, Edges = edges, Text = text, Start = Str(flow["start"]), MaxSteps = (int)Number(flow["max_steps"]),
        };
    }

    // The ACP writes "Crawler" and draws "Crawler (2)" for the node crawler-2.
    private static string CanvasLabel(string text, string id)
    {
        int dash = id.LastIndexOf('-');
        string cardinal = dash > 0 && dash < id.Length - 1 && id[(dash + 1)..].All(char.IsAsciiDigit) ? id[(dash + 1)..] : "";
        if (cardinal.Length == 0 || text.EndsWith(")", StringComparison.Ordinal)) return text;
        return $"{text} ({cardinal})";
    }

    private static string First(params string[] values)
    {
        foreach (var value in values) if (value.Length > 0) return value;
        return "";
    }

    // Python's str() of a JSON scalar: text as it is, numbers as written.
    private static string Str(JsonNode? node) => node is JsonValue value ? value.GetValueKind() switch
    {
        JsonValueKind.String => value.GetValue<string>(),
        JsonValueKind.Number => value.ToJsonString(),
        _ => "",
    } : "";

    private static double Number(JsonNode? node)
        => node is JsonValue value && value.GetValueKind() == JsonValueKind.Number ? value.GetValue<double>() : 0;

    private static int Whole(JsonNode? node, int fallback)
    {
        if (node is not JsonValue value) return fallback;
        if (value.GetValueKind() == JsonValueKind.Number && value.TryGetValue(out double number) && number == Math.Floor(number)
            && number is >= int.MinValue and <= int.MaxValue) return (int)number;
        return value.GetValueKind() == JsonValueKind.String && int.TryParse(value.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed : fallback;
    }

    // "120px", "120" or 120.
    private static double Pixels(JsonNode? node)
    {
        if (node is not JsonValue value) return 0;
        if (value.GetValueKind() == JsonValueKind.Number) return Math.Max(0, value.GetValue<double>());
        if (value.GetValueKind() != JsonValueKind.String) return 0;
        string text = value.GetValue<string>().Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase)) text = text[..^2];
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && double.IsFinite(parsed) ? Math.Max(0, parsed) : 0;
    }
}
