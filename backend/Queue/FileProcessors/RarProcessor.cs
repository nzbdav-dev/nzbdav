using System.Text.RegularExpressions;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Queue.DeobfuscationSteps._3.GetFileInfos;
using NzbWebDAV.Streams;
using SharpCompress.Common;
using SharpCompress.Common.Rar.Headers;
using SharpCompress.IO;
using SharpCompress.Readers;
using Usenet.Nzb;

namespace NzbWebDAV.Queue.FileProcessors;

public class RarProcessor(
    GetFileInfosStep.FileInfo fileInfo,
    UsenetStreamingClient usenet,
    CancellationToken ct
) : BaseProcessor
{
    public override async Task<BaseProcessor.Result?> ProcessAsync()
    {
        try
        {
            await using var stream = await GetNzbFileStream();
            await using var cancellableStream = new CancellableStream(stream, ct);
            var headers = await Task.Run(() => GetRarHeaders(cancellableStream));
            return new Result()
            {
                NzbFile = fileInfo.NzbFile,
                PartSize = cancellableStream.Length,
                ArchiveName = GetArchiveName(),
                PartNumber = GetPartNumber(headers),
                StoredFileSegments = headers
                    .Where(x => x.HeaderType == HeaderType.File)
                    .Select(x => new StoredFileSegment()
                    {
                        PathWithinArchive = x.GetFileName(),
                        Offset = x.GetDataStartPosition(),
                        ByteCount = x.GetAdditionalDataSize(),
                    }).ToArray(),
                ReleaseDate = fileInfo.ReleaseDate,
            };
        }
        catch (CryptographicException ex)
        {
            throw new PasswordProtectedRarException("Password-protected RARs are not supported");
        }
    }

    private string GetArchiveName()
    {
        // remove the .rar extension and remove the .partXX if it exists
        var sansExtension = Path.GetFileNameWithoutExtension(fileInfo.FileName);
        sansExtension = Regex.Replace(sansExtension, @"\.part\d+$", "");
        return sansExtension;
    }

    private int GetPartNumber(List<IRarHeader> rarHeaders)
    {
        // read from archive-header if possible
        var partNumberFromHeaders = GetPartNumberFromHeaders(rarHeaders);
        if (partNumberFromHeaders != null) return partNumberFromHeaders!.Value;

        // handle the `.partXXX.rar` format
        var partMatch = Regex.Match(fileInfo.FileName, @"\.part(\d+)\.rar$", RegexOptions.IgnoreCase);
        if (partMatch.Success) return int.Parse(partMatch.Groups[1].Value);

        // handle the `.rXXX` format
        var rMatch = Regex.Match(fileInfo.FileName, @"\.r(\d+)$", RegexOptions.IgnoreCase);
        if (rMatch.Success) return int.Parse(rMatch.Groups[1].Value);

        // handle the `.rar` format.
        if (fileInfo.FileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase)) return -1;

        // we were unable to determine the part number.
        throw new Exception("Could not determine part number for RAR file.");
    }

    private static int? GetPartNumberFromHeaders(List<IRarHeader> headers)
    {
        headers = headers.Where(x => x.HeaderType is HeaderType.Archive or HeaderType.EndArchive).ToList();

        var archiveHeader = headers.FirstOrDefault(x => x.HeaderType is HeaderType.Archive);
        var archiveVolumeNumber = archiveHeader?.GetVolumeNumber();
        if (archiveVolumeNumber != null) return archiveVolumeNumber!.Value;
        if (archiveHeader?.GetIsFirstVolume() == true) return -1;

        var endHeader = headers.FirstOrDefault(x => x.HeaderType == HeaderType.EndArchive);
        var endVolumeNumber = endHeader?.GetVolumeNumber();
        if (endVolumeNumber != null) return endVolumeNumber!.Value;

        return null;
    }

    private List<IRarHeader> GetRarHeaders(Stream stream)
    {
        ct.ThrowIfCancellationRequested();
        var headerFactory = new RarHeaderFactory(StreamingMode.Seekable, new ReaderOptions());
        var headers = new List<IRarHeader>();
        foreach (var header in headerFactory.ReadHeaders(stream))
        {
            ct.ThrowIfCancellationRequested();

            // Add archive headers
            if (header.HeaderType is HeaderType.Archive or HeaderType.EndArchive)
            {
                headers.Add(header);
                continue;
            }

            // we only care about file headers
            if (header.HeaderType != HeaderType.File || header.IsDirectory() || header.GetFileName() == "QO") continue;

            // we only support stored files (compression method m0).
            if (header.GetCompressionMethod() != 0)
                throw new UnsupportedRarCompressionMethodException(
                    "Only rar files with compression method m0 are supported.");

            // add the headers
            headers.Add(header);
        }

        return headers;
    }

    private async Task<NzbFileStream> GetNzbFileStream()
    {
        var filesize = fileInfo.FileSize ?? await usenet.GetFileSizeAsync(fileInfo.NzbFile, ct);
        return usenet.GetFileStream(fileInfo.NzbFile, filesize, concurrentConnections: 1);
    }

    public new class Result : BaseProcessor.Result
    {
        public required NzbFile NzbFile { get; init; }
        public required long PartSize { get; init; }
        public required string ArchiveName { get; init; }
        public required int PartNumber { get; init; }
        public required StoredFileSegment[] StoredFileSegments { get; init; }
        public required DateTimeOffset ReleaseDate { get; init; }
    }

    public class StoredFileSegment
    {
        public required string PathWithinArchive { get; init; }
        public required long Offset { get; init; }
        public required long ByteCount { get; init; }
    }
}