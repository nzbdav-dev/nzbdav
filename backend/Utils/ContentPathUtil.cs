namespace NzbWebDAV.Utils;

public static class ContentPathUtil
{
    public const string ForwardSlashPrefix = "/content/";
    public const string BackslashPrefix = "/content\\";

    public static bool IsContentChildPath(string path)
    {
        return path.StartsWith(ForwardSlashPrefix, StringComparison.Ordinal)
               || path.StartsWith(BackslashPrefix, StringComparison.Ordinal);
    }

    public static string NormalizeSeparators(string path)
    {
        return path.Replace('\\', '/');
    }
}
