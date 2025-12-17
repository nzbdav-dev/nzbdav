using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using Serilog;

namespace NzbWebDAV.Utils;

public static class CompressionUtil
{
    private static readonly JsonSerializerOptions SerializerOptions = new();
    private const int MaxLoggedFallbacks = 25;
    private static int _loggedFallbackCount;

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
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException)
        {
            var count = Interlocked.Increment(ref _loggedFallbackCount);
            if (count <= MaxLoggedFallbacks && Log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
            {
                var suffix = count == MaxLoggedFallbacks ? " Further messages suppressed." : string.Empty;
                Log.Debug(ex, "Failed to decompress payload; falling back to UTF8 decode.{Suffix}", suffix);
            }
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
        try
        {
            return JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? defaultFactory();
        }
        catch (JsonException ex)
        {
            if (Log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
            {
                Log.Debug(ex, "Failed to deserialize payload as JSON; returning default value.");
            }
            return defaultFactory();
        }
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
