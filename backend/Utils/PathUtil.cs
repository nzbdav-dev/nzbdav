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

    /// <summary>
    /// Validates that a path doesn't contain path traversal sequences and is a valid absolute path.
    /// </summary>
    public static bool IsPathSafe(string path)
    {
        try
        {
            // Get the full path to normalize it
            var fullPath = Path.GetFullPath(path);

            // Check if the normalized path equals the original (prevents traversal sequences)
            // This catches cases like "/some/path/../../../etc/passwd"
            return fullPath == path || Path.GetFullPath(Path.GetDirectoryName(path)!) == Path.GetDirectoryName(path);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Safely deletes a file with proper error handling. Returns true if successful, false otherwise.
    /// </summary>
    public static bool SafeDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
            return false;
        }
        catch (IOException)
        {
            // File might be in use or already deleted
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Insufficient permissions
            return false;
        }
    }
}