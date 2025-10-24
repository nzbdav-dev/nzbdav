using System.Collections;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;

namespace NzbWebDAV.Extensions;

public static class SevenZipArchiveEntryExtensions
{
    public static CompressionType GetCompressionType(this SevenZipArchiveEntry entry)
    {
        try
        {
            return entry.CompressionType;
        }
        catch (NotImplementedException)
        {
            var coders = entry?.GetCoders();
            var compressionMethodId = GetCoderMethodId(coders?.FirstOrDefault());
            return compressionMethodId == 0 ? CompressionType.None
                : compressionMethodId == 116459265 ? CompressionType.None
                : CompressionType.Unknown;
        }
    }

    public static byte[]? GetAesCoderInfoProps(this SevenZipArchiveEntry entry)
    {
        const ulong aesMethodId = 0x06F10701;
        return (byte[]?)entry
            ?.GetCoders()
            ?.FirstOrDefault(x => GetCoderMethodId(x) == aesMethodId)
            ?.GetReflectionField("_props");
    }

    public static long GetFolderStartByteOffset(this SevenZipArchiveEntry entry)
    {
        var filePart = entry?.GetReflectionProperty("FilePart");
        var folder = filePart?.GetReflectionProperty("Folder");
        var database = filePart?.GetReflectionField("_database");
        var folderFirstPackStreamId = (int)folder?.GetReflectionField("_firstPackStreamId")!;
        var databaseDataStartPosition = (long)database?.GetReflectionField("_dataStartPosition")!;
        var databasePackStreamStartPositions = (List<long>)database?.GetReflectionField("_packStreamStartPositions")!;
        return databaseDataStartPosition + databasePackStreamStartPositions[folderFirstPackStreamId];
    }

    public static long? GetPackedSize(this SevenZipArchiveEntry entry)
    {
        throw new NotImplementedException();
    }

    private static IEnumerable<object?>? GetCoders(this SevenZipArchiveEntry entry)
    {
        var coders = (IEnumerable?)entry
            ?.GetFolder()
            ?.GetReflectionField("_coders");
        return coders?.Cast<object?>();
    }

    private static object? GetFolder(this SevenZipArchiveEntry entry)
    {
        return entry
            ?.GetReflectionProperty("FilePart")
            ?.GetReflectionProperty("Folder");
    }

    private static ulong? GetCoderMethodId(object? coder)
    {
        return (ulong?)coder
            ?.GetReflectionField("_methodId")
            ?.GetReflectionField("_id");
    }
}