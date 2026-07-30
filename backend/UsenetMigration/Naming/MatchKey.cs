using NzbWebDAV.Utils;

namespace NzbWebDAV.UsenetMigration.Naming;

/// <summary>
/// The leaf-matching key used when planning symlink rewrites. A source file is matched to a live
/// <c>DavItem</c> by comparing this normalisation of the Altmount virtual-file
/// basename against the same normalisation of the DavItem leaf name.
///
/// <para>
/// NzbDAV names every leaf via <c>SanitizeDavName == PathSanitizer.SanitizeComponent</c>
/// (see <c>BaseAggregator</c>), so this key uses the identical transform. It also case-folds
/// because obfuscation/deobfuscation can flip case and the match should tolerate it.
/// </para>
///
/// <para>
/// <c>SanitizeComponent</c> branches on the runtime-mutable
/// <c>PathSanitizer.IsWindowsSafePathsEnabled</c> global. This method pins
/// <c>windowsSafe: true</c> — NzbDAV's production default — so the stored key is
/// deterministic and never silently depends on when the scan happened. The rare
/// case where content was imported under <c>windowsSafe: false</c> (so a special-char
/// DavItem leaf name diverges) is absorbed by the planner's single-leaf fallback,
/// not by this key. Both the scan and matcher use this method so stored and live
/// keys cannot drift apart.
/// </para>
/// </summary>
public static class MatchKey
{
    public static string ForLeaf(string fileName) =>
        PathSanitizer.SanitizeComponent(fileName, windowsSafe: true).ToLowerInvariant();

    public static string ForRelativePath(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(ForLeaf(segment));
        }
        return string.Join('/', segments);
    }
}
