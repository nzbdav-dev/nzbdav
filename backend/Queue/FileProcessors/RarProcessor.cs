using System.Text.RegularExpressions;
using NzbWebDAV.Clients;
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
            return new Result()
            {
                NzbFile = fileInfo.NzbFile,
                PartSize = cancellableStream.Length,
                ArchiveName = GetArchiveName(),
                PartNumber = GetPartNumber(),
                StoredFileSegments = (await Task.Run(() => GetRarHeaders(cancellableStream)))
                    .Select(x => new StoredFileSegment()
                    {
                        PathWithinArchive = x.GetFileName(),
                        Offset = x.GetDataStartPosition(),
                        ByteCount = x.GetAdditionalDataSize(),
                    }).ToArray(),
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

    private int GetPartNumber()
    {
        // handle the `.partXXX.rar` format
        var partMatch = Regex.Match(fileInfo.FileName, @"\.part(\d+)\.rar$", RegexOptions.IgnoreCase);
        if (partMatch.Success) return int.Parse(partMatch.Groups[1].Value);

        // handle the `.rXXX` format
        var rMatch = Regex.Match(fileInfo.FileName, @"\.r(\d+)$", RegexOptions.IgnoreCase);
        if (rMatch.Success) return int.Parse(rMatch.Groups[1].Value);

        // handle the `.rar` format.
        return -1;
    }

    private List<IRarHeader> GetRarHeaders(Stream stream)
    {
        ct.ThrowIfCancellationRequested();
        var headerFactory = new RarHeaderFactory(StreamingMode.Seekable, new ReaderOptions());
        var headers = new List<IRarHeader>();
        foreach (var header in headerFactory.ReadHeaders(stream))
        {
            ct.ThrowIfCancellationRequested();

            // we only care about file headers
            if (header.HeaderType != HeaderType.File || header.IsDirectory() || header.GetFileName() == "QO") continue;

            // we only support stored files (compression method m0).
            if (header.GetCompressionMethod() != 0)
                throw new UnsupportedRarCompressionMethodException(
                    "Only rar files with compression method m0 are supported.");

            // add the headers
            headers.Add(header);

            // break early if we think there are no more headers.
            var left = stream.Length - (header.GetDataStartPosition() + header.GetCompressedSize());
            if (left < 1000) break;
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
        public NzbFile NzbFile { get; init; } = null!;
        public long PartSize { get; init; }
        public string ArchiveName { get; init; } = null!;
        public int PartNumber { get; init; }
        public StoredFileSegment[] StoredFileSegments { get; init; } = [];
    }

    public class StoredFileSegment
    {
        public string PathWithinArchive { get; init; } = null!;
        public long Offset { get; init; }
        public long ByteCount { get; init; }
    }
}