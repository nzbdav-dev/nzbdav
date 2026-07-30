using System.Text;

namespace NzbWebDAV.UsenetMigration.Proto;

/// <summary>
/// Minimal protobuf wire-format reader (proto3), sufficient to decode the
/// Altmount messages the migration consumes without requiring a protobuf runtime.
///
/// Supports the four wire types that appear in Altmount's metadata protos and
/// skips unknown fields by wire type, so upstream field additions are ignored
/// safely rather than corrupting the parse.
/// </summary>
public ref struct ProtoWireReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _pos;

    public ProtoWireReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _pos = 0;
    }

    public readonly bool End => _pos >= _data.Length;

    /// <summary>Wire types (proto encoding).</summary>
    public enum WireType
    {
        Varint = 0,
        Fixed64 = 1,
        LengthDelimited = 2,
        StartGroup = 3, // deprecated; not expected
        EndGroup = 4,   // deprecated; not expected
        Fixed32 = 5,
    }

    /// <summary>
    /// Reads the next field's tag. Returns false at end of buffer.
    /// </summary>
    public bool TryReadTag(out int fieldNumber, out WireType wireType)
    {
        fieldNumber = 0;
        wireType = WireType.Varint;
        if (End) return false;
        var tag = ReadVarint();
        fieldNumber = (int)(tag >> 3);
        wireType = (WireType)(int)(tag & 0x7);
        if (fieldNumber <= 0)
            throw new InvalidDataException($"Invalid protobuf field number {fieldNumber}.");
        return true;
    }

    public ulong ReadVarint()
    {
        ulong result = 0;
        var shift = 0;
        while (true)
        {
            if (_pos >= _data.Length)
                throw new InvalidDataException("Truncated protobuf varint.");
            var b = _data[_pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift >= 64)
                throw new InvalidDataException("Protobuf varint exceeds 64 bits.");
        }

        return result;
    }

    public long ReadInt64() => (long)ReadVarint();
    public int ReadInt32() => (int)(long)ReadVarint();

    public ReadOnlySpan<byte> ReadLengthDelimited()
    {
        var len = (int)ReadVarint();
        if (len < 0 || _pos + len > _data.Length)
            throw new InvalidDataException("Truncated protobuf length-delimited field.");
        var slice = _data.Slice(_pos, len);
        _pos += len;
        return slice;
    }

    public string ReadString() => Encoding.UTF8.GetString(ReadLengthDelimited());

    /// <summary>Skips a field of the given wire type.</summary>
    public void SkipField(WireType wireType)
    {
        switch (wireType)
        {
            case WireType.Varint:
                ReadVarint();
                break;
            case WireType.Fixed64:
                Advance(8);
                break;
            case WireType.LengthDelimited:
                ReadLengthDelimited();
                break;
            case WireType.Fixed32:
                Advance(4);
                break;
            default:
                throw new InvalidDataException($"Unsupported protobuf wire type {wireType}.");
        }
    }

    private void Advance(int n)
    {
        if (_pos + n > _data.Length)
            throw new InvalidDataException("Truncated protobuf fixed-width field.");
        _pos += n;
    }
}
