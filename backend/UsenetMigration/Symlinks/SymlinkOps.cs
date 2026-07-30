namespace NzbWebDAV.UsenetMigration.Symlinks;

/// <summary>
/// The minimal filesystem surface needed to apply symlink rewrites while preserving
/// backup-first, drift-guard, idempotency, and never-delete behavior.
/// </summary>
public interface ISymlinkOps
{
    /// <summary>The current symlink target at <paramref name="path"/>, or null if the
    /// path is absent or not a symlink (even a broken symlink returns its target).
    /// The path must be contained by <paramref name="libraryRoot"/> without traversing
    /// a symlink or reparse point in its parent chain.</summary>
    string? ReadLink(string libraryRoot, string path);

    /// <summary>
    /// Point the symlink at <paramref name="path"/> to <paramref name="target"/>,
    /// replacing an existing symlink there. Removes only the link inode — never the
    /// pointed-at content — and refuses to touch a path that is a real (non-symlink)
    /// file or directory.
    /// </summary>
    void CreateOrReplaceSymlink(string libraryRoot, string path, string target);
}

/// <summary>Production <see cref="ISymlinkOps"/> over the real filesystem.</summary>
public sealed class RealSymlinkOps : ISymlinkOps
{
    public static readonly RealSymlinkOps Instance = new();

    /// <summary>Test-only fault injection at the final leaf validation boundary.</summary>
    internal Action<string>? BeforeFinalLeafValidation { get; init; }

    public string? ReadLink(string libraryRoot, string path)
    {
        var safePath = SymlinkPathGuard.RequireSafeParentChain(libraryRoot, path);
        return ReadLinkUnchecked(safePath);
    }

    public void CreateOrReplaceSymlink(string libraryRoot, string path, string target)
    {
        var safePath = SymlinkPathGuard.RequireSafeParentChain(libraryRoot, path);
        var existing = ReadLinkUnchecked(safePath);

        // Re-check after inspecting the leaf so an already-swapped parent is
        // rejected before the following delete.
        safePath = SymlinkPathGuard.RequireSafeParentChain(libraryRoot, safePath);
        if (existing is not null)
        {
            BeforeFinalLeafValidation?.Invoke(safePath);
            var current = ReadLinkUnchecked(safePath);
            if (current is null)
            {
                throw new IOException(
                    $"Refusing to replace '{safePath}' because it is no longer the expected symlink.");
            }
            if (!string.Equals(current, existing, SymlinkPathGuard.PathComparison))
            {
                throw new IOException(
                    $"Refusing to replace '{safePath}' because its symlink target changed during replacement.");
            }

            // Delete only the link inode. Deleting a symlink never recurses into or
            // removes its target content.
            if (Directory.Exists(safePath) && new DirectoryInfo(safePath).LinkTarget is not null)
                Directory.Delete(safePath);
            else
                File.Delete(safePath);
        }
        else if (File.Exists(safePath) || Directory.Exists(safePath))
        {
            // A real file or directory lives here, so never replace it.
            throw new IOException($"Refusing to replace non-symlink at '{safePath}'.");
        }

        // The delete/create boundary cannot be expressed as one managed filesystem
        // operation. Validate the still-existing parent again immediately before
        // creating the replacement link.
        safePath = SymlinkPathGuard.RequireSafeParentChain(libraryRoot, safePath);
        File.CreateSymbolicLink(safePath, target);
    }

    private static string? ReadLinkUnchecked(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReparsePoint) == 0)
                return null;
            return attrs.HasFlag(FileAttributes.Directory)
                ? new DirectoryInfo(path).LinkTarget
                : new FileInfo(path).LinkTarget;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}

internal static class SymlinkPathGuard
{
    internal static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    internal static string RequireRealLibraryRoot(string libraryRoot)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot))
            throw new IOException("The configured Library Root is missing.");

        var root = Path.GetFullPath(libraryRoot);
        EnsureRealDirectory(root, "The configured Library Root");
        return root;
    }

    internal static string RequireSafeParentChain(string libraryRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new IOException("The symlink path is missing.");

        var root = RequireRealLibraryRoot(libraryRoot);
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == "." || IsOutsideRoot(relative))
            throw new IOException($"Refusing to access symlink outside the configured Library Root: '{fullPath}'.");

        var parent = Path.GetDirectoryName(fullPath)
                     ?? throw new IOException($"The symlink path has no parent directory: '{fullPath}'.");
        var relativeParent = Path.GetRelativePath(root, parent);
        if (relativeParent != ".")
        {
            var current = root;
            foreach (var segment in relativeParent.Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                EnsureRealDirectory(current, "Symlink parent directory");
            }
        }

        return fullPath;
    }

    private static bool IsOutsideRoot(string relative) =>
        Path.IsPathRooted(relative)
        || relative == ".."
        || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);

    private static void EnsureRealDirectory(string path, string description)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new IOException($"{description} does not exist: '{path}'.", e);
        }

        if (!attributes.HasFlag(FileAttributes.Directory))
            throw new IOException($"{description} is not a directory: '{path}'.");
        if (attributes.HasFlag(FileAttributes.ReparsePoint) || new DirectoryInfo(path).LinkTarget is not null)
            throw new IOException($"{description} is a symbolic link or reparse point: '{path}'.");
    }
}
