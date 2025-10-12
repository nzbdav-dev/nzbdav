using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Queue.DeobfuscationSteps._3.GetFileInfos;
using NzbWebDAV.Utils;
using Serilog;
using Usenet.Nzb;

namespace NzbWebDAV.Queue.FileProcessors;

public class FileProcessor(
    GetFileInfosStep.FileInfo fileInfo,
    UsenetStreamingClient usenet,
    CancellationToken ct
) : BaseProcessor
{
    public override async Task<BaseProcessor.Result?> ProcessAsync()
    {
        try
        {
            return new Result()
            {
                NzbFile = fileInfo.NzbFile,
                FileName = fileInfo.FileName,
                FileSize = fileInfo.FileSize ?? await usenet.GetFileSizeAsync(fileInfo.NzbFile, ct),
                ReleaseDate = fileInfo.ReleaseDate,
            };
        }

        // Ignore missing articles if it's not a video file.
        // In that case, simply skip the file altogether.
        catch (UsenetArticleNotFoundException) when (!FilenameUtil.IsVideoFile(fileInfo.FileName))
        {
            Log.Warning($"File `{fileInfo.FileName}` has missing articles. Skipping file since it is not a video.");
            return null;
        }
    }

    public new class Result : BaseProcessor.Result
    {
        public required NzbFile NzbFile { get; init; }
        public required string FileName { get; init; }
        public required long FileSize { get; init; }
        public required DateTimeOffset ReleaseDate { get; init; }
    }
}