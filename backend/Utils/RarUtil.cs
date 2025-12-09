using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Streams;
using Serilog;
using SharpCompress.Common.Rar.Headers;
using SharpCompress.IO;
using SharpCompress.Readers;

namespace NzbWebDAV.Utils;

public static class RarUtil
{
    public static async Task<List<IRarHeader>> GetRarHeadersAsync
    (
        Stream stream,
        string? password,
        CancellationToken ct
    )
    {
        await using var cancellableStream = new CancellableStream(stream, ct);
        return await Task.Run(() => GetRarHeaders(cancellableStream, password), ct).ConfigureAwait(false);
    }

    private static List<IRarHeader> GetRarHeaders(Stream stream, string? password)
    {
        try
        {
            var readerOptions = new ReaderOptions() { Password = password };
            var headerFactory = new RarHeaderFactory(StreamingMode.Seekable, readerOptions);
            var headers = new List<IRarHeader>();
            foreach (var header in headerFactory.ReadHeaders(stream))
            {
                // add archive headers
                if (header.HeaderType is HeaderType.Archive or HeaderType.EndArchive)
                {
                    headers.Add(header);
                    continue;
                }

                // skip comments
                if (header.HeaderType == HeaderType.Service)
                {
                    if (header.GetFileName() == "CMT")
                    {
                        var buffer = new byte[header.GetCompressedSize()];
                        _ = stream.Read(buffer, 0, buffer.Length);
                    }

                    continue;
                }

                // we only care about file headers
                if (header.HeaderType != HeaderType.File || header.IsDirectory() ||
                    header.GetFileName() == "QO") continue;

                // we only support stored files (compression method m0).
                if (header.GetCompressionMethod() != 0)
                    throw new UnsupportedRarCompressionMethodException(
                        "Only rar files with compression method m0 are supported.");

                // TODO: support solid archives
                if (header.GetIsEncrypted() && header.GetIsSolid())
                    throw new Exception("Password-protected rar archives cannot be solid.");

                // add the headers
                headers.Add(header);
            }

            return headers;
        }
        catch (Exception e) when (e.TryGetInnerException(out UsenetArticleNotFoundException missingArticleException))
        {
            throw missingArticleException;
        }
    }
}