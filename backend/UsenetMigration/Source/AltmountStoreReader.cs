using NzbWebDAV.UsenetMigration.Model;
using NzbWebDAV.UsenetMigration.Proto;
using ZstdSharp;

namespace NzbWebDAV.UsenetMigration.Source;

/// <summary>
/// Reads a <c>.nzbz</c> store file — a single standard zstd frame wrapping a
/// marshalled <c>NzbStore</c> protobuf (internal/metadata/store.go WriteStore).
/// </summary>
public static class AltmountStoreReader
{
    /// <summary>
    /// Reads and decodes a store file from disk. Throws
    /// <see cref="AltmountStoreException"/> on a zstd or protobuf failure so the
    /// caller can classify the release as <c>store_corrupt</c>.
    /// </summary>
    public static async Task<NzbStore> ReadStoreAsync(string path, CancellationToken ct = default)
    {
        byte[] compressed;
        try
        {
            compressed = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new AltmountStoreException($"Failed to read store file '{path}'.", e);
        }

        return Decode(compressed, path);
    }

    /// <summary>Decodes an in-memory <c>.nzbz</c> byte buffer.</summary>
    public static NzbStore Decode(byte[] compressed, string pathForError)
    {
        byte[] raw;
        try
        {
            using var decompressor = new Decompressor();
            raw = decompressor.Unwrap(compressed).ToArray();
        }
        catch (Exception e)
        {
            throw new AltmountStoreException($"Failed to zstd-decompress store '{pathForError}'.", e);
        }

        try
        {
            return MetadataReader.ReadNzbStore(raw);
        }
        catch (Exception e)
        {
            throw new AltmountStoreException($"Failed to decode NzbStore proto '{pathForError}'.", e);
        }
    }
}

/// <summary>Raised when a <c>.nzbz</c> store cannot be decompressed or decoded.</summary>
public sealed class AltmountStoreException(string message, Exception inner) : Exception(message, inner);
