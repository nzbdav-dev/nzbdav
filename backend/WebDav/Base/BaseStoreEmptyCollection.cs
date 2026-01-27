using NWebDav.Server.Stores;
using NzbWebDAV.WebDav.Requests;

namespace NzbWebDAV.WebDav.Base;

public class BaseStoreEmptyCollection(string name) : BaseStoreReadonlyCollection
{
    public override string Name => name;
    public override string UniqueKey => $"empty_folder_{name}";
    public override DateTime CreatedAt => DateTime.Now;

    protected override Task<IStoreItem?> GetItemAsync(GetItemRequest request)
    {
        return Task.FromResult<IStoreItem?>(null);
    }

    protected override Task<IStoreItem[]> GetAllItemsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IStoreItem[]>([]);
    }

    protected override bool SupportsFastMove(SupportsFastMoveRequest request)
    {
        return false;
    }
}