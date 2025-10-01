using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using NzbWebDAV.Streams;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;

namespace NzbWebDAV.Utils;

public static class SevenZipUtil
{
    public static async Task<List<SevenZipEntry>> GetSevenZipEntriesAsync(Stream stream, CancellationToken ct)
    {
        await using var cancellableStream = new CancellableStream(stream, ct);
        return await Task.Run(() => GetSevenZipEntries(cancellableStream), ct);
    }

    public static List<SevenZipEntry> GetSevenZipEntries(Stream stream)
    {
        try
        {
            using var archive = SevenZipArchive.Open(stream);
            return archive.Entries
                .Where(x => !x.IsDirectory)
                .Select((entry, index) => new SevenZipEntry(entry, archive, index))
                .ToList();
        }
        catch (CryptographicException e)
        {
            throw new PasswordProtected7zException("Password-protected 7z archives are not supported");
        }
    }

    public class SevenZipEntry(SevenZipArchiveEntry entry, SevenZipArchive archive, int index)
    {
        public string PathWithinArchive { get; } = entry.Key!;
        public CompressionType CompressionType { get; } = entry.GetCompressionType();

        public LongRange ByteRangeWithinArchive { get; } =
            LongRange.FromStartAndSize(archive.GetEntryStartByteOffset(index), entry.Size);
    }
}