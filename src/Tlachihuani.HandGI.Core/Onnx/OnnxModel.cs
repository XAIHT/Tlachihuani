// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
namespace HandGI.Core.Onnx;

public sealed record OnnxValueInfo(string Name, int ElemType, long[] Dims)
{
    public override string ToString() => $"{Name} [{string.Join(",", Dims.Select(d => d < 0 ? "?" : d.ToString()))}] type {ElemType}";
}

/// <summary>Reads what HandGI needs from an ONNX file: IR version, opset, inputs, outputs and op counts.</summary>
public sealed class OnnxModelInfo
{
    public long IrVersion { get; private init; }
    public long DefaultOpset { get; private init; }
    public string Producer { get; private init; } = "";
    public IReadOnlyList<OnnxValueInfo> Inputs { get; private init; } = Array.Empty<OnnxValueInfo>();
    public IReadOnlyList<OnnxValueInfo> Outputs { get; private init; } = Array.Empty<OnnxValueInfo>();
    public IReadOnlyDictionary<string, int> Ops { get; private init; } = new Dictionary<string, int>();
    public int InitializerCount { get; private init; }

    public static OnnxModelInfo Parse(ReadOnlyMemory<byte> model)
    {
        long ir = 0, opset = 0;
        string producer = "";
        ReadOnlyMemory<byte> graph = default;
        var reader = new ProtoReader(model);
        while (reader.Next(out var f))
        {
            switch (f.Number)
            {
                case 1 when f.Wire == 0:
                    ir = (long)f.Varint;
                    break;
                case 2 when f.Wire == 2:
                    producer = ProtoReader.Utf8(f.Payload);
                    break;
                case 7 when f.Wire == 2:
                    graph = f.Payload;
                    break;
                case 8 when f.Wire == 2:
                {
                    string domain = "";
                    long version = 0;
                    var r = new ProtoReader(f.Payload);
                    while (r.Next(out var o))
                    {
                        if (o.Number == 1 && o.Wire == 2) domain = ProtoReader.Utf8(o.Payload);
                        else if (o.Number == 2 && o.Wire == 0) version = (long)o.Varint;
                    }
                    if (domain is "" or "ai.onnx") opset = version;
                    break;
                }
            }
        }

        if (graph.IsEmpty) throw new InvalidDataException("The ONNX file has no graph.");

        var initializers = new HashSet<string>(StringComparer.Ordinal);
        var inputs = new List<OnnxValueInfo>();
        var outputs = new List<OnnxValueInfo>();
        var ops = new Dictionary<string, int>(StringComparer.Ordinal);
        var gr = new ProtoReader(graph);
        while (gr.Next(out var f))
        {
            if (f.Number == 5 && f.Wire == 2)
            {
                var r = new ProtoReader(f.Payload);
                while (r.Next(out var t))
                {
                    if (t.Number == 8 && t.Wire == 2) initializers.Add(ProtoReader.Utf8(t.Payload));
                }
            }
        }

        gr = new ProtoReader(graph);
        while (gr.Next(out var f))
        {
            if (f.Wire != 2) continue;
            switch (f.Number)
            {
                case 1:
                {
                    var r = new ProtoReader(f.Payload);
                    while (r.Next(out var n))
                    {
                        if (n.Number == 4 && n.Wire == 2)
                        {
                            string op = ProtoReader.Utf8(n.Payload);
                            ops[op] = ops.TryGetValue(op, out int c) ? c + 1 : 1;
                        }
                    }
                    break;
                }
                case 11:
                {
                    var vi = ParseValueInfo(f.Payload);
                    if (!initializers.Contains(vi.Name)) inputs.Add(vi);
                    break;
                }
                case 12:
                    outputs.Add(ParseValueInfo(f.Payload));
                    break;
            }
        }

        return new OnnxModelInfo
        {
            IrVersion = ir,
            DefaultOpset = opset,
            Producer = producer,
            Inputs = inputs,
            Outputs = outputs,
            Ops = ops,
            InitializerCount = initializers.Count,
        };
    }

    internal static OnnxValueInfo ParseValueInfo(ReadOnlyMemory<byte> payload)
    {
        string name = "";
        int elem = 0;
        var dims = new List<long>();
        var r = new ProtoReader(payload);
        while (r.Next(out var f))
        {
            if (f.Number == 1 && f.Wire == 2) name = ProtoReader.Utf8(f.Payload);
            else if (f.Number == 2 && f.Wire == 2)
            {
                var tr = new ProtoReader(f.Payload);
                while (tr.Next(out var t))
                {
                    if (t.Number != 1 || t.Wire != 2) continue; // tensor_type
                    var ttr = new ProtoReader(t.Payload);
                    while (ttr.Next(out var tt))
                    {
                        if (tt.Number == 1 && tt.Wire == 0) elem = (int)tt.Varint;
                        else if (tt.Number == 2 && tt.Wire == 2)
                        {
                            var sr = new ProtoReader(tt.Payload);
                            while (sr.Next(out var dim))
                            {
                                if (dim.Number != 1 || dim.Wire != 2) continue;
                                long value = -1;
                                var dr = new ProtoReader(dim.Payload);
                                while (dr.Next(out var dv))
                                {
                                    if (dv.Number == 1 && dv.Wire == 0) value = (long)dv.Varint;
                                }
                                dims.Add(value);
                            }
                        }
                    }
                }
            }
        }
        return new OnnxValueInfo(name, elem, dims.ToArray());
    }

    public string Describe()
    {
        var lines = new List<string>
        {
            $"IR version {IrVersion}, opset {DefaultOpset}, producer '{Producer}', {InitializerCount} initializers",
        };
        lines.AddRange(Inputs.Select(i => "  input  " + i));
        lines.AddRange(Outputs.Select(o => "  output " + o));
        lines.Add("  ops    " + string.Join(", ", Ops.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}×{kv.Value}")));
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// Splices an <see cref="OnnxGraph"/> around an existing model at the protobuf level:
/// nodes added before <see cref="OnnxGraph.MarkModelPosition"/> run first and produce the
/// model's image input; nodes added after consume the model's outputs. The result is one
/// graph, so the whole pipeline (pre-processing, network, post-processing) is a single GPU run.
/// </summary>
public static class OnnxModelSurgeon
{
    public static byte[] Compose(ReadOnlyMemory<byte> model, OnnxGraph graph, string replacedInput)
    {
        var info = OnnxModelInfo.Parse(model);
        if (info.DefaultOpset < 11)
            throw new NotSupportedException($"The model uses opset {info.DefaultOpset}; HandGI needs opset 11 or newer.");

        var mw = new ProtoWriter();
        var reader = new ProtoReader(model);
        while (reader.Next(out var f))
        {
            if (f.Number == 7 && f.Wire == 2) mw.Bytes(7, RewriteGraph(f.Payload, graph, replacedInput));
            else mw.Raw(f.Encoded.Span);
        }
        return mw.ToArray();
    }

    private static byte[] RewriteGraph(ReadOnlyMemory<byte> graphBytes, OnnxGraph g, string replacedInput)
    {
        var w = new ProtoWriter();
        int mark = g.ModelMark;
        for (int i = 0; i < mark; i++) w.Bytes(1, g.Nodes[i]);

        bool replaced = false;
        var reader = new ProtoReader(graphBytes);
        while (reader.Next(out var f))
        {
            if (f.Number == 11 && f.Wire == 2 && OnnxModelInfo.ParseValueInfo(f.Payload).Name == replacedInput)
            {
                replaced = true;
                continue;
            }
            if (f.Number == 12 && f.Wire == 2) continue; // the model's outputs are replaced by ours
            w.Raw(f.Encoded.Span);
        }

        if (!replaced) throw new InvalidDataException($"The model has no input named '{replacedInput}'.");

        for (int i = mark; i < g.Nodes.Count; i++) w.Bytes(1, g.Nodes[i]);
        foreach (var t in g.Initializers) w.Bytes(5, t);
        foreach (var vi in g.Inputs) w.Bytes(11, vi);
        foreach (var vo in g.Outputs) w.Bytes(12, vo);
        return w.ToArray();
    }
}
