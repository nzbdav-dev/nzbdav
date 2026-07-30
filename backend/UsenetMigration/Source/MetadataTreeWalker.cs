namespace NzbWebDAV.UsenetMigration.Source;

/// <summary>
/// Enumerates <c>.meta</c> files under the Altmount metadata root. Skips the
/// <c>failed/</c> sibling under any <c>.nzbs/</c> — those are NZBs Altmount
/// rejected itself and must never surface as a phantom category.
/// </summary>
public static class MetadataTreeWalker
{
    public const string MetaExtension = ".meta";

    /// <summary>
    /// Lazily yields absolute paths of every <c>.meta</c> file beneath
    /// <paramref name="metadataRoot"/>, in a stable directory-first order,
    /// excluding any <c>failed</c> directory segment.
    /// </summary>
    public static IEnumerable<string> EnumerateMetaFiles(string metadataRoot)
    {
        if (!Directory.Exists(metadataRoot))
            yield break;

        var stack = new Stack<string>();
        stack.Push(metadataRoot);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            string[] subdirs;
            try
            {
                subdirs = Directory.GetDirectories(dir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            Array.Sort(subdirs, StringComparer.Ordinal);
            foreach (var sub in subdirs)
            {
                if (IsExcludedDir(sub)) continue;
                stack.Push(sub);
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*" + MetaExtension);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            Array.Sort(files, StringComparer.Ordinal);
            foreach (var file in files)
                yield return file;
        }
    }

    private static bool IsExcludedDir(string dirPath)
    {
        var name = Path.GetFileName(dirPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(name, StorePathParser.FailedDirName, StringComparison.Ordinal);
    }
}
