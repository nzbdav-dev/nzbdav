namespace NzbWebDAV.UsenetMigration.Source;

/// <summary>
/// Parses a <c>.nzbz</c> store path into its category, optional queue id, and
/// NZB basename. Operates on the <c>store_ref</c> read from the <c>.meta</c>,
/// which is authoritative; the path is never reconstructed from convention.
///
/// Store path convention (internal/importer/processor.go:522-537):
/// <code>{configDir}/.nzbs/{sanitizedCategory}/{queueID}-{base}.nzbz</code>
/// with two wrinkles this parser must honour:
/// <list type="bullet">
/// <item>The <c>{queueID}-</c> prefix is added ONLY when queueID &gt; 0
///   (processor.go:534), so it is frequently absent.</item>
/// <item>A category that produced a path outside configDir falls back to bare
///   <c>.nzbs/{base}.nzbz</c> — an uncategorised store.</item>
/// </list>
/// </summary>
public static class StorePathParser
{
    public const string NzbsDirName = ".nzbs";
    public const string FailedDirName = "failed";
    public const string StoreExtension = ".nzbz";

    public sealed class ParsedStorePath
    {
        /// <summary>Category path relative to <c>.nzbs/</c> ("" when uncategorised).</summary>
        public required string Category { get; init; }

        /// <summary>True when the store sits directly under <c>.nzbs/</c>.</summary>
        public required bool IsUncategorised { get; init; }

        /// <summary>True when the store lives under the <c>failed/</c> sibling.</summary>
        public required bool IsFailed { get; init; }

        /// <summary>Altmount queue id parsed from the filename prefix, when present.</summary>
        public required long? QueueId { get; init; }

        /// <summary>The store filename without the <c>.nzbz</c> extension (raw, with any queueID prefix).</summary>
        public required string StoreBasename { get; init; }

        /// <summary>
        /// The original NZB basename — the value to submit as <c>nzbname</c>.
        /// Equals <see cref="StoreBasename"/> with any <c>{queueID}-</c> prefix removed.
        /// </summary>
        public required string NzbBasename { get; init; }
    }

    /// <summary>
    /// Parses a store ref. Returns null when the path is not a <c>.nzbz</c> under
    /// a <c>.nzbs/</c> directory (caller should treat as a scan error).
    /// </summary>
    public static ParsedStorePath? Parse(string storeRef)
    {
        if (string.IsNullOrEmpty(storeRef)) return null;

        // Normalise separators so Windows-authored refs parse on Linux and vice versa.
        var normalised = storeRef.Replace('\\', '/');
        var segments = normalised.Split('/', StringSplitOptions.None);

        // Find the LAST ".nzbs" segment (configDir is assumed not to contain one).
        var nzbsIdx = -1;
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            if (segments[i] == NzbsDirName)
            {
                nzbsIdx = i;
                break;
            }
        }

        if (nzbsIdx < 0 || nzbsIdx >= segments.Length - 1)
            return null; // no .nzbs/ dir, or nothing after it

        var fileName = segments[^1];
        if (!fileName.EndsWith(StoreExtension, StringComparison.OrdinalIgnoreCase))
            return null;

        // Category = segments between .nzbs and the filename.
        var categorySegments = segments[(nzbsIdx + 1)..^1];
        var isFailed = categorySegments.Length > 0 &&
                       string.Equals(categorySegments[0], FailedDirName, StringComparison.Ordinal);
        var category = string.Join('/', categorySegments);
        var isUncategorised = categorySegments.Length == 0;

        var storeBasename = fileName[..^StoreExtension.Length];
        var (queueId, nzbBasename) = SplitQueueId(storeBasename);

        return new ParsedStorePath
        {
            Category = category,
            IsUncategorised = isUncategorised,
            IsFailed = isFailed,
            QueueId = queueId,
            StoreBasename = storeBasename,
            NzbBasename = nzbBasename,
        };
    }

    /// <summary>
    /// Splits a <c>{queueID}-{base}</c> store basename. The queueID is a positive
    /// integer (<c>%d</c>), so we split on the FIRST '-' and only treat the left
    /// side as a queueID when it is entirely ASCII digits.
    /// A basename that genuinely begins with "digits-" is an accepted ambiguity;
    /// the raw <c>StoreBasename</c> is preserved so any divergence stays visible.
    /// </summary>
    private static (long? queueId, string nzbBasename) SplitQueueId(string storeBasename)
    {
        var dash = storeBasename.IndexOf('-');
        if (dash <= 0) return (null, storeBasename);

        var left = storeBasename[..dash];
        foreach (var c in left)
        {
            if (c is < '0' or > '9')
                return (null, storeBasename);
        }

        if (!long.TryParse(left, out var queueId))
            return (null, storeBasename);

        return (queueId, storeBasename[(dash + 1)..]);
    }
}
