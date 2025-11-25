using NWebDav.Server;
using NWebDav.Server.Props;
using NWebDav.Server.Stores;
using NzbWebDAV.Streams;
using NzbWebDAV.WebDav.Requests;

namespace NzbWebDAV.WebDav.Base;

public abstract class BaseStoreCollection : IStoreCollection
{
    // abstract members
    public abstract string Name { get; }
    public abstract string UniqueKey { get; }
    public abstract DateTime CreatedAt { get; }

    protected abstract Task<StoreItemResult> CopyAsync(CopyRequest request);
    protected abstract Task<IStoreItem?> GetItemAsync(GetItemRequest request);
    protected abstract Task<IStoreItem[]> GetAllItemsAsync(CancellationToken cancellationToken);
    protected abstract Task<StoreItemResult> CreateItemAsync(CreateItemRequest request);
    protected abstract Task<StoreCollectionResult> CreateCollectionAsync(CreateCollectionRequest request);
    protected abstract bool SupportsFastMove(SupportsFastMoveRequest request);
    protected abstract Task<StoreItemResult> MoveItemAsync(MoveItemRequest request);
    protected abstract Task<DavStatusCode> DeleteItemAsync(DeleteItemRequest request);

    // private members
    private BaseStoreEmptyFileManager EmptyFileManager => BaseStoreEmptyFileManager.GetEmptyFileManager(UniqueKey);

    // interface implementation
    public IPropertyManager? PropertyManager => BaseStoreCollectionPropertyManager.Instance;
    public InfiniteDepthMode InfiniteDepthMode => InfiniteDepthMode.Rejected;

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

    public async IAsyncEnumerable<IStoreItem> GetItemsAsync(CancellationToken cancellationToken)
    {
        var allItems = await GetAllItemsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var item in allItems)
        {
            yield return item;
        }

        foreach (var item in EmptyFileManager.GetAllEmptyFiles())
            yield return item;
    }

    public async Task<IStoreItem?> GetItemAsync(string name, CancellationToken cancellationToken)
    {
        return await GetItemAsync(new GetItemRequest()
        {
            Name = name,
            CancellationToken = cancellationToken
        }).ConfigureAwait(false) ?? EmptyFileManager.Get(name);
    }

    public async Task<StoreItemResult> CreateItemAsync
    (
        string name,
        Stream stream,
        bool overwrite,
        CancellationToken cancellationToken
    )
    {
        var existingItem = await GetItemAsync(name, cancellationToken).ConfigureAwait(false);
        var probingStream = new ProbingStream(stream);

        // if the item doesn't already exist, create it
        //
        // Note: The windows webdav client appears to create files in multiple steps
        //       with "CreateItemAsync" called twice.
        // 1. CreateItemAsync is first called with an empty input stream to create an empty file.
        // 2. CreateItemAsync is then called again with actual input stream and overwrite set to true.
        // 3. If step #2 fails above, DeleteItemAsync is then called to remove the empty file created in step #1.
        if (existingItem is null)
        {
            // This handles step #1 in the note above.
            if (await probingStream.IsEmptyAsync().ConfigureAwait(false))
            {
                var emptyFile = new BaseStoreEmptyFile(name);
                EmptyFileManager.Add(emptyFile);
                return new StoreItemResult(DavStatusCode.Created, emptyFile);
            }
        }

        // this handles step #2 in the note above.
        if (existingItem is null || overwrite)
        {
            EmptyFileManager.Remove(name);
            return await CreateItemAsync(new CreateItemRequest()
            {
                Name = name,
                Stream = probingStream,
                Overwrite = overwrite,
                CancellationToken = cancellationToken
            }).ConfigureAwait(false);
        }

        return new StoreItemResult(DavStatusCode.Conflict);
    }

    public Task<StoreCollectionResult> CreateCollectionAsync
    (
        string name,
        bool overwrite,
        CancellationToken cancellationToken
    )
    {
        return CreateCollectionAsync(new CreateCollectionRequest()
        {
            Name = name,
            Overwrite = overwrite,
            CancellationToken = cancellationToken
        });
    }

    public bool SupportsFastMove(IStoreCollection destination, string destinationName, bool overwrite)
    {
        return SupportsFastMove(new SupportsFastMoveRequest()
        {
            Destination = destination,
            DestinationName = destinationName,
            Overwrite = overwrite
        });
    }

    public Task<StoreItemResult> MoveItemAsync
    (
        string sourceName,
        IStoreCollection destination,
        string destinationName,
        bool overwrite,
        CancellationToken cancellationToken
    )
    {
        return MoveItemAsync(new MoveItemRequest()
        {
            SourceName = sourceName,
            Destination = destination,
            DestinationName = destinationName,
            Overwrite = overwrite,
            CancellationToken = cancellationToken
        });
    }

    public Task<DavStatusCode> DeleteItemAsync(string name, CancellationToken cancellationToken)
    {
        if (EmptyFileManager.Remove(name))
            return Task.FromResult(DavStatusCode.Ok);

        return DeleteItemAsync(new DeleteItemRequest()
        {
            Name = name,
            CancellationToken = cancellationToken
        });
    }

    // collections (directories) do not store content themselves
    // these interface operations are not needed.
    public Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Stream.Null);
    }

    public Task<DavStatusCode> UploadFromStreamAsync(Stream source, CancellationToken cancellationToken)
    {
        return Task.FromResult(DavStatusCode.Conflict);
    }

    // helpers
    public async Task<IStoreItem?> ResolvePath(string path, CancellationToken cancellationToken)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        IStoreCollection current = this;
        for (var i = 0; i < segments.Length; i++)
        {
            var nextItem = await current.GetItemAsync(segments[i], cancellationToken).ConfigureAwait(false);
            if (nextItem is null) return null;

            if (i == segments.Length - 1)
                return nextItem;

            if (nextItem is not IStoreCollection nextCollection)
                return null;

            current = nextCollection;
        }

        return current;
    }
}