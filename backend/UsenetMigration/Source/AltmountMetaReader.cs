using NzbWebDAV.UsenetMigration.Model;
using NzbWebDAV.UsenetMigration.Proto;

namespace NzbWebDAV.UsenetMigration.Source;

/// <summary>
/// Reads a <c>.meta</c> file (Altmount's per-virtual-file <c>FileMetadata</c>
/// proto, optionally with the v3 magic prefix) into
/// <see cref="AltmountFileMetadata"/>.
/// </summary>
public static class AltmountMetaReader
{
    public static async Task<AltmountFileMetadata> ReadAsync(string path, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        return MetadataReader.ReadFileMetadata(bytes);
    }

    /// <summary>
    /// Reads only the <c>store_ref</c> — the grouping key for the scan's first
    /// pass — without materialising the rest of the proto (Altmount's fast path).
    /// </summary>
    public static async Task<string> ReadStoreRefAsync(string path, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        return MetadataReader.ReadStoreRef(bytes);
    }
}
