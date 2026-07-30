using System.Text;

namespace NzbWebDAV.Tests.UsenetMigration;

/// <summary>
/// Minimal protobuf wire-format writer used to synthesise fixture bytes for the
/// hand-rolled MetadataReader tests. Encodes only the wire types the Altmount
/// messages use.
/// </summary>
internal sealed class TestProtoWriter
{
    private readonly List<byte> _bytes = new();

    public byte[] ToArray() => _bytes.ToArray();

    public TestProtoWriter Varint(int field, long value)
    {
        WriteTag(field, 0);
        WriteVarint((ulong)value);
        return this;
    }

    public TestProtoWriter String(int field, string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        return Bytes(field, utf8);
    }

    public TestProtoWriter Bytes(int field, byte[] value)
    {
        WriteTag(field, 2);
        WriteVarint((ulong)value.Length);
        _bytes.AddRange(value);
        return this;
    }

    public TestProtoWriter Fixed32(int field, uint value)
    {
        WriteTag(field, 5);
        _bytes.Add((byte)(value & 0xFF));
        _bytes.Add((byte)((value >> 8) & 0xFF));
        _bytes.Add((byte)((value >> 16) & 0xFF));
        _bytes.Add((byte)((value >> 24) & 0xFF));
        return this;
    }

    private void WriteTag(int field, int wireType) => WriteVarint(((ulong)field << 3) | (uint)wireType);

    private void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }

        _bytes.Add((byte)value);
    }
}
