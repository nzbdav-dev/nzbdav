using System.Text.RegularExpressions;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using NzbWebDAV.Queue.DeobfuscationSteps._3.GetFileInfos;
using NzbWebDAV.Streams;
using NzbWebDAV.Utils;
using SharpCompress.Common;

namespace NzbWebDAV.Queue.FileProcessors;

public class SevenZipProcessor : BaseProcessor
{
    private readonly List<GetFileInfosStep.FileInfo> _fileInfos;
    private readonly INntpClient _usenetClient;
    private readonly ConfigManager _configManager;
    private readonly string? _archivePassword;
    private readonly CancellationToken _ct;

    public SevenZipProcessor
    (
        List<GetFileInfosStep.FileInfo> fileInfos,
        INntpClient usenetClient,
        ConfigManager configManager,
        string? archivePassword,
        CancellationToken ct
    )
    {
        _fileInfos = fileInfos;
        _usenetClient = usenetClient;
        _configManager = configManager;
        _archivePassword = archivePassword;
        _ct = ct;
    }

    public override async Task<BaseProcessor.Result?> ProcessAsync(IProgress<int> progress)
    {
        try
        {
            var progress95 = progress.Scale(95, 100);
            var multipartFile = await GetMultipartFile(progress95).ConfigureAwait(false);
            await using var stream = new MultipartFileStream(multipartFile, _usenetClient);
            var sevenZipEntries = await SevenZipUtil
                .GetSevenZipEntriesAsync(stream, _archivePassword, _ct)
                .ConfigureAwait(false);

            if (sevenZipEntries.Any(x => x.CompressionType != CompressionType.None))
            {
                const string message = "Only uncompressed 7z files are supported.";
                throw new Unsupported7zCompressionMethodException(message);
            }

            if (sevenZipEntries.Any(x => x.IsEncrypted && x.IsSolid))
            {
                // TODO: Add support for solid 7z archives
                const string message = "Password-protected 7z archives cannot be solid.";
                throw new NonRetryableDownloadException(message);
            }

            return new Result()
            {
                SevenZipFiles = sevenZipEntries.Select(x => new SevenZipFile()
                {
                    PathWithinArchive = x.PathWithinArchive,
                    DavMultipartFileMeta = GetDavMultipartFileMeta(x, multipartFile),
                    ReleaseDate = _fileInfos.First().ReleaseDate,
                }).ToList(),
            };
        }
        finally
        {
            progress.Report(100);
        }
    }

    private async Task<MultipartFile> GetMultipartFile(IProgress<int> progress)
    {
        var fileInfos = await PopulateMissingFileSizes(_fileInfos, progress).ConfigureAwait(false);
        var sortedFileInfos = fileInfos.OrderBy(f => GetPartNumber(f.FileName)).ToList();
        var fileParts = new List<MultipartFile.FilePart>();
        long startInclusive = 0;
        foreach (var fileInfo in sortedFileInfos)
        {
            var nzbFile = fileInfo.NzbFile;
            var fileSize = fileInfo.FileSize ?? await _usenetClient
                .GetFileSizeAsync(nzbFile, _ct)
                .ConfigureAwait(false);
            var endExclusive = startInclusive + fileSize;
            fileParts.Add(new MultipartFile.FilePart()
            {
                NzbFile = fileInfo.NzbFile,
                ByteRange = new LongRange(startInclusive, endExclusive),
            });
            startInclusive = endExclusive;
        }

        return new MultipartFile() { FileParts = fileParts };
    }

    private async Task<List<GetFileInfosStep.FileInfo>> PopulateMissingFileSizes
    (
        List<GetFileInfosStep.FileInfo> fileInfos,
        IProgress<int> progress
    )
    {
        var missingFileSizes = new List<GetFileInfosStep.FileInfo>();
        var withFileSizes = new List<GetFileInfosStep.FileInfo>();
        foreach (var fileInfo in fileInfos)
        {
            if (fileInfo.FileSize == null) missingFileSizes.Add(fileInfo);
            else withFileSizes.Add(fileInfo);
        }

        // if all files already have file sizes,
        // then we can return immediately.
        if (missingFileSizes.Count == 0)
        {
            progress.Report(100);
            return withFileSizes;
        }

        // otherwise, let's populate the missing file sizes.
        var progressPercentage = progress.ToPercentage(missingFileSizes.Count);
        var populatedFileSizes = await missingFileSizes
            .Select(PopulateMissingFileSize)
            .WithConcurrencyAsync(_configManager.GetMaxDownloadConnections())
            .GetAllAsync(_ct, progressPercentage)
            .ConfigureAwait(false);
        progress.Report(100);
        return withFileSizes.Concat(populatedFileSizes).ToList();
    }

    private async Task<GetFileInfosStep.FileInfo> PopulateMissingFileSize(GetFileInfosStep.FileInfo fileInfo)
    {
        return fileInfo with
        {
            FileSize = await _usenetClient
                .GetFileSizeAsync(fileInfo.NzbFile, _ct)
                .ConfigureAwait(false)
        };
    }

    private static int GetPartNumber(string filename)
    {
        var match = Regex.Match(filename, @"\.7z(\.(\d+))?$", RegexOptions.IgnoreCase);
        return string.IsNullOrEmpty(match.Groups[2].Value) ? -1 : int.Parse(match.Groups[2].Value);
    }

    private DavMultipartFile.Meta GetDavMultipartFileMeta
    (
        SevenZipUtil.SevenZipEntry sevenZipEntry,
        MultipartFile multipartFile
    )
    {
        var (startIndexInclusive, startIndexByteRange) = InterpolationSearch.Find(
            sevenZipEntry.ByteRangeWithinArchive.StartInclusive,
            new LongRange(0, multipartFile.FileParts.Count),
            new LongRange(0, multipartFile.FileSize),
            guess => multipartFile.FileParts[guess].ByteRange
        );

        var (endIndexInclusive, endIndexByteRange) = InterpolationSearch.Find(
            sevenZipEntry.ByteRangeWithinArchive.EndExclusive - 1,
            new LongRange(0, multipartFile.FileParts.Count),
            new LongRange(0, multipartFile.FileSize),
            guess => multipartFile.FileParts[guess].ByteRange
        );

        var endIndexExclusive = endIndexInclusive + 1;
        var indexCount = endIndexExclusive - startIndexInclusive;
        var fileParts = Enumerable
            .Range(startIndexInclusive, indexCount)
            .Select(index =>
            {
                var partStartInclusive = index == startIndexInclusive
                    ? sevenZipEntry.ByteRangeWithinArchive.StartInclusive - startIndexByteRange.StartInclusive
                    : 0;
                var partEndExclusive = index == endIndexInclusive
                    ? sevenZipEntry.ByteRangeWithinArchive.EndExclusive - endIndexByteRange.StartInclusive
                    : multipartFile.FileParts[index].PartSize;
                var partByteCount = partEndExclusive - partStartInclusive;

                return new DavMultipartFile.FilePart()
                {
                    SegmentIds = multipartFile.FileParts[index].NzbFile.GetSegmentIds(),
                    SegmentIdByteRange = LongRange.FromStartAndSize(0, multipartFile.FileParts[index].PartSize),
                    FilePartByteRange = LongRange.FromStartAndSize(partStartInclusive, partByteCount),
                };
            })
            .ToArray();

        return new DavMultipartFile.Meta()
        {
            AesParams = sevenZipEntry.AesParams,
            FileParts = fileParts,
        };
    }

    public new class Result : BaseProcessor.Result
    {
        public required List<SevenZipFile> SevenZipFiles { get; init; }
    }

    public class SevenZipFile
    {
        public required string PathWithinArchive { get; init; }
        public required DavMultipartFile.Meta DavMultipartFileMeta { get; init; }
        public required DateTimeOffset ReleaseDate { get; init; }
    }
}