// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace HandGI.Core.Onnx;

/// <summary>
/// Minimal protobuf encoder: exactly what is needed to emit ONNX ModelProto messages,
/// so HandGI can generate its GPU graphs at run time with no Python and no extra package.
/// </summary>
internal sealed class ProtoWriter
{
    private readonly MemoryStream _ms = new();

    public byte[] ToArray() => _ms.ToArray();

    private void Varint(ulong value)
    {
        while (value >= 0x80)
        {
            _ms.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        _ms.WriteByte((byte)value);
    }

    private void Tag(int field, int wire) => Varint(((ulong)(uint)field << 3) | (uint)wire);

    public void Int64(int field, long value)
    {
        Tag(field, 0);
        Varint(unchecked((ulong)value));
    }

    public void Int32(int field, int value)
    {
        Tag(field, 0);
        Varint(unchecked((ulong)(long)value));
    }

    public void Float(int field, float value)
    {
        Tag(field, 5);
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(b, value);
        _ms.Write(b);
    }

    public void Bytes(int field, ReadOnlySpan<byte> data)
    {
        Tag(field, 2);
        Varint((ulong)data.Length);
        _ms.Write(data);
    }

    public void String(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));

    public void Message(int field, Action<ProtoWriter> build)
    {
        var inner = new ProtoWriter();
        build(inner);
        Bytes(field, inner._ms.GetBuffer().AsSpan(0, (int)inner._ms.Length));
    }

    public void PackedInt64(int field, ReadOnlySpan<long> values)
    {
        var inner = new ProtoWriter();
        foreach (long v in values) inner.Varint(unchecked((ulong)v));
        Bytes(field, inner._ms.GetBuffer().AsSpan(0, (int)inner._ms.Length));
    }

    /// <summary>Copies an already-encoded field (tag + payload) verbatim.</summary>
    public void Raw(ReadOnlySpan<byte> encodedField) => _ms.Write(encodedField);
}

internal readonly record struct ProtoField(int Number, int Wire, ulong Varint, ReadOnlyMemory<byte> Payload, ReadOnlyMemory<byte> Encoded);

/// <summary>Minimal protobuf decoder over a memory block.</summary>
internal sealed class ProtoReader
{
    private readonly ReadOnlyMemory<byte> _buffer;
    private int _pos;

    public ProtoReader(ReadOnlyMemory<byte> buffer) => _buffer = buffer;

    private ulong ReadVarint()
    {
        var span = _buffer.Span;
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            if (_pos >= span.Length) throw new InvalidDataException("Truncated protobuf varint.");
            byte b = span[_pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80) return result;
            shift += 7;
            if (shift > 63) throw new InvalidDataException("Protobuf varint is too long.");
        }
    }

    public bool Next(out ProtoField field)
    {
        if (_pos >= _buffer.Length)
        {
            field = default;
            return false;
        }

        int start = _pos;
        ulong tag = ReadVarint();
        int number = (int)(tag >> 3);
        int wire = (int)(tag & 7);
        ulong varint = 0;
        ReadOnlyMemory<byte> payload = default;
        switch (wire)
        {
            case 0:
                varint = ReadVarint();
                break;
            case 1:
                payload = _buffer.Slice(_pos, 8);
                _pos += 8;
                break;
            case 2:
                int length = checked((int)ReadVarint());
                payload = _buffer.Slice(_pos, length);
                _pos += length;
                break;
            case 5:
                payload = _buffer.Slice(_pos, 4);
                _pos += 4;
                break;
            default:
                throw new InvalidDataException($"Unsupported protobuf wire type {wire}.");
        }

        field = new ProtoField(number, wire, varint, payload, _buffer.Slice(start, _pos - start));
        return true;
    }

    public static string Utf8(ReadOnlyMemory<byte> payload) => Encoding.UTF8.GetString(payload.Span);
}

internal static class ProtoBytes
{
    public static ReadOnlySpan<byte> Of(float[] data) => MemoryMarshal.AsBytes(data.AsSpan());
    public static ReadOnlySpan<byte> Of(long[] data) => MemoryMarshal.AsBytes(data.AsSpan());
    public static ReadOnlySpan<byte> Of(int[] data) => MemoryMarshal.AsBytes(data.AsSpan());
}
