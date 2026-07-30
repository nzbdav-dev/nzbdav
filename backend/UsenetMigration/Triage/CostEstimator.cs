using NzbWebDAV.UsenetMigration.Model;
using NzbWebDAV.Models.Nzb;
using NzbWebDAV.Utils;

namespace NzbWebDAV.UsenetMigration.Triage;

/// <summary>
/// Per-release wire-cost estimate derived from the store without a network
/// round-trip. A first-segment probe drains the whole decoded article to the local
/// cache, so the lazy estimate sums each file's first-segment bytes. The eager
/// estimate also includes the last segment read while identifying archive volumes.
/// These estimates exclude retries, provider protocol overhead, and existing cache hits.
/// </summary>
public sealed class CostEstimate
{
    /// <summary>Σ over files: <c>Segments[0].Bytes</c>. Near-exact, assumes lazy RAR parsing on.</summary>
    public long EstFetchBytesLazy { get; init; }

    /// <summary>Lazy + Σ over archive files (&gt;1 segment): <c>Segments[^1].Bytes</c>. A lower bound.</summary>
    public long EstFetchBytesEager { get; init; }

    /// <summary>Σ over all files of all segment bytes — the full release wire size, for reporting.</summary>
    public long TotalBytes { get; init; }

    public int NzbFileCount { get; init; }

    /// <summary>Σ over all files of segment count == <c>est_stat_commands</c> when ensure-article-existence is on for the category.</summary>
    public int SegmentCount { get; init; }

    /// <summary>True when any file looks like a RAR/7z volume, which drives the eager term.</summary>
    public bool IsRarRelease { get; init; }
}

public static class CostEstimator
{
    public static CostEstimate Estimate(NzbStore store)
    {
        long lazy = 0, eager = 0, total = 0;
        var segmentCount = 0;
        var isRar = false;

        foreach (var f in store.Files)
        {
            foreach (var s in f.Segments)
                total += s.Bytes;

            segmentCount += f.Segments.Count;
            if (f.Segments.Count == 0)
                continue;

            var first = f.Segments[0].Bytes;
            lazy += first;
            eager += first;

            if (IsArchiveFile(f))
            {
                isRar = true;
                // Eager reads a header near the LAST segment — one FetchFirstSegments
                // never pulled. A single-segment file's last IS its first (already
                // cached), so no extra cost there.
                if (f.Segments.Count > 1)
                    eager += f.Segments[^1].Bytes;
            }
        }

        return new CostEstimate
        {
            EstFetchBytesLazy = lazy,
            EstFetchBytesEager = eager,
            TotalBytes = total,
            NzbFileCount = store.Files.Count,
            SegmentCount = segmentCount,
            IsRarRelease = isRar,
        };
    }

    /// <summary>
    /// Whether a file is a RAR/7z volume, using NzbDAV's subject parser and
    /// archive matchers. A subject that yields no
    /// parseable filename is treated as non-archive.
    /// </summary>
    private static bool IsArchiveFile(NzbFileEntry f)
    {
        var name = new NzbFile { Subject = f.Subject }.GetSubjectFileName();
        if (string.IsNullOrEmpty(name)) return false;
        return FilenameUtil.IsRarFile(name) || FilenameUtil.Is7zFile(name);
    }
}
