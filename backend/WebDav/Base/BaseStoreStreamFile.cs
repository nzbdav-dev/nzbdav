namespace NzbWebDAV.WebDav.Base;

public abstract class BaseStoreStreamFile() : BaseStoreReadonlyItem
{
    protected abstract Task<Stream> GetStreamAsync(CancellationToken cancellationToken);

    public override Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken)
    {
        return GetStreamAsync(cancellationToken);
    }
}