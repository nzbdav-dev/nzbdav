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
            var coders = (IEnumerable?)entry
                ?.GetReflectionProperty("FilePart")
                ?.GetReflectionProperty("Folder")
                ?.GetReflectionField("_coders");
            var compressionMethodId = (ulong?)FirstOrDefault(coders)
                ?.GetReflectionField("_methodId")
                ?.GetReflectionField("_id");
            return compressionMethodId == 0
                ? CompressionType.None
                : CompressionType.Unknown;
        }
    }

    private static object? FirstOrDefault(IEnumerable? enumerable)
    {
        return enumerable?.Cast<object?>().FirstOrDefault();
    }
}