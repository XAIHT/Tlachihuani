// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using System.Globalization;
using System.Text;

namespace HandGI.Core.Onnx;

/// <summary>ONNX TensorProto.DataType values used by HandGI.</summary>
public enum OnnxType
{
    Float = 1,
    UInt8 = 2,
    Int8 = 3,
    Int32 = 6,
    Int64 = 7,
    Bool = 9,
}

/// <summary>An ONNX node attribute (only the kinds HandGI needs).</summary>
public readonly struct OnnxAttr
{
    private readonly string _name;
    private readonly int _kind; // AttributeProto.AttributeType: 1 FLOAT, 2 INT, 7 INTS
    private readonly float _f;
    private readonly long _i;
    private readonly long[]? _ints;

    private OnnxAttr(string name, int kind, float f, long i, long[]? ints)
    {
        _name = name;
        _kind = kind;
        _f = f;
        _i = i;
        _ints = ints;
    }

    public static OnnxAttr Int(string name, long value) => new(name, 2, 0, value, null);
    public static OnnxAttr Float(string name, float value) => new(name, 1, value, 0, null);
    public static OnnxAttr Ints(string name, params long[] values) => new(name, 7, 0, 0, values);

    internal void WriteTo(ProtoWriter w)
    {
        w.String(1, _name);
        switch (_kind)
        {
            case 1:
                w.Float(2, _f);
                break;
            case 2:
                w.Int64(3, _i);
                break;
            case 7:
                w.PackedInt64(8, _ints ?? Array.Empty<long>());
                break;
        }
        w.Int32(20, _kind);
    }
}

/// <summary>
/// A small ONNX graph builder. Every helper emits only operators whose signature is identical
/// from opset 11 to opset 13+, so the same nodes can be spliced into any model of that range.
/// Nodes are serialized immediately; the builder only keeps encoded protobuf blobs.
/// </summary>
public sealed class OnnxGraph
{
    private readonly string _prefix;
    private readonly Dictionary<string, string> _constCache = new(StringComparer.Ordinal);
    private int _counter;
    private int _modelMark = -1;

    internal readonly List<byte[]> Nodes = new();
    internal readonly List<byte[]> Initializers = new();
    internal readonly List<byte[]> Inputs = new();
    internal readonly List<byte[]> Outputs = new();

    public OnnxGraph(string prefix = "hgi_") => _prefix = prefix;

    public int NodeCount => Nodes.Count;

    /// <summary>Nodes added before this call run before the host model; nodes after run after it.</summary>
    public void MarkModelPosition() => _modelMark = Nodes.Count;

    internal int ModelMark => _modelMark < 0 ? Nodes.Count : _modelMark;

    public string Fresh(string hint) => $"{_prefix}{hint}_{_counter++}";

    // ------------------------------------------------------------------ inputs / outputs

    public string Input(string name, OnnxType type, params long[] shape)
    {
        Inputs.Add(ValueInfo(name, type, shape));
        return name;
    }

    public void Output(string name, OnnxType type, params long[] shape) => Outputs.Add(ValueInfo(name, type, shape));

    internal static byte[] ValueInfo(string name, OnnxType type, long[] shape)
    {
        var w = new ProtoWriter();
        w.String(1, name);
        w.Message(2, typeProto => typeProto.Message(1, tensor =>
        {
            tensor.Int32(1, (int)type);
            tensor.Message(2, s =>
            {
                foreach (long d in shape)
                {
                    s.Message(1, dim =>
                    {
                        if (d >= 0) dim.Int64(1, d);
                        else dim.String(2, "N");
                    });
                }
            });
        }));
        return w.ToArray();
    }

    // ------------------------------------------------------------------ constants

    public string Const(float[] data, params long[] shape) =>
        AddInitializer(OnnxType.Float, ProtoBytes.Of(data), shape, data.Length <= 32 ? "f" + Key(data, shape) : null);

    public string ConstI64(long[] data, params long[] shape) =>
        AddInitializer(OnnxType.Int64, ProtoBytes.Of(data), shape, data.Length <= 32 ? "l" + Key(data, shape) : null);

    public string ConstI32(int[] data, params long[] shape) =>
        AddInitializer(OnnxType.Int32, ProtoBytes.Of(data), shape, data.Length <= 32 ? "i" + Key(data, shape) : null);

    /// <summary>A [1]-shaped float constant (broadcasts against anything).</summary>
    public string Scalar(float value) => Const(new[] { value }, 1);

    public string Ones(int count, params long[] shape)
    {
        var data = new float[count];
        Array.Fill(data, 1f);
        return Const(data, shape);
    }

    private static string Key<T>(T[] data, long[] shape) where T : IFormattable
    {
        var sb = new StringBuilder();
        foreach (long s in shape) sb.Append(s).Append('x');
        sb.Append(':');
        // .NET's default float formatting is the shortest round-trip form, and it is valid for integers too.
        foreach (var v in data) sb.Append(v.ToString(null, CultureInfo.InvariantCulture)).Append(',');
        return sb.ToString();
    }

    private string AddInitializer(OnnxType type, ReadOnlySpan<byte> raw, long[] shape, string? cacheKey)
    {
        if (cacheKey is not null && _constCache.TryGetValue(cacheKey, out var hit)) return hit;
        string name = Fresh("c");
        var w = new ProtoWriter();
        if (shape.Length > 0) w.PackedInt64(1, shape);
        w.Int32(2, (int)type);
        w.String(8, name);
        w.Bytes(9, raw);
        Initializers.Add(w.ToArray());
        if (cacheKey is not null) _constCache[cacheKey] = name;
        return name;
    }

    // ------------------------------------------------------------------ nodes

    public void NodeTo(string op, string[] inputs, string[] outputs, params OnnxAttr[] attributes)
    {
        var w = new ProtoWriter();
        foreach (string i in inputs) w.String(1, i);
        foreach (string o in outputs) w.String(2, o);
        w.String(3, Fresh("n_" + op));
        w.String(4, op);
        foreach (var a in attributes) w.Message(5, a.WriteTo);
        Nodes.Add(w.ToArray());
    }

    public string[] Node(string op, string[] inputs, int outputCount, params OnnxAttr[] attributes)
    {
        var outputs = new string[outputCount];
        for (int i = 0; i < outputCount; i++) outputs[i] = Fresh(op.ToLowerInvariant());
        NodeTo(op, inputs, outputs, attributes);
        return outputs;
    }

    public string Op(string op, params string[] inputs) => Node(op, inputs, 1)[0];

    public string OpA(string op, string[] inputs, params OnnxAttr[] attributes) => Node(op, inputs, 1, attributes)[0];

    // ------------------------------------------------------------------ helpers (opset 11..13 stable)

    public string Add(string a, string b) => Op("Add", a, b);
    public string Sub(string a, string b) => Op("Sub", a, b);
    public string Mul(string a, string b) => Op("Mul", a, b);
    public string Div(string a, string b) => Op("Div", a, b);
    public string MatMul(string a, string b) => Op("MatMul", a, b);
    public string Max(string a, string b) => Op("Max", a, b);
    public string Min(string a, string b) => Op("Min", a, b);
    public string Greater(string a, string b) => Op("Greater", a, b);
    public string Less(string a, string b) => Op("Less", a, b);
    public string And(string a, string b) => Op("And", a, b);
    public string Not(string a) => Op("Not", a);
    public string Floor(string a) => Op("Floor", a);
    public string Sqrt(string a) => Op("Sqrt", a);
    public string Log(string a) => Op("Log", a);
    public string Relu(string a) => Op("Relu", a);
    public string Sigmoid(string a) => Op("Sigmoid", a);
    public void IdentityTo(string a, string output) => NodeTo("Identity", new[] { a }, new[] { output });
    public string Softmax(string a, long axis) => OpA("Softmax", new[] { a }, OnnxAttr.Int("axis", axis));
    public string Cast(string a, OnnxType to) => OpA("Cast", new[] { a }, OnnxAttr.Int("to", (long)to));
    public string Reshape(string a, params long[] shape) => Op("Reshape", a, ConstI64(shape, shape.Length));
    public void ReshapeTo(string a, string output, params long[] shape) => NodeTo("Reshape", new[] { a, ConstI64(shape, shape.Length) }, new[] { output });
    public string Transpose(string a, params long[] perm) => OpA("Transpose", new[] { a }, OnnxAttr.Ints("perm", perm));
    public string Concat(long axis, params string[] xs) => OpA("Concat", xs, OnnxAttr.Int("axis", axis));
    public string Gather(string data, string indices, long axis = 0) => OpA("Gather", new[] { data, indices }, OnnxAttr.Int("axis", axis));
    public string Expand(string a, params long[] shape) => Op("Expand", a, ConstI64(shape, shape.Length));

    /// <summary>ReduceMax with the opset-13 attribute form (axes as attribute).</summary>
    public string ReduceMax(string a, long axis, bool keepDims) =>
        OpA("ReduceMax", new[] { a }, OnnxAttr.Ints("axes", axis), OnnxAttr.Int("keepdims", keepDims ? 1 : 0));

    /// <summary>ReduceMin with the opset-13 attribute form (axes as attribute).</summary>
    public string ReduceMin(string a, long axis, bool keepDims) =>
        OpA("ReduceMin", new[] { a }, OnnxAttr.Ints("axes", axis), OnnxAttr.Int("keepdims", keepDims ? 1 : 0));

    /// <summary>ReduceMean with the opset-13 attribute form (axes as attribute).</summary>
    public string ReduceMean(string a, long axis, bool keepDims) =>
        OpA("ReduceMean", new[] { a }, OnnxAttr.Ints("axes", axis), OnnxAttr.Int("keepdims", keepDims ? 1 : 0));

    public string Slice(string a, long start, long end, long axis) =>
        Op("Slice", a,
            ConstI64(new[] { start }, 1),
            ConstI64(new[] { end }, 1),
            ConstI64(new[] { axis }, 1));

    public (string Values, string Indices) TopK(string x, long k, long axis)
    {
        var o = Node("TopK", new[] { x, ConstI64(new[] { k }, 1) }, 2, OnnxAttr.Int("axis", axis));
        return (o[0], o[1]);
    }

    // ------------------------------------------------------------------ serialization

    /// <summary>Serializes this graph as a standalone model (IR 7 / opset 13 by default).</summary>
    public byte[] ToModel(string graphName = "handgi", long irVersion = 7, long opset = 13)
    {
        var g = new ProtoWriter();
        foreach (var n in Nodes) g.Bytes(1, n);
        g.String(2, graphName);
        foreach (var t in Initializers) g.Bytes(5, t);
        foreach (var i in Inputs) g.Bytes(11, i);
        foreach (var o in Outputs) g.Bytes(12, o);
        var graphBytes = g.ToArray();

        var m = new ProtoWriter();
        m.Int64(1, irVersion);
        m.String(2, "HandGI");
        m.String(3, "0.1");
        m.Bytes(7, graphBytes);
        m.Message(8, op =>
        {
            op.String(1, "");
            op.Int64(2, opset);
        });
        return m.ToArray();
    }
}
