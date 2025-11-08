using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NzbWebDAV.Utils;

public static class SymlinkAndStrmUtil
{
    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public static IEnumerable<ISymlinkOrStrmInfo> GetAllSymlinksAndStrms(string directoryPath)
    {
        return IsLinux
            ? GetAllSymlinksAndStrmsLinux(directoryPath)
            : GetAllSymlinksAndStrmsWindows(directoryPath);
    }

    private static IEnumerable<ISymlinkOrStrmInfo> GetAllSymlinksAndStrmsLinux(string directoryPath)
    {
        const string command =
            """
            find . \( -type l -o -name '*.strm' \) -print0 | xargs -0 sh -c '
              for path in \"$@\"; do
                echo \"$path\"
                if [ \"${path##*.}\" = \"strm\" ]; then
                  echo \"$(cat \"$path\")\"
                else
                  echo \"$(readlink \"$path\")\"
                fi
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
            var filePath = process.StandardOutput.ReadLine();
            if (filePath == null) break;
            var target = process.StandardOutput.ReadLine();
            if (target == null) break;

            if (filePath.ToLower().EndsWith(".strm"))
            {
                yield return new StrmInfo()
                {
                    StrmPath = Path.GetFullPath(filePath, directoryPath),
                    TargetUrl = target
                };
            }
            else
            {
                yield return new SymlinkInfo()
                {
                    SymlinkPath = Path.GetFullPath(filePath, directoryPath),
                    TargetPath = target
                };
            }
        }
    }

    private static IEnumerable<ISymlinkOrStrmInfo> GetAllSymlinksAndStrmsWindows(string directoryPath)
    {
        return Directory.EnumerateFileSystemEntries(directoryPath, "*", SearchOption.AllDirectories)
            .Select(x => new FileInfo(x))
            .Select(GetSymlinkOrStrmInfo)
            .Where(x => x != null)
            .Select(x => x!);
    }

    public static ISymlinkOrStrmInfo? GetSymlinkOrStrmInfo(FileInfo x)
    {
        return IsStrm(x) ? new StrmInfo() { StrmPath = x.FullName, TargetUrl = File.ReadAllText(x.FullName) }
            : IsSymLink(x) ? new SymlinkInfo() { SymlinkPath = x.FullName, TargetPath = x.LinkTarget! }
            : null;
    }

    private static bool IsStrm(FileInfo x) =>
        x.Extension.Equals(".strm", StringComparison.CurrentCultureIgnoreCase);

    private static bool IsSymLink(FileInfo x) =>
        x.Attributes.HasFlag(FileAttributes.ReparsePoint) && x.LinkTarget is not null;

    public interface ISymlinkOrStrmInfo;

    public struct SymlinkInfo : ISymlinkOrStrmInfo
    {
        public required string SymlinkPath;
        public required string TargetPath;
    }

    public struct StrmInfo : ISymlinkOrStrmInfo
    {
        public required string StrmPath;
        public required string TargetUrl;
    }
}