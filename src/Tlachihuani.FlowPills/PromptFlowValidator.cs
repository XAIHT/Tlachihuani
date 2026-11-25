// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (a pure check of a whole document; the refusal it throws is the line that matters)
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tlachihuani.FlowPills;

// Tlamatini's validate_flow (agent/services/prompt_flow_panel.py) in C#: the same limits, the
// same allowlists and the same refusal sentences, so a flow pill Tlamatini's Prompt Flow Panel
// accepts is accepted here, and one it refuses is refused with the same words.
public static partial class PromptFlowValidator
{
    public const string Format = "tlamatini-prompting-flow";
    public const int Version = 2;
    public const long MaxFileBytes = 5L * 1024 * 1024;

    private static readonly HashSet<string> Operations = new(StringComparer.Ordinal)
    {
        "prompt", "programmed_prompt", "decision", "feed_embeddings", "flush_embeddings",
        "clean_history", "user_input", "user_commentary", "terminater",
    };
    private static readonly HashSet<string> CommentColors = new(StringComparer.Ordinal) { "#fbcfe8", "#fef3c7", "#dcfce7", "#dbeafe", "#ede9fe", "#ffedd5", "#ffffff", "#e5e7eb" };
    private static readonly HashSet<string> CommentTextColors = new(StringComparer.Ordinal) { "#202938", "#000000", "#1d4ed8", "#166534", "#7e22ce", "#b91c1c", "#92400e", "#ffffff" };
    private static readonly HashSet<string> CommentFonts = new(StringComparer.Ordinal) { "Nunito", "Arial", "Verdana", "Georgia", "Times New Roman", "Courier New" };
    private static readonly HashSet<string> Comparisons = new(StringComparer.Ordinal) { "contains", "not_contains", "equals", "is_empty", "user" };

    // Terminater (the parallelogram): one action in the user's visible CMD / PowerShell terminals.
    public static readonly string[] TerminaterActions =
    [
        "run", "open", "type", "send_key", "wait", "read", "commands", "abort", "kill", "force_kill",
        "kill_all", "list", "status", "focus", "rename", "layout", "clear", "watch_user", "warn_user", "handover",
    ];
    private static readonly string[] TerminaterKinds = ["cmd", "powershell", "pwsh"];
    private static readonly string[] TerminaterTextActions = ["run", "type", "handover"];
    private static readonly (string Key, int Limit, string Default)[] TerminaterTexts =
    [
        ("terminal", 80, ""), ("title", 80, ""), ("cwd", 1000, ""), ("keys", 500, ""), ("message", 2000, ""),
        ("suggestion", 1000, ""), ("hint", 200, ""), ("which", 40, "last"), ("until", 20, ""), ("pattern", 500, ""),
        ("explanation", 1000, ""),
    ];
    private static readonly (string Key, bool Default)[] TerminaterFlags =
    [
        ("submit", true), ("secret", false), ("wait", true), ("close_after", false), ("load_profile", true),
        ("advisor", true), ("use_my_defaults", false),
    ];
    private static readonly (string Key, double Minimum, double Maximum, bool Integer, double Default)[] TerminaterNumbers =
    [
        ("typing_wpm", 20, 250, true, 50), ("wait_seconds", 1, 3600, false, 120), ("hang_seconds", 3, 3600, false, 45),
        ("user_wait_seconds", 5, 3600, false, 300), ("idle_seconds", 0.2, 600, false, 2),
    ];
    private static readonly string[] StyleKeys = ["font_family", "font_size", "text_color", "bold", "italic", "underline"];

    [GeneratedRegex(@"\A[A-Za-z0-9_-]+\z")] private static partial Regex IdPattern();
    [GeneratedRegex(@"\A\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:[Zz]|[+-]\d{2}(?::?\d{2})?)\z")] private static partial Regex IsoWithZone();

    /// <summary>Returns a fresh, allowlisted document; never trusts or mutates the input.</summary>
    public static JsonObject Validate(JsonObject payload, bool playable)
    {
        if (Choice(payload, "format", null) != Format)
            throw Fail("Open a Prompt Flow Panel .fpmt document (legacy prompt.pmt text is not a diagram).");
        double? version = payload["version"] is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? v.GetValue<double>() : null;
        if (version is not (1d or 2d)) throw Fail("Unsupported .fpmt version. This panel supports versions 1 and 2.");
        bool legacy = version == 1d;
        if (payload["nodes"] is not JsonArray nodes || nodes.Count > 500 || payload["edges"] is not JsonArray edges || edges.Count > 1000)
            throw Fail("A flow may contain up to 500 operations and 1,000 connections.");
        var result = new JsonObject
        {
            ["format"] = Format,
            ["version"] = Version,
            ["name"] = Text(payload, "name", "Untitled", "Flow name", 120),
            ["start"] = payload["start"]?.DeepClone(),
            ["max_steps"] = Number(payload.TryGetPropertyValue("max_steps", out var steps) ? steps : JsonValue.Create(500d), "Step limit", 1, 5000, integer: true),
        };

        var known = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var cleanNodes = new JsonArray();
        foreach (var raw in nodes)
        {
            if (raw is not JsonObject node) throw Fail("Every operation must be an object.");
            string id = TextValue(node["id"], "Operation ID", 80);
            if (!IdPattern().IsMatch(id) || known.ContainsKey(id))
                throw Fail("Operation IDs must be unique letters, digits, underscores or dashes.");
            string? kind = Choice(node, "type", null);
            if (legacy && kind == "user_commentary") kind = "user_input";
            if (kind is null || !Operations.Contains(kind)) throw Fail($"Unknown operation: {Repr(node["type"])}.");
            JsonObject config;
            if (!node.TryGetPropertyValue("config", out var rawConfig)) config = new JsonObject();
            else config = rawConfig as JsonObject ?? throw Fail("Operation settings must be an object.");
            string messageLabel = node["label"] is JsonValue lv && lv.GetValueKind() == JsonValueKind.String ? lv.GetValue<string>() : kind;

            var clean = new JsonObject { ["text"] = Text(config, "text", "", "Operation text") };
            if (kind == "user_commentary") Commentary(config, clean);
            if (kind is "prompt" or "programmed_prompt")
            {
                bool multiTurn = Flag(config, "multi_turn", false, "multi_turn must be true or false.");
                bool acpx = Flag(config, "acpx", false, "acpx must be true or false.");
                clean["multi_turn"] = multiTurn;
                clean["acpx"] = acpx;
                if (acpx && !multiTurn) throw Fail("ACPX requires Multi-Turn.");
                if (playable && S(clean["text"]).Trim().Length == 0) throw Fail($"{messageLabel} needs prompt text.");
            }
            if (kind == "programmed_prompt")
            {
                clean["delay_seconds"] = Number(Value(config, "delay_seconds", 0), "Delay", 0, 86400);
                clean["scheduled_at"] = Text(config, "scheduled_at", "", "Scheduled time", 60);
                ScheduledTime(S(clean["scheduled_at"]));
            }
            if (kind == "decision")
            {
                string? comparison = Choice(config, "comparison", "contains");
                if (comparison is null || !Comparisons.Contains(comparison)) throw Fail("Unknown decision comparison.");
                clean["comparison"] = comparison;
                clean["value"] = Text(config, "value", "", "Comparison value", 10000);
                clean["case_sensitive"] = Flag(config, "case_sensitive", false, "Case sensitivity must be true or false.");
                if (playable && (comparison is "contains" or "not_contains" or "equals") && S(clean["value"]).Length == 0)
                    throw Fail("Set a decision comparison value, or choose 'Output is empty'.");
            }
            if (kind == "feed_embeddings" && playable && S(clean["text"]).Trim().Length == 0)
                throw Fail("Feed embeddings needs text or {{last_output}}.");
            if (kind == "terminater") TerminaterConfig(config, messageLabel, S(clean["text"]), playable, clean);

            string label = node.TryGetPropertyValue("label", out var rawLabel) ? TextValue(rawLabel, "Operation label", 120) : Title(kind);
            var item = new JsonObject
            {
                ["id"] = id,
                ["type"] = kind,
                ["label"] = label,
                ["x"] = Number(node.TryGetPropertyValue("x", out var x) ? x : JsonValue.Create(0d), "X position", 0, 100000),
                ["y"] = Number(node.TryGetPropertyValue("y", out var y) ? y : JsonValue.Create(0d), "Y position", 0, 100000),
                ["config"] = clean,
            };
            if (legacy && Choice(node, "type", null) == "user_commentary" && label == "User Commentary") item["label"] = "User Input";
            known[id] = item;
            cleanNodes.Add(item);
        }

        var slots = new HashSet<(string, string)>();
        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        var cleanEdges = new JsonArray();
        foreach (var raw in edges)
        {
            if (raw is not JsonObject edge) throw Fail("Every connection must be an object.");
            string edgeId = TextValue(edge["id"], "Connection ID", 80);
            if (!IdPattern().IsMatch(edgeId) || edgeIds.Contains(edgeId))
                throw Fail("Connection IDs must be unique letters, digits, underscores or dashes.");
            string? source = Choice(edge, "source", null), target = Choice(edge, "target", null);
            if (source is null || target is null || !known.ContainsKey(source) || !known.ContainsKey(target))
                throw Fail("Every connection must join existing operations.");
            string? branch = Choice(edge, "branch", "next");
            if (S(known[source]["type"]) == "user_commentary" || S(known[target]["type"]) == "user_commentary")
                throw Fail("Static User Commentary assets cannot have connections.");
            bool allowed = S(known[source]["type"]) == "decision" ? branch is "yes" or "no" : branch == "next";
            if (!allowed || !slots.Add((source, branch!)))
                throw Fail("Each output accepts one connection. Decisions have separate Yes and No outputs.");
            edgeIds.Add(edgeId);
            cleanEdges.Add(new JsonObject { ["id"] = edgeId, ["source"] = source, ["target"] = target, ["branch"] = branch });
        }

        JsonNode? start = result["start"];
        if (start is not null)
        {
            if (start is not JsonValue sv || sv.GetValueKind() != JsonValueKind.String || !known.ContainsKey(sv.GetValue<string>()))
                throw Fail("The start operation does not exist.");
            if (S(known[sv.GetValue<string>()]["type"]) == "user_commentary")
                throw Fail("A static User Commentary cannot be the Start operation.");
        }
        if (playable)
        {
            if (nodes.Count == 0 || start is null) throw Fail("Add an operation and choose where the flow starts.");
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(S(start));
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                if (!reachable.Add(current)) continue;
                foreach (var edge in cleanEdges)
                    if (S(edge?["source"]) == current) pending.Push(S(edge?["target"]));
            }
            if (reachable.Count != cleanNodes.Count(node => S(node?["type"]) != "user_commentary"))
                throw Fail("Some operations are unreachable from Start. Connect them or choose another Start.");
            foreach (var node in cleanNodes)
            {
                string nodeId = S(node?["id"]);
                if (S(node?["type"]) == "decision" && !(slots.Contains((nodeId, "yes")) && slots.Contains((nodeId, "no"))))
                    throw Fail("Connect both Yes and No outputs of every decision.");
            }
        }
        result["nodes"] = cleanNodes;
        result["edges"] = cleanEdges;
        return result;
    }

    public static DateTimeOffset? ScheduledTime(string value)
    {
        if (value.Length == 0) return null;
        if (!IsoWithZone().IsMatch(value) || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var when))
            throw Fail("Scheduled time must be an ISO date with a timezone.");
        return when;
    }

    private static void Commentary(JsonObject config, JsonObject clean)
    {
        clean["width"] = Number(Value(config, "width", 320), "Comment width", 200, 2400);
        clean["height"] = Number(Value(config, "height", 200), "Comment height", 128, 2400);
        clean["font_size"] = Number(Value(config, "font_size", 16), "Comment font size", 10, 48);
        clean["color"] = Text(config, "color", "#fbcfe8", "Comment color", 20);
        clean["text_color"] = Text(config, "text_color", "#202938", "Comment text color", 20);
        clean["font_family"] = Text(config, "font_family", "Nunito", "Comment font", 80);
        clean["align"] = Text(config, "align", "left", "Comment alignment", 10);
        if (!CommentColors.Contains(S(clean["color"])) || !CommentTextColors.Contains(S(clean["text_color"]))
            || !CommentFonts.Contains(S(clean["font_family"])) || S(clean["align"]) is not ("left" or "center" or "right"))
            throw Fail("Choose a supported comment color, font and alignment.");
        foreach (var key in new[] { "bold", "italic", "underline" })
            clean[key] = Flag(config, key, false, $"Comment {key} must be true or false.");

        JsonArray runs;
        if (!config.TryGetPropertyValue("runs", out var rawRuns))
        {
            runs = new JsonArray();
            if (S(clean["text"]).Length > 0) runs.Add(new JsonObject { ["text"] = S(clean["text"]) });
        }
        else runs = rawRuns as JsonArray ?? throw Fail("Use at most 10,000 formatted text runs per comment.");
        if (runs.Count > 10000) throw Fail("Use at most 10,000 formatted text runs per comment.");
        var cleanRuns = new JsonArray();
        var joined = new StringBuilder();
        JsonObject? last = null;
        foreach (var rawRun in runs)
        {
            if (rawRun is not JsonObject run) throw Fail("A formatted text run must be an object.");
            var part = new JsonObject { ["text"] = TextValue(run["text"], "Comment run text") };
            foreach (var key in StyleKeys)
                part[key] = (run.TryGetPropertyValue(key, out var style) ? style : clean[key])?.DeepClone();
            Number(part["font_size"], "Comment run font size", 10, 48);
            if (!CommentFonts.Contains(TextValue(part["font_family"], "Comment run font", 80))
                || !CommentTextColors.Contains(TextValue(part["text_color"], "Comment run color", 20)))
                throw Fail("Choose a supported comment font and text color.");
            foreach (var key in new[] { "bold", "italic", "underline" })
                if (part[key] is not JsonValue flag || flag.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
                    throw Fail("Comment run style switches must be true or false.");
            string text = S(part["text"]);
            if (text.Length == 0) continue;
            joined.Append(text);
            if (last is not null && StyleKeys.All(key => Same(last[key], part[key]))) last["text"] = S(last["text"]) + text;
            else { cleanRuns.Add(part); last = part; }
        }
        if (joined.ToString() != S(clean["text"])) throw Fail("Comment runs must contain exactly the comment text.");
        clean["runs"] = cleanRuns;
    }

    private static void TerminaterConfig(JsonObject config, string label, string text, bool playable, JsonObject output)
    {
        string? action = Choice(config, "action", "run");
        if (action is null || Array.IndexOf(TerminaterActions, action) < 0) throw Fail("Unknown Terminater action.");
        output["action"] = action;
        string? shell = Choice(config, "kind", "powershell");
        if (shell is null || Array.IndexOf(TerminaterKinds, shell) < 0) throw Fail("Choose CMD, PowerShell or PowerShell 7.");
        output["kind"] = shell;
        foreach (var (key, limit, fallback) in TerminaterTexts) output[key] = Text(config, key, fallback, "Terminater " + key, limit);
        string? severity = Choice(config, "severity", "warning");
        if (severity is not ("info" or "hint" or "warning" or "danger")) throw Fail("Unknown Terminater severity.");
        output["severity"] = severity;
        string? layout = Choice(config, "layout", ""), area = Choice(config, "area", "");
        if (layout is not ("" or "tabs" or "tile" or "tile-h" or "tile-v" or "grid") || area is not ("" or "dock" or "full"))
            throw Fail("Unknown Terminater layout or area.");
        output["layout"] = layout;
        output["area"] = area;
        foreach (var (key, fallback) in TerminaterFlags) output[key] = Flag(config, key, fallback, $"Terminater {key} must be true or false.");
        foreach (var (key, minimum, maximum, integer, fallback) in TerminaterNumbers)
            output[key] = Number(Value(config, key, fallback), "Terminater " + key, minimum, maximum, integer);
        if (playable && Array.IndexOf(TerminaterTextActions, action) >= 0 && text.Trim().Length == 0) throw Fail($"{label} needs a command to type.");
        if (playable && action == "send_key" && S(output["keys"]).Trim().Length == 0) throw Fail($"{label} needs the keys to press.");
    }

    internal static string TextValue(JsonNode? node, string label, int maximum = 100_000)
    {
        if (node is JsonValue value && value.GetValueKind() == JsonValueKind.String)
        {
            string text = value.GetValue<string>();
            if (text.Length <= maximum || text.EnumerateRunes().Count() <= maximum) return text;
        }
        throw Fail($"{label} must be text of at most {maximum.ToString("N0", CultureInfo.InvariantCulture)} characters.");
    }

    private static string Text(JsonObject source, string key, string fallback, string label, int maximum = 100_000)
        => TextValue(source.TryGetPropertyValue(key, out var node) ? node : JsonValue.Create(fallback), label, maximum);

    private static JsonNode? Value(JsonObject source, string key, double fallback)
        => source.TryGetPropertyValue(key, out var node) ? node : JsonValue.Create(fallback);

    private static double Number(JsonNode? node, string label, double minimum, double maximum, bool integer = false)
    {
        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.Number) throw Fail($"{label} must be a number.");
        double number = value.GetValue<double>();
        if (!double.IsFinite(number) || number < minimum || number > maximum || (integer && number != Math.Floor(number)))
            throw Fail($"{label} must be between {Show(minimum)} and {Show(maximum)}.");
        return number;
    }

    private static bool Flag(JsonObject source, string key, bool fallback, string message)
    {
        if (!source.TryGetPropertyValue(key, out var node)) return fallback;
        if (node is JsonValue value && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False) return value.GetValue<bool>();
        throw Fail(message);
    }

    private static string? Choice(JsonObject source, string key, string? fallback)
        => !source.TryGetPropertyValue(key, out var node) ? fallback
            : node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    private static bool Same(JsonNode? a, JsonNode? b)
    {
        if (a is not JsonValue x || b is not JsonValue y) return a is null && b is null;
        var kind = x.GetValueKind();
        if (kind != y.GetValueKind()) return false;
        return kind switch
        {
            JsonValueKind.Number => x.GetValue<double>() == y.GetValue<double>(),
            JsonValueKind.String => x.GetValue<string>() == y.GetValue<string>(),
            _ => true,
        };
    }

    private static string Repr(JsonNode? node) => node switch
    {
        null => "None",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => "'" + v.GetValue<string>() + "'",
        JsonValue v when v.GetValueKind() == JsonValueKind.True => "True",
        JsonValue v when v.GetValueKind() == JsonValueKind.False => "False",
        _ => node.ToJsonString(),
    };

    private static string Title(string kind)
        => string.Join(' ', kind.Split('_').Select(word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]));

    private static string Show(double number)
        => number == Math.Floor(number) ? ((long)number).ToString(CultureInfo.InvariantCulture) : number.ToString("R", CultureInfo.InvariantCulture);

    internal static string S(JsonNode? node)
        => node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : "";

    private static FlowPillsException Fail(string message) => new("invalid_fpmt", message);
}

// How a flow pill reads an answer: Tlamatini's _plain, _equals and decide.
public static partial class PromptDecision
{
    // What a Prompt step adds to its request when the next operation is an "equals" Decision.
    public const string VerdictFirstHint = "\n\n(This answer is read by an automatic Decision. Put your short answer - "
        + "exactly the word or phrase this request asks for - alone on the first line, then any explanation.)";
    private const string EdgePunctuation = " \t\r\n.,;:!?\"'()[]{}—–-";

    [GeneratedRegex(@"\bEND-RESPONSE\b")] private static partial Regex EndResponse();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex HtmlTag();
    [GeneratedRegex(@"[#*_`>~|]+")] private static partial Regex MarkdownMarks();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();

    public static string Expand(string text, string lastOutput) => text.Replace("{{last_output}}", lastOutput, StringComparison.Ordinal);

    // An answer as a reader sees it: no END-RESPONSE, HTML tags or Markdown marks.
    public static string Plain(string text)
    {
        text = HtmlTag().Replace(EndResponse().Replace(text, " "), " ");
        text = MarkdownMarks().Replace(text.Replace("&nbsp;", " ", StringComparison.Ordinal).Replace("&amp;", "&", StringComparison.Ordinal), " ");
        return Spaces().Replace(text, " ").Trim().Trim(EdgePunctuation.ToCharArray());
    }

    // True when the answer IS the value, or opens with it as its verdict: "## YES - the toolkit is
    // current" equals "YES", while "Yesterday" does not equal "yes".
    public static bool EqualsVerdict(string output, string value)
    {
        if (output.Trim() == value.Trim()) return true;
        string answer = Plain(output), wanted = Plain(value);
        if (wanted.Length == 0) return false;
        return answer == wanted || Regex.IsMatch(answer, @"\A" + Regex.Escape(wanted) + @"(?!\w)");
    }

    public static bool Decide(JsonObject config, string output)
    {
        string value = PromptFlowValidator.S(config["value"]);
        if (!(config["case_sensitive"] is JsonValue sensitive && sensitive.GetValueKind() == JsonValueKind.True))
        {
            output = output.ToLowerInvariant();
            value = value.ToLowerInvariant();
        }
        return PromptFlowValidator.S(config["comparison"]) switch
        {
            "contains" => output.Contains(value, StringComparison.Ordinal),
            "not_contains" => !output.Contains(value, StringComparison.Ordinal),
            "equals" => EqualsVerdict(output, value),
            "is_empty" => output.Trim().Length == 0,
            _ => false,
        };
    }
}
