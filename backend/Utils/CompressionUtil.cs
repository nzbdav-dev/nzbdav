using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace NzbWebDAV.Utils;

public static class CompressionUtil
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    public static byte[] CompressString(string value)
    {
        if (string.IsNullOrEmpty(value)) return [];
        var inputBytes = Encoding.UTF8.GetBytes(value);
        return CompressBytes(inputBytes);
    }

    public static string DecompressToString(byte[]? data)
    {
        if (data == null || data.Length == 0) return string.Empty;
        try
        {
            using var input = new MemoryStream(data);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(brotli, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (InvalidDataException)
        {
            // Legacy payloads were stored as uncompressed UTF8.
            return Encoding.UTF8.GetString(data);
        }
    }

    public static byte[] SerializeToCompressedJson<T>(T value)
    {
        var serialized = JsonSerializer.Serialize(value, SerializerOptions);
        return CompressString(serialized);
    }

    public static T DeserializeCompressedJson<T>(byte[]? data, Func<T> defaultFactory)
    {
        if (data == null || data.Length == 0) return defaultFactory();
        var json = DecompressToString(data);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? defaultFactory();
    }

    private static byte[] CompressBytes(byte[] payload)
    {
        if (payload.Length == 0) return [];
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            brotli.Write(payload, 0, payload.Length);
        }
        return output.ToArray();
    }
}
