using System.Text.RegularExpressions;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using NzbWebDAV.Queue.DeobfuscationSteps._3.GetFileInfos;
using NzbWebDAV.Streams;
using NzbWebDAV.Utils;
using SharpCompress.Common.Rar.Headers;
using Usenet.Nzb;

namespace NzbWebDAV.Queue.FileProcessors;

public class RarProcessor(
    GetFileInfosStep.FileInfo fileInfo,
    UsenetStreamingClient usenet,
    string? password,
    CancellationToken ct
) : BaseProcessor
{
    public override async Task<BaseProcessor.Result?> ProcessAsync()
    {
        await using var stream = await GetNzbFileStream();
        var headers = await RarUtil.GetRarHeadersAsync(stream, password, ct);
        var archiveName = GetArchiveName();
        var partNumber = GetPartNumber(headers);
        return new Result()
        {
            StoredFileSegments = headers
                .Where(x => x.HeaderType == HeaderType.File)
                .Select(x => new StoredFileSegment()
                {
                    NzbFile = fileInfo.NzbFile,
                    PartSize = stream.Length,
                    ArchiveName = archiveName,
                    PartNumber = partNumber,
                    PathWithinArchive = x.GetFileName(),
                    ByteRangeWithinPart = LongRange.FromStartAndSize(
                        x.GetDataStartPosition(),
                        x.GetAdditionalDataSize()
                    ),
                    AesParams = x.GetAesParams(password),
                    ReleaseDate = fileInfo.ReleaseDate,
                }).ToArray(),
        };
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

    private async Task<NzbFileStream> GetNzbFileStream()
    {
        var filesize = fileInfo.FileSize ?? await usenet.GetFileSizeAsync(fileInfo.NzbFile, ct);
        return usenet.GetFileStream(fileInfo.NzbFile, filesize, concurrentConnections: 1);
    }

    public new class Result : BaseProcessor.Result
    {
        public required StoredFileSegment[] StoredFileSegments { get; init; }
    }

    public class StoredFileSegment
    {
        public required NzbFile NzbFile { get; init; }
        public required long PartSize { get; init; }
        public required string ArchiveName { get; init; }
        public required int PartNumber { get; init; }
        public required DateTimeOffset ReleaseDate { get; init; }

        public required string PathWithinArchive { get; init; }
        public required LongRange ByteRangeWithinPart { get; init; }
        public required AesParams? AesParams { get; init; }
    }
}