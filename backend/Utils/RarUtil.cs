using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using NzbWebDAV.Streams;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Common.Rar.Headers;
using SharpCompress.IO;
using SharpCompress.Readers;

namespace NzbWebDAV.Utils;

public static class RarUtil
{
    public static async Task<List<IRarHeader>> GetRarHeadersAsync(Stream stream, CancellationToken ct)
    {
        await using var cancellableStream = new CancellableStream(stream, ct);
        return await Task.Run(() => GetRarHeaders(cancellableStream), ct);
    }

    private static List<IRarHeader> GetRarHeaders(Stream stream)
    {
        try
        {
            var headerFactory = new RarHeaderFactory(StreamingMode.Seekable, new ReaderOptions());
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

                // add the headers
                headers.Add(header);
            }

            return headers;
        }
        catch (CryptographicException ex)
        {
            throw new PasswordProtectedRarException("Password-protected RARs are not supported");
        }
    }
}