using System.Text.RegularExpressions;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using NzbWebDAV.Models.Nzb;
using NzbWebDAV.Queue.DeobfuscationSteps._3.GetFileInfos;
using NzbWebDAV.Streams;
using NzbWebDAV.Utils;
using SharpCompress.Common.Rar.Headers;

namespace NzbWebDAV.Queue.FileProcessors;

public class RarProcessor(
    GetFileInfosStep.FileInfo fileInfo,
    INntpClient usenetClient,
    string? password,
    CancellationToken ct
) : BaseProcessor
{
    public override async Task<BaseProcessor.Result?> ProcessAsync()
    {
        await using var stream = await GetNzbFileStream().ConfigureAwait(false);
        var headers = await RarUtil.GetRarHeadersAsync(stream, password, ct).ConfigureAwait(false);
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
                    FileUncompressedSize = x.GetUncompressedSize(),
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

    private PartNumber GetPartNumber(List<IRarHeader> rarHeaders)
    {
        var partNumber = new PartNumber()
        {
            PartNumberFromHeader = GetPartNumberFromHeaders(rarHeaders),
            PartNumberFromFilename = GetPartNumberFromFilename(fileInfo.FileName),
        };

        if (partNumber.PartNumberFromHeader == null && partNumber.PartNumberFromFilename == null)
            throw new Exception("Could not determine part number for RAR file.");

        return partNumber;
    }

    private static int? GetPartNumberFromHeaders(List<IRarHeader> headers)
    {
        headers = headers.Where(x => x.HeaderType is HeaderType.Archive or HeaderType.EndArchive).ToList();

        var archiveHeader = headers.FirstOrDefault(x => x.HeaderType is HeaderType.Archive);
        var archiveVolumeNumber = archiveHeader?.GetVolumeNumber();
        if (archiveVolumeNumber != null) return archiveVolumeNumber!.Value;

        var endHeader = headers.FirstOrDefault(x => x.HeaderType == HeaderType.EndArchive);
        var endVolumeNumber = endHeader?.GetVolumeNumber();
        if (endVolumeNumber != null) return endVolumeNumber!.Value;

        if (archiveHeader?.GetIsFirstVolume() == true) return -1;
        return null;
    }

    private static int? GetPartNumberFromFilename(string filename)
    {
        // handle the `.partXXX.rar` format
        var partMatch = Regex.Match(filename, @"\.part(\d+)\.rar$", RegexOptions.IgnoreCase);
        if (partMatch.Success)
            return int.Parse(partMatch.Groups[1].Value);

        // handle the `.rXXX` format
        var rMatch = Regex.Match(filename, @"\.r(\d+)$", RegexOptions.IgnoreCase);
        if (rMatch.Success)
            return int.Parse(rMatch.Groups[1].Value);

        // handle the `.rar` format.
        if (filename.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
            return -1;

        //  could not determine from filename
        return null;
    }

    private async Task<NzbFileStream> GetNzbFileStream()
    {
        var filesize = fileInfo.FileSize;
        filesize ??= await usenetClient.GetFileSizeAsync(fileInfo.NzbFile, ct).ConfigureAwait(false);
        return usenetClient.GetFileStream(fileInfo.NzbFile, filesize!.Value, articleBufferSize: 0);
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
        public required PartNumber PartNumber { get; init; }
        public required DateTimeOffset ReleaseDate { get; init; }

        public required string PathWithinArchive { get; init; }
        public required LongRange ByteRangeWithinPart { get; init; }
        public required AesParams? AesParams { get; init; }

        public required long FileUncompressedSize { get; init; }
    }

    public record PartNumber
    {
        public int? PartNumberFromHeader { get; init; }
        public int? PartNumberFromFilename { get; init; }
    }
}