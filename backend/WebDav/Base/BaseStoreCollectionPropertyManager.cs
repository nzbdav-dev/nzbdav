using System.Xml.Linq;
using NWebDav.Server;
using NWebDav.Server.Props;

namespace NzbWebDAV.WebDav.Base;

internal class DavIsCollection : DavString<BaseStoreCollection>
{
    public static readonly XName PropertyName = WebDavNamespaces.DavNs + "iscollection";
    public override XName Name => PropertyName;
}

public class BaseStoreCollectionPropertyManager() : PropertyManager<BaseStoreCollection>(DavProperties)
{
    private static readonly XElement DavResourceType = new(WebDavNamespaces.DavNs + "collection");

    private static readonly DavProperty<BaseStoreCollection>[] DavProperties =
    [
        new DavDisplayName<BaseStoreCollection>
        {
            Getter = collection => collection.Name
        },
        new DavGetResourceType<BaseStoreCollection>
        {
            Getter = _ => [DavResourceType]
        },
        new DavGetLastModified<BaseStoreCollection>
        {
            Getter = x => x.CreatedAt
        },
        new Win32FileAttributes<BaseStoreCollection>
        {
            Getter = _ => FileAttributes.Directory
        },
        new DavQuotaAvailableBytes<BaseStoreCollection>()
        {
            Getter = _ => long.MaxValue
        },
        new DavQuotaUsedBytes<BaseStoreCollection>()
        {
            Getter = _ => 0
        },
        new DavGetContentLength<BaseStoreCollection>
        {
            Getter = _ => 0
        },
        new DavIsCollection
        {
            Getter = _ => "1"
        }
    ];

    public static readonly BaseStoreCollectionPropertyManager Instance = new();
}