using NzbWebDAV.Utils;

namespace NzbWebDAV.Database;

public class BlobStore
{
    private static readonly string ConfigPath = DavDatabaseContext.ConfigPath;
    private static readonly Lock LockObj = new();

    private static string GetBlobPath(Guid id)
    {
        var guidStr = id.ToString("N"); // Without hyphens
        var firstTwo = guidStr[..2];
        var nextTwo = guidStr.Substring(2, 2);
        var fileName = id.ToString(); // With hyphens for readability

        return Path.Combine(ConfigPath, "blobs", firstTwo, nextTwo, fileName);
    }

    public static async Task WriteBlob(Guid id, Stream stream)
    {
        var blobPath = GetBlobPath(id);
        var directory = Path.GetDirectoryName(blobPath);

        // Acquire file handle inside lock to prevent race condition where
        // directory gets deleted between CreateDirectory and File.Create
        FileStream fileStream;
        lock (LockObj)
        {
            Directory.CreateDirectory(directory!);
            fileStream = File.Create(blobPath);
        }

        // Write data outside lock to avoid blocking other operations during I/O
        await using (fileStream)
        {
            await stream.CopyToAsync(fileStream);
        }
    }

    public static Stream? ReadBlob(Guid id)
    {
        var blobPath = GetBlobPath(id);
        return File.Exists(blobPath) ? File.OpenRead(blobPath) : null;
    }

    public static void Delete(Guid id)
    {
        var blobPath = GetBlobPath(id);

        // Delete the file
        if (File.Exists(blobPath))
        {
            File.Delete(blobPath);
        }

        lock (LockObj)
        {
            // Clean up empty directories
            // Structure: CONFIG_PATH/blobs/{firstTwo}/{nextTwo}/{fileName}
            var nextTwoDir = Path.GetDirectoryName(blobPath);
            var firstTwoDir = Path.GetDirectoryName(nextTwoDir);

            TryDeleteEmptyDirectory(nextTwoDir);
            TryDeleteEmptyDirectory(firstTwoDir);
        }
    }

    private static void TryDeleteEmptyDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory)) return;
        if (!Directory.Exists(directory)) return;
        if (!IsDirectoryEmpty(directory)) return;
        Directory.Delete(directory, recursive: false);
    }

    private static bool IsDirectoryEmpty(string path)
    {
        return !Directory.EnumerateFileSystemEntries(path).Any();
    }
}