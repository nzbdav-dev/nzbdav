using NWebDav.Server;
using NWebDav.Server.Props;
using NWebDav.Server.Stores;
using NzbWebDAV.WebDav.Requests;

namespace NzbWebDAV.WebDav.Base;

public abstract class BaseStoreItem : IStoreItem
{
    // abstract members
    public abstract string Name { get; }
    public abstract string UniqueKey { get; }
    public abstract long FileSize { get; }
    public abstract DateTime CreatedAt { get; }
    public abstract Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken);
    protected abstract Task<DavStatusCode> UploadFromStreamAsync(UploadFromStreamRequest request);
    protected abstract Task<StoreItemResult> CopyAsync(CopyRequest request);

    // NZB blob ID for downloading the original NZB file; null for items without one
    public virtual Guid? NzbBlobId => null;

    // interface implementation
    public IPropertyManager? PropertyManager => BaseStoreItemPropertyManager.Instance;

    public Task<DavStatusCode> UploadFromStreamAsync(Stream source, CancellationToken cancellationToken)
    {
        return UploadFromStreamAsync(new UploadFromStreamRequest()
        {
            Source = source,
            CancellationToken = cancellationToken
        });
    }

    public Task<StoreItemResult> CopyAsync
    (
        IStoreCollection destination,
        string name,
        bool overwrite,
        CancellationToken cancellationToken
    )
    {
        return CopyAsync(new CopyRequest()
        {
            Destination = destination,
            Name = name,
            Overwrite = overwrite,
            CancellationToken = cancellationToken
        });
    }
}