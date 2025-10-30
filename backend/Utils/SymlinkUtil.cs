using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NzbWebDAV.Utils;

public static class SymlinkUtil
{
    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public static IEnumerable<SymlinkInfo> GetAllSymlinks(string directoryPath)
    {
        return IsLinux
            ? GetAllSymlinksLinux(directoryPath)
            : GetAllSymlinksWindows(directoryPath);
    }

    private static IEnumerable<SymlinkInfo> GetAllSymlinksLinux(string directoryPath)
    {
        const string command =
            """
            find . -type l -print0 | xargs -0 sh -c '
              for link_path in \"$@\"; do
                echo \"$link_path\"
                echo \"$(readlink \"$link_path\")\"
              done
            ' sh
            """;

        var escapedDirectory = directoryPath.Replace("'", "'\"'\"'");
        var startInfo = new ProcessStartInfo
        {
            FileName = "sh",
            Arguments = $"-c \"cd '{escapedDirectory}' && {command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)!;
        while (process.StandardOutput.EndOfStream == false)
        {
            var symlinkPath = process.StandardOutput.ReadLine();
            if (symlinkPath == null) break;
            var targetPath = process.StandardOutput.ReadLine();
            if (targetPath == null) break;

            yield return new SymlinkInfo()
            {
                SymlinkPath = Path.GetFullPath(symlinkPath, directoryPath),
                TargetPath = targetPath
            };
        }
    }

    private static IEnumerable<SymlinkInfo> GetAllSymlinksWindows(string directoryPath)
    {
        return Directory.EnumerateFileSystemEntries(directoryPath, "*", SearchOption.AllDirectories)
            .Select(x => new FileInfo(x))
            .Where(x => x.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .Where(x => x.LinkTarget is not null)
            .Select(x => new SymlinkInfo() { SymlinkPath = x.FullName, TargetPath = x.LinkTarget! });
    }

    public struct SymlinkInfo
    {
        public string SymlinkPath;
        public string TargetPath;
    }
}