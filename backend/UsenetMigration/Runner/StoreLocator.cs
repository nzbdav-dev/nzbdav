using NzbWebDAV.UsenetMigration.Source;

namespace NzbWebDAV.UsenetMigration.Runner;

/// <summary>
/// Resolves a release's <c>store_ref</c> to an on-disk <c>.nzbz</c> path. The ref
/// is authoritative and normally read directly; when it was authored on another
/// host (a copied library), it is remapped under the configured store root by its
/// <c>.nzbs/…</c> suffix. Returns null when no readable file is found (⇒
/// <c>store_missing</c>).
/// </summary>
public static class StoreLocator
{
    public static string? Resolve(string storeRef, string? storeRoot)
    {
        if (File.Exists(storeRef)) return storeRef;

        if (!string.IsNullOrEmpty(storeRoot))
        {
            var normalised = storeRef.Replace('\\', '/');
            var marker = "/" + StorePathParser.NzbsDirName + "/";
            var idx = normalised.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var suffix = normalised[(idx + 1)..].Replace('/', Path.DirectorySeparatorChar);
                var candidate = Path.Combine(storeRoot, suffix);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
