// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (asked for every node on every repaint)
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tlachihuani.FlowPills;

/// <summary>One gradient stop: an ARGB color at a position from 0 to 1.</summary>
public readonly record struct GradientStop(uint Argb, float Position);

/// <summary>How the Agentic Control Panel paints one agent: its gradient and its text color.</summary>
public sealed record AgentLook(uint Fill, float Angle, IReadOnlyList<GradientStop> Stops, uint Text);

// The agent colors of Tlamatini's Agentic Control Panel (agentic_control_panel.css and the
// classMap of acp-canvas-core.js), extracted into acp-palette.json, so a flow looks here as it
// looked where it was drawn. An agent the palette does not know gets the ACP's default blue.
public sealed class AcpPalette
{
    private static readonly Lazy<AcpPalette> shared = new(Load);
    public static AcpPalette Shared => shared.Value;
    public static AgentLook Default { get; } = new(0xFF7289DA, 135, [new(0xFF7289DA, 0), new(0xFF7289DA, 1)], 0xFFFFFFFF);

    private readonly Dictionary<string, string> classMap = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AgentLook> looks = new(StringComparer.Ordinal);
    private readonly HashSet<string> neverStart, noInputs, twoInputs;

    private AcpPalette(JsonObject root)
    {
        if (root["classMap"] is JsonObject map)
            foreach (var (key, value) in map)
                if (Str(value) is { Length: > 0 } cls) classMap[key] = cls;
        if (root["classes"] is JsonObject classes)
            foreach (var (cls, value) in classes)
                if (value is JsonObject spec && Parse(spec) is { } look) looks[cls] = look;
        neverStart = Set(root["neverStartOthers"]);
        noInputs = Set(root["withoutInputs"]);
        twoInputs = Set(root["twoInputs"]);
    }

    public int KnownAgents => classMap.Count;
    public IReadOnlyDictionary<string, string> ClassMap => classMap;
    public bool HasClass(string cls) => looks.ContainsKey(cls);

    public AgentLook LookFor(string agent)
    {
        string key = AgentNames.LookKey(agent);
        if (classMap.TryGetValue(key, out var cls) && looks.TryGetValue(cls, out var look)) return look;
        return looks.TryGetValue(key + "-agent", out var direct) ? direct : Default;
    }

    // Cleaner, Emailer, Stopper, the monitors... keep an output connector but never start the
    // agent wired to it; the ACP draws that connector grey.
    public bool NeverStartsOthers(string agent) => neverStart.Contains(AgentNames.LookKey(agent));
    public int Inputs(string agent)
    {
        string key = AgentNames.LookKey(agent);
        return noInputs.Contains(key) ? 0 : twoInputs.Contains(key) ? 2 : 1;
    }
    public static int Outputs(string agent) => AgentNames.LookKey(agent) switch
    {
        "flowcreator" or "flowhypervisor" => 0,
        "asker" or "forker" or "counter" => 2,
        _ => 1
    };
    public static (string First, string Second) OutputLabels(string agent)
        => AgentNames.LookKey(agent) == "counter" ? ("L", "G") : ("A", "B");

    private static AcpPalette Load()
    {
        using var stream = typeof(AcpPalette).Assembly.GetManifestResourceStream("Tlachihuani.FlowPills.acp-palette.json")
            ?? throw new InvalidOperationException("acp-palette.json is not embedded in Tlachihuani.FlowPills");
        return new AcpPalette(JsonNode.Parse(stream) as JsonObject ?? throw new InvalidOperationException("acp-palette.json is not a JSON object"));
    }

    private static AgentLook? Parse(JsonObject spec)
    {
        if (!TryColor(Str(spec["fill"]), out uint fill)) return null;
        float angle = spec["angle"] is JsonValue a && a.GetValueKind() == JsonValueKind.Number ? (float)a.GetValue<double>() : 135f;
        var stops = new List<GradientStop>();
        if (spec["stops"] is JsonArray array)
            foreach (var item in array)
                if (item is JsonArray pair && pair.Count == 2 && TryColor(Str(pair[0]), out uint color)
                    && pair[1] is JsonValue p && p.GetValueKind() == JsonValueKind.Number)
                    stops.Add(new(color, (float)Math.Clamp(p.GetValue<double>(), 0, 1)));
        if (stops.Count == 0) stops.AddRange([new(fill, 0), new(fill, 1)]);
        else if (stops.Count == 1) stops.Add(new(stops[0].Argb, 1));
        return new AgentLook(fill, angle, stops, TryColor(Str(spec["text"]), out uint ink) ? ink : 0xFFFFFFFF);
    }

    // #RRGGBB or #AARRGGBB, as acp-palette.json writes them.
    internal static bool TryColor(string text, out uint argb)
    {
        argb = 0;
        if (text.Length is not (7 or 9) || text[0] != '#'
            || !uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value)) return false;
        argb = text.Length == 7 ? 0xFF000000 | value : value;
        return true;
    }

    private static HashSet<string> Set(JsonNode? node)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (node is JsonArray array) foreach (var item in array) if (Str(item) is { Length: > 0 } text) set.Add(text);
        return set;
    }

    private static string Str(JsonNode? node)
        => node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : "";
}

// Tlamatini's agent names, the same three ways its flow compiler reads them.
public static partial class AgentNames
{
    [GeneratedRegex(@"\s*\(\d+\)\s*$")] private static partial Regex Cardinal();
    [GeneratedRegex(@"[^A-Za-z0-9_]+")] private static partial Regex NotWord();
    [GeneratedRegex(@"_+")] private static partial Regex Underscores();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();

    // "Monitor Netstat (2)" -> "monitor_netstat": normalize_agent_type of flowpills_compile.py.
    public static string Normalize(string value)
    {
        string text = Cardinal().Replace((value ?? "").Trim(), "");
        if (text.Length == 0) return "";
        text = NotWord().Replace(text.Replace('-', '_').Replace(' ', '_'), "_");
        return Underscores().Replace(text, "_").Trim('_').ToLowerInvariant();
    }

    public static bool IsSingleton(string agentType) => agentType is "flowcreator" or "flowhypervisor";

    // "executer-2" -> ("executer", "executer_2"); "flowhypervisor" -> ("flowhypervisor", "flowhypervisor").
    public static (string Type, string Pool) FromCanvasId(string id, string label)
    {
        var parts = (id ?? "").Split('-', StringSplitOptions.RemoveEmptyEntries).ToList();
        string? cardinal = parts.Count > 1 && parts[^1].All(char.IsAsciiDigit) ? parts[^1] : null;
        if (cardinal is not null) parts.RemoveAt(parts.Count - 1);
        string type = Normalize(string.Join('_', parts));
        if (type.Length == 0) type = Normalize(label);
        return (type, cardinal is null ? type : type + "_" + cardinal);
    }

    // The ACP classMap key: "Monitor Netstat (2)" -> "monitor-netstat", "video_analyzer" -> "video-analyzer".
    public static string LookKey(string name)
        => Spaces().Replace(Cardinal().Replace((name ?? "").Trim(), "").ToLowerInvariant(), "-").Replace('_', '-');
}
