using System.Xml.Linq;
using Microsoft.AspNetCore.StaticFiles;
using NWebDav.Server;
using NWebDav.Server.Props;

namespace NzbWebDAV.WebDav.Base;

internal class DavIsCollectionItem : DavString<BaseStoreItem>
{
    public static readonly XName PropertyName = WebDavNamespaces.DavNs + "iscollection";
    public override XName Name => PropertyName;
}

public class BaseStoreItemPropertyManager() : PropertyManager<BaseStoreItem>(DavProperties)
{
    private static readonly FileExtensionContentTypeProvider MimeTypeProvider = new();
    private static readonly XElement DavResourceType = new(WebDavNamespaces.DavNs + "item");

    private static readonly DavProperty<BaseStoreItem>[] DavProperties =
    [
        new DavDisplayName<BaseStoreItem>
        {
            Getter = item => item.Name
        },
        new DavGetContentLength<BaseStoreItem>
        {
            Getter = item => item.FileSize
        },
        new DavGetContentType<BaseStoreItem>
        {
            Getter = item => !MimeTypeProvider.TryGetContentType(item.Name, out var mimeType)
                ? "application/octet-stream"
                : mimeType
        },
        new DavGetLastModified<BaseStoreItem>
        {
            Getter = x => x.CreatedAt
        },
        new Win32FileAttributes<BaseStoreItem>
        {
            Getter = _ => FileAttributes.Normal
        },
        new DavGetResourceType<BaseStoreItem>
        {
            Getter = _ => [DavResourceType]
        },
        new DavIsCollectionItem
        {
            Getter = _ => "0"
        }
    ];

    public static readonly BaseStoreItemPropertyManager Instance = new();
}