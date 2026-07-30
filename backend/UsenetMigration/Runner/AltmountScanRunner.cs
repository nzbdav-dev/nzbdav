using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.UsenetMigration.Model;
using NzbWebDAV.UsenetMigration.Naming;
using NzbWebDAV.UsenetMigration.Nzb;
using NzbWebDAV.UsenetMigration.Provenance;
using NzbWebDAV.UsenetMigration.Source;
using NzbWebDAV.UsenetMigration.Triage;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models.UsenetMigration;
using NzbWebDAV.Utils;
using Serilog;

namespace NzbWebDAV.UsenetMigration.Runner;

/// <summary>Headline counts produced by a completed scan for the Review UI.</summary>
public sealed class ScanSummary
{
    public int MetaCount { get; init; }
    public int ReleaseCount { get; init; }
    public int GreenCount { get; init; }
    public int AmberCount { get; init; }
    public int RedCount { get; init; }
    public int AlreadyMigratedCount { get; init; }

    /// <summary>v1 metadata releases that cannot be migrated because they have no store reference.</summary>
    public int NoStoreRefCount { get; init; }

    /// <summary>Releases blocked because they share an NzbDAV queue key.</summary>
    public int QueueKeyCollisionCount { get; init; }

    public int ScanErrorCount { get; init; }
    public double JobNameDivergenceRate { get; init; }
    public long EstFetchBytesLazyTotal { get; init; }
}

/// <summary>
/// Walks the Altmount metadata tree, groups virtual files
/// into releases by <c>store_ref</c>, decodes each store, applies the category
/// map, computes NzbDAV's own naming, triages, estimates cost, and detects
/// collisions — then persists Releases + ReleaseFiles + pending Submissions to the
/// migration DB. This class coordinates the readers and classifiers without
/// duplicating their naming or verdict logic.
/// </summary>
public sealed class AltmountScanRunner(UsenetMigrationStore store, ConfigManager configManager)
{
    /// <summary>Test seam for the live NzbDAV context; production leaves it null.</summary>
    internal Func<DavDatabaseContext>? DavContextFactory { get; set; }
    internal Func<CancellationToken, Task>? BeforePersistOverride { get; set; }

    private sealed record WalkedMeta(string MetaPath, string VirtualPath, AltmountFileMetadata Meta);
    private sealed record PendingScanError(string? Path, string Kind, string Message);

    /// <summary>A release assembled in memory before verdict/collision finalisation.</summary>
    private sealed class PendingRelease
    {
        public required MigrationRelease Release { get; init; }
        public required List<MigrationReleaseFile> Files { get; init; }

        /// <summary>Triage reasons recorded before collision detection.</summary>
        public required List<string> BaseReasons { get; init; }

        /// <summary>True when this release enters collision detection (Included, not already Red).</summary>
        public bool IsCollisionCandidate { get; set; }
    }

    public async Task<ScanSummary?> RunAsync(CancellationToken ct = default)
    {
        var session = await store.GetSessionAsync(ct).ConfigureAwait(false);
        var metadataRoot = session.AltmountMetadataRoot
                           ?? throw new InvalidOperationException("Scan requires AltmountMetadataRoot to be set.");
        var storeRoot = session.AltmountStoreRoot;

        // Capture the two global settings the run must re-check before submitting.
        // The guarded write refuses to revive a scan already being cancelled.
        if (!await store.CaptureScanStartAsync(
                configManager.IsLazyRarParsingEnabled(),
                PathSanitizer.IsWindowsSafePathsEnabled,
                ct).ConfigureAwait(false))
            return null;

        var categoryMap = await LoadCategoryMapAsync(ct).ConfigureAwait(false);

        // Pass 1: walk metas, group by store_ref. v1 (empty store_ref) metas are
        // handled individually — each becomes its own Red no_store_ref release.
        var groups = new Dictionary<string, List<WalkedMeta>>(StringComparer.Ordinal);
        var v1Metas = new List<WalkedMeta>();
        var metaCount = 0;
        var scanErrors = new List<PendingScanError>();

        foreach (var metaPath in MetadataTreeWalker.EnumerateMetaFiles(metadataRoot))
        {
            ct.ThrowIfCancellationRequested();
            AltmountFileMetadata meta;
            try
            {
                meta = await AltmountMetaReader.ReadAsync(metaPath, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                scanErrors.Add(new PendingScanError(metaPath, "meta_read", e.Message));
                continue;
            }

            metaCount++;
            var walked = new WalkedMeta(metaPath, DeriveVirtualPath(metadataRoot, metaPath), meta);
            if (string.IsNullOrEmpty(meta.StoreRef))
                v1Metas.Add(walked);
            else
                Group(groups, meta.StoreRef).Add(walked);
        }

        // Pass 2: assemble a PendingRelease per store group and per v1 meta.
        var pending = new List<PendingRelease>(groups.Count + v1Metas.Count);
        foreach (var (storeRef, metas) in groups)
        {
            ct.ThrowIfCancellationRequested();
            pending.Add(await BuildStoreReleaseAsync(
                    storeRef, metas, storeRoot, categoryMap, scanErrors, ct)
                .ConfigureAwait(false));
        }

        foreach (var v1 in v1Metas)
            pending.Add(BuildV1Release(v1));

        // Pass 3: recognize complete live/provenance matches before collision
        // detection so they are not resubmitted or shown as conflicts.
        await ApplyAlreadyMigratedAsync(pending, ct).ConfigureAwait(false);

        // Pass 4: collision detection over the included, not-yet-Red candidate set.
        ApplyCollisions(pending);

        // Finalise verdicts and persist.
        if (BeforePersistOverride is not null)
            await BeforePersistOverride(ct).ConfigureAwait(false);

        var summary = await PersistAsync(pending, metaCount, scanErrors, ct).ConfigureAwait(false);
        if (summary is null)
            return null;

        Log.Information(
            "Altmount scan complete: {Releases} releases ({Green} green / {Amber} amber / {Red} red), " +
            "{AlreadyMigrated} already migrated, {NoStore} v1 no-store, " +
            "{Collisions} queue-key collisions, {Errors} scan errors",
            summary.ReleaseCount, summary.GreenCount, summary.AmberCount, summary.RedCount,
            summary.AlreadyMigratedCount, summary.NoStoreRefCount,
            summary.QueueKeyCollisionCount, summary.ScanErrorCount);

        return summary;
    }

    // --- release assembly --------------------------------------------------

    private async Task<PendingRelease> BuildStoreReleaseAsync(
        string storeRef,
        List<WalkedMeta> metas,
        string? storeRoot,
        IReadOnlyDictionary<string, MigrationCategoryMap> categoryMap,
        List<PendingScanError> scanErrors,
        CancellationToken ct)
    {
        var parsed = StorePathParser.Parse(storeRef);
        var submitFileName = parsed?.NzbBasename ?? DeriveBasename(storeRef);
        var storeBasename = parsed?.StoreBasename ?? submitFileName;
        var queueFileName = NzbDavNaming.QueueFileName(submitFileName);
        var jobName = NzbDavNaming.JobName(submitFileName);
        var jobNameDiverges = !string.Equals(jobName, submitFileName, StringComparison.Ordinal);

        var altCategory = parsed?.Category ?? "";
        categoryMap.TryGetValue(altCategory, out var mapping);
        var targetCategory = mapping is { Action: "migrate" } ? mapping.TargetCategory : null;
        var categoryMapped = !string.IsNullOrEmpty(targetCategory);

        // Decode the store to learn availability and cost.
        var (availability, cost) = await LoadStoreAsync(
                storeRef, storeRoot, scanErrors, ct)
            .ConfigureAwait(false);

        var input = new TriageInput
        {
            HasStoreRef = true,
            Store = availability,
            Metas = metas.Select(m => m.Meta).ToList(),
            CategoryMapped = categoryMapped,
            JobNameDiverges = jobNameDiverges,
            FilenamePasswordMarker = FilenameUtil.PasswordRegex.IsMatch(queueFileName),
        };
        var baseReasons = TriageClassifier.Classify(input).ToList();

        var worstStatus = TriageClassifier.WorstFileStatus(input.Metas);
        var release = new MigrationRelease
        {
            StoreRef = storeRef,
            StoreBasename = storeBasename,
            QueueId = parsed?.QueueId,
            SubmitFileName = submitFileName,
            QueueFileName = queueFileName,
            JobName = jobName,
            JobNameDiverges = jobNameDiverges,
            AltmountCategory = altCategory,
            TargetCategory = targetCategory,
            CollisionGroupKey = categoryMapped ? $"{targetCategory} {jobName}" : null,
            MetaFileCount = metas.Count,
            TotalBytes = cost?.TotalBytes,
            NzbFileCount = cost?.NzbFileCount ?? 0,
            SegmentCount = cost?.SegmentCount ?? 0,
            EstFetchBytesLazy = cost?.EstFetchBytesLazy ?? 0,
            EstFetchBytesEager = cost?.EstFetchBytesEager ?? 0,
            IsRarRelease = cost?.IsRarRelease ?? false,
            EstStatCommands = cost?.SegmentCount ?? 0,
            ReleaseDate = DeriveReleaseDate(input.Metas),
            Encryption = DeriveEncryption(input.Metas),
            HasPassword = input.Metas.Any(m => !string.IsNullOrEmpty(m.Password)),
            HasFilenamePassword = input.FilenamePasswordMarker,
            WorstFileStatus = worstStatus == AltmountFileStatus.Unspecified ? null : worstStatus.ToString(),
            HasNestedSources = input.Metas.Any(m => m.HasNestedSources),
            HasClipBoundaries = input.Metas.Any(m => m.HasClipBoundaries),
            SourceNzbdavId = null,
            Included = mapping is not { Action: "exclude" },
            ScannedAt = DateTime.UtcNow,
        };

        var files = metas.Select(m => BuildFile(storeRef, m)).ToList();
        return new PendingRelease { Release = release, Files = files, BaseReasons = baseReasons };
    }

    private static PendingRelease BuildV1Release(WalkedMeta v1)
    {
        var basename = DeriveBasename(v1.MetaPath);
        var storeRef = $"v1:{v1.MetaPath}";
        var queueFileName = NzbDavNaming.QueueFileName(basename);
        var jobName = NzbDavNaming.JobName(basename);
        var release = new MigrationRelease
        {
            StoreRef = storeRef,
            StoreBasename = basename,
            SubmitFileName = basename,
            QueueFileName = queueFileName,
            JobName = jobName,
            JobNameDiverges = !string.Equals(jobName, basename, StringComparison.Ordinal),
            MetaFileCount = 1,
            WorstFileStatus = v1.Meta.Status == AltmountFileStatus.Unspecified ? null : v1.Meta.Status.ToString(),
            Included = true,
            ScannedAt = DateTime.UtcNow,
        };
        return new PendingRelease
        {
            Release = release,
            Files = new List<MigrationReleaseFile> { BuildFile(storeRef, v1) },
            BaseReasons = new List<string> { VerdictReason.NoStoreRef },
        };
    }

    private static MigrationReleaseFile BuildFile(string storeRef, WalkedMeta m)
    {
        var fileName = Path.GetFileName(m.VirtualPath);
        var flags = BuildFlags(m.Meta);
        return new MigrationReleaseFile
        {
            StoreRef = storeRef,
            MetaPath = m.MetaPath,
            VirtualPath = m.VirtualPath,
            FileName = fileName,
            NormalisedName = MatchKey.ForLeaf(fileName),
            FileSize = m.Meta.FileSize,
            FileStatus = m.Meta.Status == AltmountFileStatus.Unspecified ? null : m.Meta.Status.ToString(),
            NzbdavId = null,
            Flags = flags,
        };
    }

    // --- collision + persistence ------------------------------------------

    private async Task ApplyAlreadyMigratedAsync(
        List<PendingRelease> pending,
        CancellationToken ct)
    {
        var candidates = pending
            .Where(p => p.Release.Included
                        && p.Release.TargetCategory != null
                        && VerdictReason.VerdictFor(p.BaseReasons) != Verdict.Red)
            .Select(p => new AlreadyMigratedCandidate
            {
                StoreRef = p.Release.StoreRef,
                TargetCategory = p.Release.TargetCategory!,
                JobName = p.Release.JobName,
                Files = p.Files,
            })
            .ToList();
        if (candidates.Count == 0)
            return;

        await using var migrationContext = store.NewContext();
        await using var davContext = DavContextFactory?.Invoke() ?? new DavDatabaseContext();
        var detected = await new AlreadyMigratedDetector()
            .DetectAndRecordAsync(candidates, migrationContext, davContext, ct)
            .ConfigureAwait(false);
        foreach (var item in pending.Where(p => detected.Contains(p.Release.StoreRef)))
            item.BaseReasons.Add(VerdictReason.AlreadyMigrated);
    }

    private void ApplyCollisions(List<PendingRelease> pending)
    {
        var candidates = new List<CollisionCandidate>();
        var byStoreRef = new Dictionary<string, PendingRelease>(StringComparer.Ordinal);
        foreach (var p in pending)
        {
            var baseVerdict = VerdictReason.VerdictFor(p.BaseReasons);
            p.IsCollisionCandidate = p.Release.Included
                                     && baseVerdict != Verdict.Red
                                     && p.Release.TargetCategory != null
                                     && !p.BaseReasons.Contains(VerdictReason.AlreadyMigrated);
            if (!p.IsCollisionCandidate) continue;

            byStoreRef[p.Release.StoreRef] = p;
            candidates.Add(new CollisionCandidate
            {
                StoreRef = p.Release.StoreRef,
                TargetCategory = p.Release.TargetCategory!,
                QueueFileName = p.Release.QueueFileName,
                JobName = p.Release.JobName,
                SubmitFileName = p.Release.SubmitFileName,
            });
        }

        // Passes 1, 2, 4 — local to the included set.
        var local = CollisionDetector.Detect(candidates);
        MergeFindings(byStoreRef, local.FindingsByStoreRef);

        // Pass 3 — against live NzbDAV content (queue + mounted folders).
        var (queueKeys, contentPaths) = LiveCollisions.LoadSets(DavContextFactory);
        var existing = CollisionDetector.DetectAgainstExisting(
            candidates,
            (cat, qfn) => queueKeys.Contains(LiveCollisions.Key(cat, qfn)),
            (cat, job) => contentPaths.Contains($"/content/{cat}/{job}"));
        MergeFindings(byStoreRef, existing);
    }

    private static void MergeFindings(
        IReadOnlyDictionary<string, PendingRelease> byStoreRef,
        IReadOnlyDictionary<string, List<CollisionFinding>> findings)
    {
        foreach (var (storeRef, list) in findings)
        {
            if (!byStoreRef.TryGetValue(storeRef, out var p)) continue;
            foreach (var f in list)
                if (!p.BaseReasons.Contains(f.Reason))
                    p.BaseReasons.Add(f.Reason);
        }
    }

    private async Task<ScanSummary?> PersistAsync(
        List<PendingRelease> pending,
        int metaCount,
        IReadOnlyList<PendingScanError> scanErrors,
        CancellationToken ct)
    {
        int green = 0, amber = 0, red = 0, alreadyMigrated = 0, noStoreRef = 0, queueKeyCollisions = 0;
        long estLazyTotal = 0;

        await using var ctx = store.NewContext();
        await using var transaction = await ctx.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Claim scan completion as the transaction's first write. If CancelScan
        // already published scan_cancelling, no scan artifact is touched. If this
        // write wins, cancellation waits for the complete result set to commit
        // and then observes scanned instead of reporting a false cancellation.
        var now = DateTime.UtcNow;
        var completed = await ctx.SessionState
            .Where(s => s.Id == UsenetMigrationStore.SessionId
                        && s.Status == MigrationSessionStatus.Scanning)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.Status, MigrationSessionStatus.Scanned)
                .SetProperty(s => s.ScanCompletedAt, now)
                .SetProperty(s => s.UpdatedAt, now), ct)
            .ConfigureAwait(false);
        if (completed != 1)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return null;
        }

        await UsenetMigrationStore.ClearScanArtifactsAsync(ctx, ct).ConfigureAwait(false);

        foreach (var p in pending)
        {
            var verdict = VerdictReason.VerdictFor(p.BaseReasons);
            p.Release.Verdict = VerdictName(verdict);
            p.Release.VerdictReasons = JsonSerializer.Serialize(p.BaseReasons);

            switch (verdict)
            {
                case Verdict.Green: green++; break;
                case Verdict.Amber: amber++; break;
                case Verdict.Red: red++; break;
            }

            if (p.BaseReasons.Contains(VerdictReason.NoStoreRef)) noStoreRef++;
            if (p.BaseReasons.Contains(VerdictReason.AlreadyMigrated)) alreadyMigrated++;
            if (p.BaseReasons.Contains(VerdictReason.QueueKeyCollision)) queueKeyCollisions++;
            if (!p.BaseReasons.Contains(VerdictReason.AlreadyMigrated))
                estLazyTotal += p.Release.EstFetchBytesLazy;

            ctx.Releases.Add(p.Release);
            ctx.ReleaseFiles.AddRange(p.Files);

            // A pending submission exists only for what we will actually submit:
            // included and not Red.
            if (p.Release.Included
                && verdict != Verdict.Red
                && !p.BaseReasons.Contains(VerdictReason.AlreadyMigrated))
            {
                ctx.Submissions.Add(new MigrationSubmission
                {
                    StoreRef = p.Release.StoreRef,
                    State = "pending",
                    UpdatedAt = DateTime.UtcNow,
                });
            }
        }

        ctx.ScanErrors.AddRange(scanErrors.Select(error => new MigrationScanError
        {
            Path = error.Path,
            Kind = error.Kind,
            Message = error.Message,
            OccurredAt = now,
        }));

        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);

        var divergent = pending.Count(p => p.Release.JobNameDiverges);
        return new ScanSummary
        {
            MetaCount = metaCount,
            ReleaseCount = pending.Count,
            GreenCount = green,
            AmberCount = amber,
            RedCount = red,
            AlreadyMigratedCount = alreadyMigrated,
            NoStoreRefCount = noStoreRef,
            QueueKeyCollisionCount = queueKeyCollisions,
            ScanErrorCount = scanErrors.Count,
            JobNameDivergenceRate = pending.Count == 0 ? 0 : (double)divergent / pending.Count,
            EstFetchBytesLazyTotal = estLazyTotal,
        };
    }

    // --- helpers -----------------------------------------------------------

    private async Task<Dictionary<string, MigrationCategoryMap>> LoadCategoryMapAsync(CancellationToken ct)
    {
        await using var ctx = store.NewContext();
        var rows = await ctx.CategoryMap.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        return rows.ToDictionary(r => r.AltmountCategory, r => r, StringComparer.Ordinal);
    }

    /// <summary>
    /// Resolves and decodes the store file. Returns the availability class and, on
    /// success, the cost estimate. A store_ref pointing outside the current host is
    /// remapped under <paramref name="storeRoot"/> by its <c>.nzbs/…</c> suffix.
    /// </summary>
    private async Task<(StoreAvailability, CostEstimate?)> LoadStoreAsync(
        string storeRef,
        string? storeRoot,
        List<PendingScanError> scanErrors,
        CancellationToken ct)
    {
        var path = StoreLocator.Resolve(storeRef, storeRoot);
        if (path is null)
            return (StoreAvailability.Missing, null);

        NzbStore decoded;
        try
        {
            decoded = await AltmountStoreReader.ReadStoreAsync(path, ct).ConfigureAwait(false);
        }
        catch (AltmountStoreException e)
        {
            scanErrors.Add(new PendingScanError(path, "store_decode", e.Message));
            return (StoreAvailability.Corrupt, null);
        }

        if (decoded.Files.Count == 0 || decoded.Files.All(f => f.Segments.Count == 0))
            return (StoreAvailability.Empty, null);

        return (StoreAvailability.Ok, CostEstimator.Estimate(decoded));
    }

    private static List<WalkedMeta> Group(Dictionary<string, List<WalkedMeta>> groups, string key)
    {
        if (!groups.TryGetValue(key, out var list))
        {
            list = new List<WalkedMeta>();
            groups[key] = list;
        }

        return list;
    }

    private static string DeriveVirtualPath(string metadataRoot, string metaPath)
    {
        var relative = Path.GetRelativePath(metadataRoot, metaPath).Replace('\\', '/');
        return relative.EndsWith(MetadataTreeWalker.MetaExtension, StringComparison.Ordinal)
            ? relative[..^MetadataTreeWalker.MetaExtension.Length]
            : relative;
    }

    private static string DeriveBasename(string path)
    {
        var name = Path.GetFileName(path.Replace('\\', '/').TrimEnd('/'));
        if (name.EndsWith(MetadataTreeWalker.MetaExtension, StringComparison.Ordinal))
            name = name[..^MetadataTreeWalker.MetaExtension.Length];
        else if (name.EndsWith(StorePathParser.StoreExtension, StringComparison.OrdinalIgnoreCase))
            name = name[..^StorePathParser.StoreExtension.Length];
        return name;
    }

    private static DateTime? DeriveReleaseDate(IReadOnlyList<AltmountFileMetadata> metas)
    {
        foreach (var m in metas)
            if (m.ReleaseDate > 0)
                return DateTimeOffset.FromUnixTimeSeconds(m.ReleaseDate).UtcDateTime;
        return null;
    }

    private static string? DeriveEncryption(IReadOnlyList<AltmountFileMetadata> metas)
    {
        foreach (var m in metas)
            if (m.Encryption != AltmountEncryption.None)
                return EncryptionHeadInjector.EncryptionToString(m.Encryption);
        return null;
    }

    private static string? BuildFlags(AltmountFileMetadata meta)
    {
        var flags = new List<string>();
        if (meta.HasNestedSources) flags.Add("nested");
        if (meta.HasClipBoundaries) flags.Add("clips");
        if (meta.Encryption != AltmountEncryption.None) flags.Add("encrypted");
        return flags.Count == 0 ? null : JsonSerializer.Serialize(flags);
    }


    private static string VerdictName(Verdict v) => v switch
    {
        Verdict.Green => "green",
        Verdict.Amber => "amber",
        _ => "red",
    };
}
