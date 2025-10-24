using SharpCompress.Archives.SevenZip;

namespace NzbWebDAV.Extensions;

public static class SevenZipArchiveExtensions
{
    public static long GetEntryStartByteOffset(this SevenZipArchive archive, int index)
    {
        var database = archive?.GetReflectionField("_database");
        var dataStartPosition = (long?)database?.GetReflectionField("_dataStartPosition");
        var packStreamStartPositions = (List<long>?)database?.GetReflectionField("_packStreamStartPositions");
        return dataStartPosition!.Value + packStreamStartPositions![index];
    }

    public static long GetPackSize(this SevenZipArchive archive, int index)
    {
        var database = archive?.GetReflectionField("_database");
        var packSizes = (List<long>?)database?.GetReflectionField("_packSizes");
        return packSizes![index];
    }
}