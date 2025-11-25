using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Extensions;
using NzbWebDAV.Streams;

namespace NzbWebDAV.WebDav.Base;

public abstract class BaseStoreStreamFile() : BaseStoreReadonlyItem
{
    public abstract Task<Stream> GetStreamAsync(CancellationToken cancellationToken);

    public override async Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken)
    {
        var stream = await GetStreamAsync(cancellationToken).ConfigureAwait(false);
        var scopedContext = cancellationToken.SetScopedContext(new LastSuccessfulProviderContext());
        return new DisposableCallbackStream
        (
            stream,
            () => scopedContext.Dispose()
        );
    }
}