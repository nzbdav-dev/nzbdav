using System.Text;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.WebDav.Base;

namespace NzbWebDAV.WebDav;

public class DatabaseStoreSymlinkFile(DavItem davFile, ConfigManager configManager) : BaseStoreReadonlyItem
{
    public override string Name => davFile.Name + ".rclonelink";
    public override string UniqueKey => davFile.Id + ".rclonelink";
    public override long FileSize => ContentBytes.Length;
    public override DateTime CreatedAt => davFile.CreatedAt;

    private byte[] ContentBytes => Encoding.UTF8.GetBytes(GetTargetPath());

    public override Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<Stream>(new MemoryStream(ContentBytes));
    }

    private string GetTargetPath()
    {
        return GetTargetPath(davFile.Id, configManager.GetRcloneMountDir());
    }

    public static string GetTargetPath(Guid davItemId, string mountDir, char? pathSeparator = null)
    {
        var pathParts = davItemId.GetFiveLengthPrefix()
            .Select(x => x.ToString())
            .Prepend(DavItem.IdsFolder.Name)
            .Prepend(mountDir)
            .Append(davItemId.ToString())
            .ToArray();
        return string.Join(pathSeparator ?? Path.DirectorySeparatorChar, pathParts);
    }
}