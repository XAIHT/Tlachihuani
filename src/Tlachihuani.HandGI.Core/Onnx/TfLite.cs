// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace HandGI.Core.Onnx;

/// <summary>TensorFlow Lite builtin operator codes HandGI reads (tensorflow/lite/schema/schema.fbs).</summary>
public enum TfLiteOp
{
    Add = 0,
    FullyConnected = 9,
    Mul = 18,
    Reshape = 22,
    Softmax = 25,
    Gather = 36,
    Mean = 40,
    Sub = 41,
    Div = 42,
    StridedSlice = 45,
    Maximum = 55,
    ReduceMax = 82,
    Unpack = 88,
    ReduceMin = 89,
}

/// <summary>One tensor of a .tflite model; constant tensors carry their raw little-endian data.</summary>
public sealed class TfLiteTensor
{
    public const int Float32 = 0;
    public const int Int32 = 2;

    internal TfLiteTensor(int index, string name, int[] shape, int type, ReadOnlyMemory<byte> data)
    {
        Index = index;
        Name = name;
        Shape = shape;
        Type = type;
        Data = data;
    }

    public int Index { get; }
    public string Name { get; }
    public int[] Shape { get; }
    public int Type { get; }
    public ReadOnlyMemory<byte> Data { get; }
    public bool IsConstant => !Data.IsEmpty;
    public int Elements => Shape.Aggregate(1, (a, b) => a * Math.Max(1, b));

    public float[] Floats()
    {
        if (Type != Float32 || !IsConstant) throw new InvalidDataException($"Tensor '{Name}' is not a constant float32 tensor.");
        return MemoryMarshal.Cast<byte, float>(Data.Span).ToArray();
    }

    public int[] Ints()
    {
        if (Type != Int32 || !IsConstant) throw new InvalidDataException($"Tensor '{Name}' is not a constant int32 tensor.");
        return MemoryMarshal.Cast<byte, int>(Data.Span).ToArray();
    }

    public override string ToString() => $"{Name} [{string.Join(",", Shape)}]";
}

/// <summary>One operator: its builtin code, tensor indices and (for ADD/SUB/MUL/DIV/FULLY_CONNECTED) the fused activation.</summary>
public sealed record TfLiteOperator(int Code, int[] Inputs, int[] Outputs, int FusedActivation)
{
    public const int ActivationNone = 0;
    public const int ActivationRelu = 1;

    public TfLiteOp Op => (TfLiteOp)Code;
}

/// <summary>
/// Reads what HandGI needs from a .tflite FlatBuffer: the first subgraph's tensors (with constant
/// data), operators and I/O. Pure C#, no TensorFlow: HandGI loads Google's model files as they ship
/// and rebuilds the networks as ONNX for DirectML. Every read is bounds-checked; a damaged file is
/// refused with <see cref="InvalidDataException"/>, never half-read.
/// </summary>
public sealed class TfLiteModel
{
    private TfLiteModel(IReadOnlyList<TfLiteTensor> tensors, IReadOnlyList<TfLiteOperator> operators, int[] inputs, int[] outputs)
    {
        Tensors = tensors;
        Operators = operators;
        Inputs = inputs;
        Outputs = outputs;
    }

    public IReadOnlyList<TfLiteTensor> Tensors { get; }
    public IReadOnlyList<TfLiteOperator> Operators { get; }
    public int[] Inputs { get; }
    public int[] Outputs { get; }

    public static TfLiteModel Parse(ReadOnlyMemory<byte> file)
    {
        try
        {
            return ParseCore(file);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("The .tflite file is damaged (a FlatBuffer offset points outside the file).", ex);
        }
    }

    private static TfLiteModel ParseCore(ReadOnlyMemory<byte> file)
    {
        if (file.Length < 8 || Encoding.ASCII.GetString(file.Span.Slice(4, 4)) != "TFL3")
            throw new InvalidDataException("Not a TensorFlow Lite model (no TFL3 identifier).");

        var model = new FbTable(file, checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file.Span)));
        var codes = model.Tables(1).Select(c => Math.Max((int)(sbyte)c.Byte(0), c.Int32(3))).ToArray();
        var buffers = model.Tables(4);
        var graphs = model.Tables(2);
        if (graphs.Length == 0) throw new InvalidDataException("The .tflite model has no subgraph.");
        var graph = graphs[0];

        var tensorTables = graph.Tables(0);
        var tensors = new List<TfLiteTensor>(tensorTables.Length);
        for (int i = 0; i < tensorTables.Length; i++)
        {
            var t = tensorTables[i];
            int bufferIndex = checked((int)t.UInt32(2));
            ReadOnlyMemory<byte> data = default;
            if (bufferIndex > 0 && bufferIndex < buffers.Length)
            {
                var b = buffers[bufferIndex];
                data = b.Bytes(0);
                if (data.IsEmpty)
                {
                    ulong offset = b.UInt64(1), size = b.UInt64(2);
                    if (offset > 1 && size > 0) data = file.Slice(checked((int)offset), checked((int)size));
                }
            }
            tensors.Add(new TfLiteTensor(i, t.String(3) ?? $"tensor{i}", t.Ints(0), t.Byte(1), data));
        }

        var operators = new List<TfLiteOperator>();
        foreach (var op in graph.Tables(3))
        {
            int code = codes[checked((int)op.UInt32(0))];
            int activation = 0;
            if (code is (int)TfLiteOp.Add or (int)TfLiteOp.Sub or (int)TfLiteOp.Mul or (int)TfLiteOp.Div or (int)TfLiteOp.FullyConnected)
                activation = op.Table(4)?.Byte(0) ?? 0; // field 0 of these option tables is fused_activation_function
            operators.Add(new TfLiteOperator(code, op.Ints(1), op.Ints(2), activation));
        }

        return new TfLiteModel(tensors, operators, graph.Ints(1), graph.Ints(2));
    }
}

/// <summary>A FlatBuffers table view: vtable lookup, scalars, strings, vectors and sub-tables.</summary>
internal readonly struct FbTable
{
    private readonly ReadOnlyMemory<byte> _buf;
    private readonly int _pos;
    private readonly int _vtable;
    private readonly int _vtableSize;

    public FbTable(ReadOnlyMemory<byte> buf, int pos)
    {
        _buf = buf;
        _pos = pos;
        _vtable = checked(pos - BinaryPrimitives.ReadInt32LittleEndian(buf.Span.Slice(pos, 4)));
        _vtableSize = BinaryPrimitives.ReadUInt16LittleEndian(buf.Span.Slice(_vtable, 2));
    }

    private int Field(int index)
    {
        int slot = 4 + 2 * index;
        return slot + 2 > _vtableSize ? 0 : BinaryPrimitives.ReadUInt16LittleEndian(_buf.Span.Slice(_vtable + slot, 2));
    }

    public byte Byte(int index, byte fallback = 0)
    {
        int o = Field(index);
        return o == 0 ? fallback : _buf.Span[_pos + o];
    }

    public int Int32(int index, int fallback = 0)
    {
        int o = Field(index);
        return o == 0 ? fallback : BinaryPrimitives.ReadInt32LittleEndian(_buf.Span.Slice(_pos + o, 4));
    }

    public uint UInt32(int index, uint fallback = 0)
    {
        int o = Field(index);
        return o == 0 ? fallback : BinaryPrimitives.ReadUInt32LittleEndian(_buf.Span.Slice(_pos + o, 4));
    }

    public ulong UInt64(int index, ulong fallback = 0)
    {
        int o = Field(index);
        return o == 0 ? fallback : BinaryPrimitives.ReadUInt64LittleEndian(_buf.Span.Slice(_pos + o, 8));
    }

    private int Indirect(int index)
    {
        int o = Field(index);
        if (o == 0) return -1;
        int at = _pos + o;
        return checked(at + (int)BinaryPrimitives.ReadUInt32LittleEndian(_buf.Span.Slice(at, 4)));
    }

    private int Length(int at) => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(_buf.Span.Slice(at, 4)));

    public FbTable? Table(int index)
    {
        int p = Indirect(index);
        return p < 0 ? null : new FbTable(_buf, p);
    }

    public string? String(int index)
    {
        int p = Indirect(index);
        return p < 0 ? null : Encoding.UTF8.GetString(_buf.Span.Slice(p + 4, Length(p)));
    }

    public FbTable[] Tables(int index)
    {
        int p = Indirect(index);
        if (p < 0) return Array.Empty<FbTable>();
        int n = Length(p);
        var result = new FbTable[n];
        for (int i = 0; i < n; i++)
        {
            int at = p + 4 + 4 * i;
            result[i] = new FbTable(_buf, checked(at + (int)BinaryPrimitives.ReadUInt32LittleEndian(_buf.Span.Slice(at, 4))));
        }
        return result;
    }

    public int[] Ints(int index)
    {
        int p = Indirect(index);
        if (p < 0) return Array.Empty<int>();
        int n = Length(p);
        return MemoryMarshal.Cast<byte, int>(_buf.Span.Slice(p + 4, checked(n * 4))).ToArray();
    }

    public ReadOnlyMemory<byte> Bytes(int index)
    {
        int p = Indirect(index);
        return p < 0 ? default : _buf.Slice(p + 4, Length(p));
    }
}
