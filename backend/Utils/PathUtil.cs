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
}