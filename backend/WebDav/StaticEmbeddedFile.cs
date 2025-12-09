using NzbWebDAV.Utils;
using NzbWebDAV.WebDav.Base;

namespace NzbWebDAV.WebDav;

public class StaticEmbeddedFile(string resourcePath, string name) : BaseStoreReadonlyItem
{
    public override string Name => name;
    public override string UniqueKey => resourcePath;
    public override long FileSize { get; } = EmbeddedResourceUtil.GetLength(resourcePath);
    public override DateTime CreatedAt { get; } = DateTime.Now;

    public override Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(EmbeddedResourceUtil.GetStream(resourcePath));
    }
}