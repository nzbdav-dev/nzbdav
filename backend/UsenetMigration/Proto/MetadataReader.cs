using NzbWebDAV.UsenetMigration.Model;

namespace NzbWebDAV.UsenetMigration.Proto;

/// <summary>
/// Decodes Altmount's <c>FileMetadata</c> and <c>NzbStore</c> protobuf messages
/// from raw wire bytes. Field numbers follow the vendored
/// <c>metadata.proto</c> schema.
/// </summary>
public static class MetadataReader
{
    /// <summary>
    /// 5-byte magic prefix Altmount prepends to v3 <c>.meta</c> files. The leading
    /// 0x00 is an invalid proto tag byte, so v1 files (raw proto, no magic) are
    /// distinguishable. (internal/metadata/service.go:29)
    /// </summary>
    public static readonly byte[] MetaMagicV3 = { 0x00, (byte)'A', (byte)'M', (byte)'3', 0x01 };

    public static bool IsV3Meta(ReadOnlySpan<byte> data)
    {
        if (data.Length < MetaMagicV3.Length) return false;
        for (var i = 0; i < MetaMagicV3.Length; i++)
            if (data[i] != MetaMagicV3[i])
                return false;
        return true;
    }

    /// <summary>
    /// Decodes a full <c>.meta</c> file (with or without the v3 magic prefix)
    /// into the subset of <see cref="AltmountFileMetadata"/> the migration needs.
    /// </summary>
    public static AltmountFileMetadata ReadFileMetadata(ReadOnlySpan<byte> fileBytes)
    {
        var isV3 = IsV3Meta(fileBytes);
        var payload = isV3 ? fileBytes[MetaMagicV3.Length..] : fileBytes;
        return DecodeFileMetadata(payload, isV3);
    }

    private static AltmountFileMetadata DecodeFileMetadata(ReadOnlySpan<byte> payload, bool isV3)
    {
        long fileSize = 0;
        var status = AltmountFileStatus.Unspecified;
        var password = "";
        var salt = "";
        var encryption = AltmountEncryption.None;
        long releaseDate = 0;
        var storeRef = "";
        var hasNested = false;
        var hasClips = false;

        var reader = new ProtoWireReader(payload);
        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == ProtoWireReader.WireType.Varint: // file_size
                    fileSize = reader.ReadInt64();
                    break;
                case 3 when wire == ProtoWireReader.WireType.Varint: // status
                    status = (AltmountFileStatus)reader.ReadInt32();
                    break;
                case 6 when wire == ProtoWireReader.WireType.LengthDelimited: // password
                    password = reader.ReadString();
                    break;
                case 7 when wire == ProtoWireReader.WireType.LengthDelimited: // salt
                    salt = reader.ReadString();
                    break;
                case 8 when wire == ProtoWireReader.WireType.Varint: // encryption
                    encryption = (AltmountEncryption)reader.ReadInt32();
                    break;
                case 12 when wire == ProtoWireReader.WireType.Varint: // release_date
                    releaseDate = reader.ReadInt64();
                    break;
                case 15 when wire == ProtoWireReader.WireType.LengthDelimited: // nested_sources
                    reader.ReadLengthDelimited();
                    hasNested = true;
                    break;
                case 17 when wire == ProtoWireReader.WireType.LengthDelimited: // clip_boundaries
                    reader.ReadLengthDelimited();
                    hasClips = true;
                    break;
                case 18 when wire == ProtoWireReader.WireType.LengthDelimited: // store_ref
                    storeRef = reader.ReadString();
                    break;
                default:
                    reader.SkipField(wire);
                    break;
            }
        }

        return new AltmountFileMetadata
        {
            IsV3 = isV3,
            FileSize = fileSize,
            Status = status,
            Password = password,
            Salt = salt,
            Encryption = encryption,
            ReleaseDate = releaseDate,
            StoreRef = storeRef,
            HasNestedSources = hasNested,
            HasClipBoundaries = hasClips,
        };
    }

    /// <summary>
    /// Reads only the <c>store_ref</c> from a <c>.meta</c> file, mirroring
    /// Altmount's <c>readStoreRef</c> fast path. Returns "" for non-v3 files.
    /// </summary>
    public static string ReadStoreRef(ReadOnlySpan<byte> fileBytes)
    {
        if (!IsV3Meta(fileBytes)) return "";
        return DecodeFileMetadata(fileBytes[MetaMagicV3.Length..], isV3: true).StoreRef;
    }

    /// <summary>
    /// Decodes an already-decompressed <c>NzbStore</c> protobuf payload.
    /// </summary>
    public static NzbStore ReadNzbStore(ReadOnlySpan<byte> payload)
    {
        var files = new List<NzbFileEntry>();
        var reader = new ProtoWireReader(payload);
        while (reader.TryReadTag(out var field, out var wire))
        {
            if (field == 1 && wire == ProtoWireReader.WireType.LengthDelimited) // repeated NzbFileEntry files
                files.Add(DecodeFileEntry(reader.ReadLengthDelimited()));
            else
                reader.SkipField(wire);
        }

        return new NzbStore { Files = files };
    }

    private static NzbFileEntry DecodeFileEntry(ReadOnlySpan<byte> payload)
    {
        var subject = "";
        var poster = "";
        long date = 0;
        var groups = new List<string>();
        var segments = new List<NzbSeg>();

        var reader = new ProtoWireReader(payload);
        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == ProtoWireReader.WireType.LengthDelimited: // subject
                    subject = reader.ReadString();
                    break;
                case 2 when wire == ProtoWireReader.WireType.LengthDelimited: // poster
                    poster = reader.ReadString();
                    break;
                case 3 when wire == ProtoWireReader.WireType.Varint: // date
                    date = reader.ReadInt64();
                    break;
                case 4 when wire == ProtoWireReader.WireType.LengthDelimited: // groups
                    groups.Add(reader.ReadString());
                    break;
                case 5 when wire == ProtoWireReader.WireType.LengthDelimited: // segments
                    segments.Add(DecodeSeg(reader.ReadLengthDelimited()));
                    break;
                default:
                    reader.SkipField(wire);
                    break;
            }
        }

        return new NzbFileEntry
        {
            Subject = subject,
            Poster = poster,
            Date = date,
            Groups = groups,
            Segments = segments,
        };
    }

    private static NzbSeg DecodeSeg(ReadOnlySpan<byte> payload)
    {
        var id = "";
        var number = 0;
        long bytes = 0;

        var reader = new ProtoWireReader(payload);
        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == ProtoWireReader.WireType.LengthDelimited: // id
                    id = reader.ReadString();
                    break;
                case 2 when wire == ProtoWireReader.WireType.Varint: // number
                    number = reader.ReadInt32();
                    break;
                case 3 when wire == ProtoWireReader.WireType.Varint: // bytes
                    bytes = reader.ReadInt64();
                    break;
                default:
                    reader.SkipField(wire);
                    break;
            }
        }

        return new NzbSeg { Id = id, Number = number, Bytes = bytes };
    }
}
