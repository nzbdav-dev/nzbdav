using Microsoft.AspNetCore.StaticFiles;

namespace NzbWebDAV.Utils;

public static class ContentTypeUtil
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider;

    static ContentTypeUtil()
    {
        // ReSharper disable once UseObjectOrCollectionInitializer
        ContentTypeProvider = new FileExtensionContentTypeProvider();
        ContentTypeProvider.Mappings[".flac"] = "audio/flac";
    }

    public static string GetContentType(string fileName)
    {
        return !ContentTypeProvider.TryGetContentType(Path.GetFileName(fileName), out var contentType)
            ? "application/octet-stream"
            : contentType;
    }
}