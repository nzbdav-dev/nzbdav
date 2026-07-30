using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NzbWebDAV.UsenetMigration.Symlinks;

/// <summary>
/// Migration-only library traversal. This is intentionally isolated from the
/// shared symlink/STRM walker used by orphan cleanup and maintenance tasks.
/// </summary>
internal static class MigrationSymlinkUtil
{
    private const int MaxStderrChars = 4096;

    internal static LibraryWalkResult GetAllSymlinks(
        string directoryPath,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return OperatingSystem.IsLinux()
            ? GetAllSymlinksLinux(directoryPath, ct)
            : GetAllSymlinksManaged(directoryPath, ct);
    }

    private static LibraryWalkResult GetAllSymlinksLinux(
        string directoryPath,
        CancellationToken ct)
    {
        var startInfo = CreateLinuxFindStartInfo(directoryPath);
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Unable to start migration symlink scan.");
        using var cancellationRegistration = ct.Register(
            static state => TryKill((Process)state!),
            process);

        // Drain stderr while stdout is consumed so a large number of traversal
        // errors cannot fill the process pipe and deadlock the scan.
        var stderrBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderrBuilder)
            {
                if (stderrBuilder.Length >= MaxStderrChars) return;
                if (stderrBuilder.Length > 0)
                    stderrBuilder.Append('\n');

                var remaining = MaxStderrChars - stderrBuilder.Length;
                stderrBuilder.Append(e.Data.Length <= remaining ? e.Data : e.Data[..remaining]);
            }
        };
        process.BeginErrorReadLine();

        // This walk is a census: every entry find reports is returned as either
        // readable or unreadable. If traversal itself fails, throw rather than
        // handing callers a partial result.
        var links = new List<SymlinkPair>();
        var unreadable = new List<UnreadableLink>();
        try
        {
            // Read raw bytes from find -print0. StreamReader/UTF-8 decoding can
            // mis-handle arbitrary Linux filenames and stall the census.
            var stdout = process.StandardOutput.BaseStream;
            while (ReadNullTerminated(stdout, ct) is { } symlinkPath)
            {
                ct.ThrowIfCancellationRequested();
                var fullPath = Path.GetFullPath(symlinkPath);
                try
                {
                    var target = new FileInfo(fullPath).LinkTarget;
                    if (target is not null)
                        links.Add(new SymlinkPair(fullPath, target));
                    else
                        unreadable.Add(new UnreadableLink(fullPath, DescribeUnreadable(fullPath)));
                }
                catch (Exception e)
                {
                    unreadable.Add(new UnreadableLink(fullPath, e.Message));
                }
            }

            if (!process.WaitForExit(60_000))
            {
                TryKill(process);
                throw new TimeoutException("Migration symlink scan timed out waiting for find to exit.");
            }

            ct.ThrowIfCancellationRequested();
        }
        catch
        {
            TryKill(process);
            throw;
        }

        if (process.ExitCode != 0)
        {
            string stderr;
            lock (stderrBuilder)
                stderr = stderrBuilder.ToString();

            throw new InvalidOperationException(
                $"Migration symlink scan failed with exit code {process.ExitCode}" +
                (string.IsNullOrWhiteSpace(stderr) ? "." : $": {stderr}"));
        }

        return new LibraryWalkResult(links, unreadable);
    }

    /// <summary>
    /// Builds the Linux traversal process without a command shell. The selected
    /// root is passed to find as one opaque argv value.
    /// </summary>
    internal static ProcessStartInfo CreateLinuxFindStartInfo(string directoryPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "find",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // -H permits a symlink supplied as the root while still avoiding symlink
        // traversal below that root. SymlinkPathGuard rejects such roots in the
        // production planner, but retaining the behavior keeps this helper robust.
        startInfo.ArgumentList.Add("-H");
        startInfo.ArgumentList.Add(Path.GetFullPath(directoryPath));
        startInfo.ArgumentList.Add("-type");
        startInfo.ArgumentList.Add("l");
        startInfo.ArgumentList.Add("-print0");
        return startInfo;
    }

    private static LibraryWalkResult GetAllSymlinksManaged(
        string directoryPath,
        CancellationToken ct)
    {
        var links = new List<SymlinkPair>();
        var unreadable = new List<UnreadableLink>();
        foreach (var path in Directory.EnumerateFileSystemEntries(
                     directoryPath,
                     "*",
                     SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            try
            {
                if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;

                var target = info.LinkTarget;
                if (target is not null)
                    links.Add(new SymlinkPair(info.FullName, target));
                else
                    unreadable.Add(new UnreadableLink(info.FullName, DescribeUnreadable(info.FullName)));
            }
            catch (Exception e)
            {
                unreadable.Add(new UnreadableLink(info.FullName, e.Message));
            }
        }

        return new LibraryWalkResult(links, unreadable);
    }

    private static string DescribeUnreadable(string fullPath)
    {
        try
        {
            return File.ResolveLinkTarget(fullPath, returnFinalTarget: false) is null
                ? "The path is no longer a symlink."
                : "The link target could not be read.";
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    private static string? ReadNullTerminated(Stream stream, CancellationToken ct)
    {
        var bytes = new List<byte>(256);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var next = stream.ReadByte();
            if (next < 0)
                return bytes.Count == 0 ? null : DecodePathBytes(bytes);
            if (next == 0)
                return DecodePathBytes(bytes);
            bytes.Add((byte)next);
        }
    }

    /// <summary>
    /// Decodes find -print0 paths as UTF-8 with replacement so non-UTF-8
    /// filenames become unreadable entries instead of stalling StreamReader.
    /// </summary>
    private static string DecodePathBytes(List<byte> bytes) =>
        Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(bytes));

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }
        catch (Win32Exception)
        {
            // Best effort during cancellation or exceptional cleanup.
        }
    }
}
