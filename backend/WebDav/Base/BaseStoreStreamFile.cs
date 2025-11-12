using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Config;
using NzbWebDAV.Extensions;
using NzbWebDAV.Streams;

namespace NzbWebDAV.WebDav.Base;

public abstract class BaseStoreStreamFile(ConfigManager configManager) : BaseStoreReadonlyItem
{
    public abstract Task<Stream> GetStreamAsync(CancellationToken cancellationToken);

    public override async Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken)
    {
        var stream = await GetStreamAsync(cancellationToken).ConfigureAwait(false);
        var connectionsPerStream = configManager.GetConnectionsPerStream();
        var semaphoreContext = new ConnectionSemaphoreContext(connectionsPerStream);
        var scopedContext = cancellationToken.SetScopedContext(semaphoreContext);
        return new DisposableCallbackStream
        (
            stream,
            () =>
            {
                semaphoreContext.Dispose();
                scopedContext.Dispose();
            }
        );
    }
}