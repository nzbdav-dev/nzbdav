namespace NzbWebDAV.Utils;

public class PathUtil
{
    public static IEnumerable<string> GetAllParentDirectories(string path)
    {
        var directoryName = Path.GetDirectoryName(path);
        return !string.IsNullOrEmpty(directoryName)
            ? GetAllParentDirectories(directoryName).Prepend(directoryName)
            : [];
    }

    public static string ReplaceExtension(string path, string newExtensions)
    {
        var directoryName = Path.GetDirectoryName(path);
        var filenameWithoutExtension = Path.GetFileNameWithoutExtension(path);
        var newFilename = $"{filenameWithoutExtension}.{newExtensions.TrimStart('.')}";
        return Path.Join(directoryName, newFilename);
    }
}