using SharpCompress.Common.Rar.Headers;

namespace NzbWebDAV.Extensions;

public static class RarHeaderExtensions
{
    public static byte GetCompressionMethod(this IRarHeader header)
    {
        return (byte)header.GetReflectionProperty("CompressionMethod")!;
    }

    public static long GetDataStartPosition(this IRarHeader header)
    {
        return (long)header.GetReflectionProperty("DataStartPosition")!;
    }

    public static long GetAdditionalDataSize(this IRarHeader header)
    {
        return (long)header.GetReflectionProperty("AdditionalDataSize")!;
    }

    public static long GetCompressedSize(this IRarHeader header)
    {
        return (long)header.GetReflectionProperty("CompressedSize")!;
    }

    public static string GetFileName(this IRarHeader header)
    {
        return (string)header.GetReflectionProperty("FileName")!;
    }

    public static bool IsDirectory(this IRarHeader header)
    {
        return (bool)header.GetReflectionProperty("IsDirectory")!;
    }

    public static int? GetVolumeNumber(this IRarHeader header)
    {
        return header.HeaderType == HeaderType.Archive
            ? (int?)header.GetReflectionProperty("VolumeNumber")
            : (short)header.GetReflectionProperty("VolumeNumber")!;
    }

    public static bool GetIsFirstVolume(this IRarHeader header)
    {
        return (bool)header.GetReflectionProperty("IsFirstVolume")!;
    }
}