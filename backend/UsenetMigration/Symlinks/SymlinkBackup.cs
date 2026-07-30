using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;

namespace NzbWebDAV.UsenetMigration.Symlinks;

/// <summary>
/// A restore <c>.tar.gz</c> whose single entry is a
/// JSON manifest of the symlinks' pre-rewrite state (path → original Altmount target).
/// The wizard writes this <b>before</b> any rewrite so the exact prior state can be
/// replayed. A manifest (rather than raw symlink tar entries) keeps entry names safe
/// and restore fully deterministic and testable.
/// </summary>
public static class SymlinkBackup
{
    private const string ManifestEntryName = "symlinks.json";

    public sealed record Entry(string Path, string Target, string? ReplacementTarget = null);

    public static async Task WriteAsync(
        string backupFilePath, IReadOnlyList<Entry> entries, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(backupFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.SerializeToUtf8Bytes(entries);

        await using var fs = File.Create(backupFilePath);
        await using var gz = new GZipStream(fs, CompressionLevel.Optimal);
        await using var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: false);
        var manifest = new PaxTarEntry(TarEntryType.RegularFile, ManifestEntryName)
        {
            DataStream = new MemoryStream(json),
        };
        await tar.WriteEntryAsync(manifest, ct).ConfigureAwait(false);
    }

    /// <summary>Reads the manifest back out of a backup (does not touch the filesystem).</summary>
    public static async Task<IReadOnlyList<Entry>> ReadAsync(
        string backupFilePath, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(backupFilePath);
        await using var gz = new GZipStream(fs, CompressionMode.Decompress);
        await using var tar = new TarReader(gz);
        while (await tar.GetNextEntryAsync(cancellationToken: ct).ConfigureAwait(false) is { } entry)
        {
            if (entry.Name != ManifestEntryName || entry.DataStream is null)
                continue;
            using var ms = new MemoryStream();
            await entry.DataStream.CopyToAsync(ms, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<Entry>>(ms.ToArray()) ?? new List<Entry>();
        }
        throw new InvalidDataException("The archive does not contain a symlink manifest.");
    }
}
