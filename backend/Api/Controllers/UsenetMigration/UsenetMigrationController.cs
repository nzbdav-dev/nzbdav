using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models.UsenetMigration;
using NzbWebDAV.UsenetMigration;
using NzbWebDAV.UsenetMigration.Model;
using NzbWebDAV.UsenetMigration.Runner;
using NzbWebDAV.UsenetMigration.Source;
using NzbWebDAV.UsenetMigration.Symlinks;
using NzbWebDAV.UsenetMigration.Triage;

namespace NzbWebDAV.Api.Controllers.UsenetMigration;

/// <summary>
/// The Usenet migration wizard's HTTP surface. Endpoints are thin:
/// they validate input, read/write the migration DB via
/// <see cref="UsenetMigrationStore"/>, and — crucially — advance the session's
/// <c>Status</c>. The heavy lifting (scan/submit/reconcile) belongs to
/// <see cref="UsenetMigrationRunner"/>, which reacts to Status changes. Progress
/// is surfaced by polling <c>status</c>/<c>summary</c> rather than websockets.
///
/// Symlink continuity (<c>symlinks/plan|apply</c>) is the optional Step 6: it
/// runs against a completed migration and, like scan/run, is status-driven —
/// <c>plan</c> flips Status to <c>linking</c> and <c>apply</c> (confirm-gated) to
/// <c>applying</c>; the runner performs the work and returns to <c>linked</c>.
/// </summary>
public sealed class UsenetMigrationController(
    UsenetMigrationStore store,
    UsenetMigrationRunner runner) : UsenetMigrationBaseController
{
    // --- connect -----------------------------------------------------------

    [HttpPost("api/altmount-migration/connect")]
    public Task<IActionResult> Connect([FromBody] ConnectRequest request) => GuardedAsync(async () =>
    {
        var metadataRoot = RequireDir(request.MetadataRoot, "metadataRoot");
        var storeRoot = OptionalDir(request.StoreRoot, "storeRoot");
        var configPath = OptionalFile(request.ConfigPath, "configPath");
        var current = await store.GetSessionAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        if (!CanConnect(current.Status))
            throw new BadHttpRequestException(
                $"Cannot connect while migration operation '{current.Status}' is active.");

        var categories = new List<AltmountCategory>();
        if (configPath is not null)
        {
            var config = await AltmountConfigReader.ReadAsync(configPath, HttpContext.RequestAborted)
                .ConfigureAwait(false);
            categories = config.Categories.ToList();
        }

        var transition = await store.ApplyConnectionAsync(
                new MigrationConnectionValues(
                    metadataRoot,
                    configPath,
                    storeRoot,
                    request.MaxQueueDepth,
                    request.SubmitWorkers),
                categories,
                HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!transition.Succeeded)
            throw new BadHttpRequestException(
                $"Cannot connect while migration operation '{transition.CurrentStatus}' is active.");

        return Ok(new { status = true, categoryCount = categories.Count, categories });
    });

    // --- categories --------------------------------------------------------

    [HttpGet("api/altmount-migration/categories")]
    public Task<IActionResult> GetCategories() => GuardedAsync(async () =>
    {
        var map = await store.GetCategoryMapAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var session = await store.GetSessionAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        return Ok(new { status = true, sessionStatus = session.Status, categories = map });
    });

    [HttpPut("api/altmount-migration/categories")]
    public Task<IActionResult> PutCategories([FromBody] CategoryMapRequest request) => GuardedAsync(async () =>
    {
        var session = await store.GetSessionAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        if (!CanEditCategoryMappings(session.Status))
            throw new BadHttpRequestException(
                "Category mappings can only be changed before migration work starts.");

        if (request.Mappings is null || request.Mappings.Count == 0)
            throw new BadHttpRequestException("At least one mapping is required.");

        var mappings = request.Mappings
            .Select(m => new MigrationCategoryMappingChange(
                m.AltmountCategory,
                m.TargetCategory,
                m.Action is "exclude" ? "exclude" : "migrate"))
            .ToList();
        var transition = await store.ApplyCategoryMappingsAsync(
                mappings, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!transition.Succeeded)
            throw new BadHttpRequestException(
                $"Category mappings cannot change while migration operation '{transition.CurrentStatus}' is active.");

        // Category changes alter per-release base reasons (category_unmapped, target
        // category), so a scanned result is no longer valid — drop back to "mapped"
        // and require a re-scan to apply. (An include/exclude edit, by contrast, is
        // recomputed in place — see releases/include.)
        var map = await store.GetCategoryMapAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        return Ok(new { status = true, categories = map });
    });

    // --- scan --------------------------------------------------------------

    [HttpPost("api/altmount-migration/scan")]
    public Task<IActionResult> StartScan() => GuardedAsync(async () =>
    {
        var session = await store.GetSessionAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        if (string.IsNullOrEmpty(session.AltmountMetadataRoot))
            throw new BadHttpRequestException("Connect to an Altmount library before scanning.");
        if (session.Status is "scanning")
            return Ok(new { status = true, state = "scanning" });
        if (!CanStartScan(session.Status))
            throw new BadHttpRequestException(
                $"Cannot scan while migration operation '{session.Status}' is active.");

        var transition = await store.TryTransitionSessionAsync(
                MigrationSessionTransition.StartScan, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!transition.Succeeded)
            throw new BadHttpRequestException(
                $"Cannot scan while migration operation '{transition.CurrentStatus}' is active.");

        return Ok(new { status = true, state = "scanning" });
    });

    [HttpDelete("api/altmount-migration/scan")]
    public Task<IActionResult> CancelScan() => GuardedAsync(async () =>
    {
        var transition = await store.TryTransitionSessionAsync(
                MigrationSessionTransition.CancelScan, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!transition.Succeeded)
            throw new BadHttpRequestException(
                $"Only an active scan can be cancelled; current state is '{transition.CurrentStatus}'.");

        runner.InterruptScan();
        return Ok(new { status = true, state = "scan_cancelling" });
    });

    // --- releases ----------------------------------------------------------

    [HttpGet("api/altmount-migration/releases")]
    public Task<IActionResult> GetReleases(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? verdict = null,
        [FromQuery] bool? included = null,
        [FromQuery] string? targetCategory = null,
        [FromQuery] string? q = null,
        [FromQuery] string? sort = null) => GuardedAsync(async () =>
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        await using var ctx = store.NewContext();
        var query = ctx.Releases.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(verdict))
            query = query.Where(r => r.Verdict == verdict &&
                                     !r.VerdictReasons.Contains(VerdictReason.AlreadyMigrated));
        if (included is not null)
            query = query.Where(r => r.Included == included.Value);
        if (!string.IsNullOrEmpty(targetCategory))
            query = query.Where(r => r.TargetCategory == targetCategory);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var normalizedSearch = q.Trim().ToLowerInvariant();
            query = query.Where(r => r.SubmitFileName.ToLower().Contains(normalizedSearch));
        }

        var total = await query.CountAsync(HttpContext.RequestAborted).ConfigureAwait(false);

        query = ApplyReleaseSort(query, sort);

        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(HttpContext.RequestAborted)
            .ConfigureAwait(false);

        return Ok(new
        {
            status = true,
            total,
            page,
            pageSize,
            releases = rows.Select(ReleaseDto.From).ToList(),
        });
    });

    [HttpPut("api/altmount-migration/releases/include")]
    public Task<IActionResult> SetInclude([FromBody] IncludeRequest request) => GuardedAsync(async () =>
    {
        var session = await store.GetSessionAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        if (!CanEditReleaseSelection(session.Status))
            throw new BadHttpRequestException(
                "Release inclusion can only be changed while reviewing a completed scan.");

        if (request.StoreRefs is null || request.StoreRefs.Count == 0)
            throw new BadHttpRequestException("storeRefs is required.");

        int updated;
        await using (var ctx = store.NewContext())
        {
            updated = await ctx.Releases
                .Where(r => request.StoreRefs.Contains(r.StoreRef))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Included, request.Included),
                    HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }

        // Include/exclude changes the collision set, so recompute it in place.
        await new CollisionRecomputer(store).RecomputeAsync(HttpContext.RequestAborted).ConfigureAwait(false);

        return Ok(new { status = true, updated });
    });

    // --- collisions --------------------------------------------------------

    [HttpGet("api/altmount-migration/collisions")]
    public Task<IActionResult> GetCollisions() => GuardedAsync(async () =>
    {
        await using var ctx = store.NewContext();
        var flagged = await ctx.Releases.AsNoTracking()
            .Where(r => r.Included && r.CollisionGroupKey != null &&
                        (r.VerdictReasons.Contains(VerdictReason.QueueKeyCollision) ||
                         r.VerdictReasons.Contains(VerdictReason.MountFolderCollision) ||
                         r.VerdictReasons.Contains(VerdictReason.CollidesWithExistingQueueItem) ||
                         r.VerdictReasons.Contains(VerdictReason.MountFolderExists)))
            .Select(r => new { r.StoreRef, r.CollisionGroupKey, r.SubmitFileName, r.Verdict, r.VerdictReasons })
            .ToListAsync(HttpContext.RequestAborted)
            .ConfigureAwait(false);

        var groups = flagged
            .GroupBy(r => r.CollisionGroupKey!)
            .Select(g => new
            {
                key = g.Key,
                blocking = g.Any(r => Reasons(r.VerdictReasons).Contains(VerdictReason.QueueKeyCollision) ||
                                      Reasons(r.VerdictReasons).Contains(VerdictReason.CollidesWithExistingQueueItem)),
                members = g.Select(r => new
                {
                    r.StoreRef,
                    r.SubmitFileName,
                    r.Verdict,
                    reasons = Reasons(r.VerdictReasons).Where(LiveCollisions.CollisionReasons.Contains).ToArray(),
                }).ToList(),
            })
            .OrderByDescending(g => g.blocking)
            .ToList();

        return Ok(new { status = true, groups });
    });

    // --- summary -----------------------------------------------------------

    [HttpGet("api/altmount-migration/summary")]
    public Task<IActionResult> GetSummary() => GuardedAsync(async () =>
    {
        var session = await store.GetSessionAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        await using var ctx = store.NewContext();

        var included = ctx.Releases.AsNoTracking().Where(r => r.Included);
        var green = await included.CountAsync(
            r => r.Verdict == "green" && !r.VerdictReasons.Contains(VerdictReason.AlreadyMigrated),
            HttpContext.RequestAborted).ConfigureAwait(false);
        var amber = await included.CountAsync(
            r => r.Verdict == "amber" && !r.VerdictReasons.Contains(VerdictReason.AlreadyMigrated),
            HttpContext.RequestAborted).ConfigureAwait(false);
        var red = await included.CountAsync(
            r => r.Verdict == "red" && !r.VerdictReasons.Contains(VerdictReason.AlreadyMigrated),
            HttpContext.RequestAborted).ConfigureAwait(false);
        var total = await ctx.Releases.AsNoTracking().CountAsync(HttpContext.RequestAborted).ConfigureAwait(false);

        var estLazy = await included.Where(r => r.Verdict != "red" &&
                                                !r.VerdictReasons.Contains(VerdictReason.AlreadyMigrated))
            .SumAsync(r => r.EstFetchBytesLazy, HttpContext.RequestAborted).ConfigureAwait(false);
        var estEager = await included.Where(r => r.Verdict != "red" &&
                                                 !r.VerdictReasons.Contains(VerdictReason.AlreadyMigrated))
            .SumAsync(r => r.EstFetchBytesEager, HttpContext.RequestAborted).ConfigureAwait(false);

        var blockingCollisions = await included.CountAsync(
            r => r.VerdictReasons.Contains(VerdictReason.QueueKeyCollision) ||
                 r.VerdictReasons.Contains(VerdictReason.CollidesWithExistingQueueItem),
            HttpContext.RequestAborted).ConfigureAwait(false);
        var unmapped = await included.CountAsync(
            r => r.VerdictReasons.Contains(VerdictReason.CategoryUnmapped), HttpContext.RequestAborted)
            .ConfigureAwait(false);
        var noStoreRef = await ctx.Releases.AsNoTracking().CountAsync(
            r => r.VerdictReasons.Contains(VerdictReason.NoStoreRef), HttpContext.RequestAborted)
            .ConfigureAwait(false);
        var alreadyMigrated = await included.CountAsync(
            r => r.VerdictReasons.Contains(VerdictReason.AlreadyMigrated), HttpContext.RequestAborted)
            .ConfigureAwait(false);

        var submittable = await included.CountAsync(
                r => r.Verdict != "red"
                     && !r.VerdictReasons.Contains(VerdictReason.AlreadyMigrated),
                HttpContext.RequestAborted)
            .ConfigureAwait(false);
        var scanErrors = await ctx.ScanErrors.AsNoTracking().CountAsync(HttpContext.RequestAborted).ConfigureAwait(false);

        var canRun = CanStartMigration(session.Status) &&
                     blockingCollisions == 0 && unmapped == 0 &&
                     (submittable > 0 || alreadyMigrated > 0);

        return Ok(new
        {
            status = true,
            sessionStatus = session.Status,
            counts = new { total, green, amber, red, submittable, noStoreRef, alreadyMigrated },
            cost = new { estFetchBytesLazy = estLazy, estFetchBytesEager = estEager },
            warnings = new { blockingCollisions, unmapped, scanErrors },
            canRun,
        });
    });

    // --- run ---------------------------------------------------------------

    [HttpPost("api/altmount-migration/run")]
    public Task<IActionResult> StartRun() => GuardedAsync(async () =>
    {
        var session = await store.GetSessionAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        if (session.Status is "running")
            return Ok(new { status = true, state = "running" });
        if (!CanStartMigration(session.Status))
            throw new BadHttpRequestException(
                "Complete a new scan before starting another migration.");

        var submittable = 0;
        await using (var ctx = store.NewContext())
        {
            var blocking = await ctx.Releases.AsNoTracking().CountAsync(
                r => r.Included &&
                     (r.VerdictReasons.Contains(VerdictReason.QueueKeyCollision) ||
                      r.VerdictReasons.Contains(VerdictReason.CollidesWithExistingQueueItem) ||
                      r.VerdictReasons.Contains(VerdictReason.CategoryUnmapped)),
                HttpContext.RequestAborted).ConfigureAwait(false);
            if (blocking > 0)
                return Conflict(new BaseApiResponse
                {
                    Status = false,
                    Error = $"{blocking} included release(s) have blocking collisions or unmapped categories. " +
                            "Resolve them in Review before running.",
                });

            submittable = await ctx.Releases.AsNoTracking().CountAsync(
                r => r.Included && r.Verdict != "red" &&
                     !r.VerdictReasons.Contains(VerdictReason.AlreadyMigrated),
                HttpContext.RequestAborted).ConfigureAwait(false);
        }

        // A scan made entirely of releases that are already present has no work
        // to queue. Mark the wizard complete directly so Step 6 remains usable
        // without manufacturing an empty migration run.
        if (submittable == 0)
        {
            var completed = await store.CompleteEmptyRunAsync(HttpContext.RequestAborted)
                .ConfigureAwait(false);
            if (!completed.Succeeded)
                throw new BadHttpRequestException(
                    $"The wizard moved to '{completed.CurrentStatus}' before the migration could start.");
            return Ok(new { status = true, state = "complete" });
        }

        var transition = await store.TryTransitionSessionAsync(
                MigrationSessionTransition.StartRun, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!transition.Succeeded)
            throw new BadHttpRequestException(
                $"The wizard moved to '{transition.CurrentStatus}' before the migration could start.");

        if (transition.Outcome == MigrationSessionTransitionOutcome.AlreadyApplied)
            return Ok(new { status = true, state = "running" });

        await store.BeginRunAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        return Ok(new { status = true, state = "running" });
    });

    [HttpPost("api/altmount-migration/run/resume")]
    public Task<IActionResult> ResumeRun() => GuardedAsync(async () =>
    {
        var transition = await store.TryTransitionSessionAsync(
                MigrationSessionTransition.ResumeRun, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!transition.Succeeded)
            throw new BadHttpRequestException("Only a paused migration can be resumed.");
        return Ok(new { status = true, state = "running" });
    });

    [HttpDelete("api/altmount-migration/run")]
    public Task<IActionResult> StopRun([FromQuery] bool cancel = false) => GuardedAsync(async () =>
    {
        var session = await store.GetSessionAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        if (cancel)
        {
            if (session.Status is "cancelled")
                return Ok(new { status = true, state = "cancelled" });
            if (session.Status is "cancelling")
            {
                runner.InterruptSubmissionBatch();
                return Ok(new { status = true, state = "cancelling" });
            }
            if (!CanCancelMigration(session.Status))
                throw new BadHttpRequestException("Only a running or paused migration can be cancelled.");

            var state = await store.BeginCancellationAsync(HttpContext.RequestAborted).ConfigureAwait(false);
            if (state is not "cancelling")
                throw new BadHttpRequestException("The migration finished before cancellation could begin.");
            runner.InterruptSubmissionBatch();
            return Ok(new { status = true, state = "cancelling" });
        }

        var transition = await store.TryTransitionSessionAsync(
                MigrationSessionTransition.PauseRun, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!transition.Succeeded)
            throw new BadHttpRequestException("Only a running migration can be paused.");

        runner.InterruptSubmissionBatch();
        return Ok(new { status = true, state = "paused" });
    });

    // --- status ------------------------------------------------------------

    [HttpGet("api/altmount-migration/status")]
    public Task<IActionResult> GetStatus() => GuardedAsync(async () =>
    {
        var session = await store.GetSessionAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var preferences = await store.GetPreferencesAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        await using var ctx = store.NewContext();

        var byState = await ctx.Submissions.AsNoTracking()
            .GroupBy(s => s.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(HttpContext.RequestAborted)
            .ConfigureAwait(false);
        var submissions = byState.ToDictionary(x => x.State, x => x.Count);
        var submissionIssues = await LoadSubmissionIssuesAsync(ctx, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        var cleanupRows = await ctx.Submissions.AsNoTracking()
            .Where(s => s.State == "completed" || s.State == "history_cleared")
            .Select(s => new { s.NzoId, s.HistoryClearedAt })
            .ToListAsync(HttpContext.RequestAborted)
            .ConfigureAwait(false);
        var validCleanupRows = cleanupRows.Where(s => Guid.TryParse(s.NzoId, out _)).ToList();
        var historyCleanupEligible = validCleanupRows.Count;
        var historyCleanupCleared = validCleanupRows.Count(s => s.HistoryClearedAt != null);

        return Ok(new
        {
            status = true,
            sessionStatus = session.Status,
            roots = new
            {
                AltmountMetadataRoot = session.AltmountMetadataRoot ?? preferences?.AltmountMetadataRoot,
                AltmountConfigPath = session.AltmountConfigPath ?? preferences?.AltmountConfigPath,
                AltmountStoreRoot = session.AltmountStoreRoot ?? preferences?.AltmountStoreRoot,
            },
            symlinks = new
            {
                SymlinkLibraryRoot = session.SymlinkLibraryRoot ?? preferences?.SymlinkLibraryRoot,
                SymlinkBackupDir = session.SymlinkBackupDir ?? preferences?.SymlinkBackupDir,
            },
            MaxQueueDepth = preferences is null || session.AltmountMetadataRoot is not null
                ? session.MaxQueueDepth
                : preferences.MaxQueueDepth,
            SubmitWorkers = preferences is null || session.AltmountMetadataRoot is not null
                ? session.SubmitWorkers
                : preferences.SubmitWorkers,
            timestamps = new
            {
                session.ScanStartedAt,
                session.ScanCompletedAt,
                session.RunStartedAt,
                session.RunCompletedAt,
            },
            submissions,
            submissionIssues,
            historyCleanup = new
            {
                eligible = historyCleanupEligible,
                cleared = historyCleanupCleared,
                pending = historyCleanupEligible - historyCleanupCleared,
            },
        });
    });

    [HttpPost("api/altmount-migration/history/cleanup")]
    public Task<IActionResult> CleanupHistory([FromBody] HistoryCleanupRequest request) => GuardedAsync(async () =>
    {
        if (request.Confirm != true)
            throw new BadHttpRequestException("Clearing migration history requires explicit confirmation.");

        var session = await store.GetSessionAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        if (session.Status is not ("complete" or "linked"))
            throw new BadHttpRequestException("Finish the migration before clearing its SAB history.");

        var summary = await new MigrationHistoryCleaner(store)
            .CleanAsync(HttpContext.RequestAborted)
            .ConfigureAwait(false);
        return Ok(new { status = true, cleanup = summary });
    });

    // --- symlinks (Step 6 — optional) --------------------------------------

    [HttpPost("api/altmount-migration/symlinks/plan")]
    public Task<IActionResult> PlanSymlinks([FromBody] SymlinkPlanRequest request) => GuardedAsync(async () =>
    {
        var session = await store.GetSessionAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        // Step 6 runs against a finished migration. Allow a re-plan from the
        // resting "linked" state; forbid it mid-scan/mid-run.
        if (session.Status is not ("complete" or "linked"))
            throw new BadHttpRequestException("Finish the migration before rewriting symlinks.");

        var libraryRoot = RequireDir(request.LibraryRoot, "libraryRoot");
        var backupDir = EnsureWritableDir(request.BackupDir, "backupDir");

        var transition = await store.StartSymlinkPlanAsync(
                libraryRoot, backupDir, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (transition.Outcome != MigrationSessionTransitionOutcome.Applied)
            throw new BadHttpRequestException(
                $"Cannot build a symlink plan while migration operation '{transition.CurrentStatus}' is active.");

        return Ok(new { status = true, state = "linking" });
    });

    [HttpGet("api/altmount-migration/symlinks")]
    public Task<IActionResult> GetSymlinks(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? status = null,
        [FromQuery] string? q = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? format = null) => GuardedAsync(async () =>
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 1000);

        await using var ctx = store.NewContext();

        // Status breakdown over the whole plan, independent of the current filter —
        // this backs the Review panel's summary counts.
        var counts = (await ctx.SymlinkRewrites.AsNoTracking()
                .GroupBy(r => r.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(HttpContext.RequestAborted).ConfigureAwait(false))
            .ToDictionary(x => x.Status, x => x.Count);

        var query = ctx.SymlinkRewrites.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(status))
            query = query.Where(r => r.Status == status);
        if (!string.IsNullOrEmpty(q))
            query = query.Where(r => r.SymlinkPath.Contains(q) || r.OldTarget.Contains(q));

        query = sort switch
        {
            "path" => query.OrderBy(r => r.SymlinkPath),
            "-path" => query.OrderByDescending(r => r.SymlinkPath),
            _ => query.OrderBy(r => r.Status).ThenBy(r => r.SymlinkPath),
        };

        // CSV export returns the full filtered set (unpaged) so nothing is truncated.
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var all = await query.ToListAsync(HttpContext.RequestAborted).ConfigureAwait(false);
            return File(BuildCsv(all), "text/csv", "altmount-symlink-plan.csv");
        }

        var total = await query.CountAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(HttpContext.RequestAborted)
            .ConfigureAwait(false);

        return Ok(new
        {
            status = true,
            total,
            page,
            pageSize,
            counts,
            rows = rows.Select(SymlinkRewriteDto.From).ToList(),
        });
    });

    [HttpPost("api/altmount-migration/symlinks/apply")]
    public Task<IActionResult> ApplySymlinks([FromBody] SymlinkApplyRequest request) => GuardedAsync(async () =>
    {
        // Apply is the only Step 6 action that mutates the library, so require
        // explicit confirmation and a reviewed plan.
        if (request.Confirm != true)
            throw new BadHttpRequestException("Applying symlink rewrites requires explicit confirmation.");

        var session = await store.GetSessionAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        if (session.Status is not "linked")
            throw new BadHttpRequestException("Build and review a symlink plan before applying.");
        if (string.IsNullOrEmpty(session.SymlinkBackupDir))
            throw new BadHttpRequestException("No backup directory is configured; re-run the plan step.");

        await using (var ctx = store.NewContext())
        {
            var unreadable = await ctx.SymlinkRewrites.AsNoTracking()
                .CountAsync(r => r.Status == "unreadable", HttpContext.RequestAborted)
                .ConfigureAwait(false);
            if (unreadable > 0 && request.AcknowledgeUnreadable != true)
                throw new BadHttpRequestException(
                    $"The current plan contains {unreadable} unreadable symlink(s). " +
                    "Review the Unreadable rows, then fix access and rebuild the plan or explicitly acknowledge them.");

            var rewrites = await ctx.SymlinkRewrites.AsNoTracking()
                .CountAsync(r => r.Status == "rewrite", HttpContext.RequestAborted)
                .ConfigureAwait(false);
            if (rewrites == 0)
                throw new BadHttpRequestException("The current plan has no rewrites to apply.");
        }

        var transition = await store.TryTransitionSessionAsync(
                MigrationSessionTransition.StartApply, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (transition.Outcome != MigrationSessionTransitionOutcome.Applied)
            throw new BadHttpRequestException(
                $"Cannot apply symlinks while migration operation '{transition.CurrentStatus}' is active.");

        return Ok(new { status = true, state = "applying" });
    });

    [HttpGet("api/altmount-migration/symlinks/backups")]
    public Task<IActionResult> GetSymlinkBackups() => GuardedAsync(async () =>
    {
        var backups = await new SymlinkRestoreService(store)
            .ListAsync(HttpContext.RequestAborted)
            .ConfigureAwait(false);
        return Ok(new { status = true, backups });
    });

    [HttpPost("api/altmount-migration/symlinks/restore")]
    public Task<IActionResult> RestoreSymlinks([FromBody] SymlinkRestoreRequest request) => GuardedAsync(async () =>
    {
        if (request.Confirm != true)
            throw new BadHttpRequestException("Restoring symlinks requires explicit confirmation.");

        SymlinkRestoreSummary summary;
        try
        {
            summary = await runner
                .RestoreSymlinksAsync(request.FileName ?? "", HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is InvalidDataException or FileNotFoundException or InvalidOperationException)
        {
            throw new BadHttpRequestException(e.Message, e);
        }
        return Ok(new { status = true, restore = summary });
    });

    // --- reset -------------------------------------------------------------

    [HttpPost("api/altmount-migration/reset")]
    public Task<IActionResult> Reset() => GuardedAsync(async () =>
    {
        await ResetWizardAsync(store, HttpContext.RequestAborted).ConfigureAwait(false);
        return Ok(new { status = true });
    });

    [HttpGet("api/altmount-migration/migration-data")]
    public Task<IActionResult> GetMigrationData() => GuardedAsync(async () =>
    {
        var summary = await store.GetMigrationDataSummaryAsync(HttpContext.RequestAborted)
            .ConfigureAwait(false);
        return Ok(new { status = true, summary });
    });

    [HttpPost("api/altmount-migration/migration-data/forget")]
    public Task<IActionResult> ForgetMigrationData([FromBody] ForgetMigrationDataRequest request) => GuardedAsync(async () =>
    {
        if (request.Confirm != true)
            throw new BadHttpRequestException("Forgetting all migration records requires explicit confirmation.");

        if (!await store.ForgetAllMigrationRecordsAsync(HttpContext.RequestAborted)
                .ConfigureAwait(false))
            throw new BadHttpRequestException("Wait for the active migration task to finish before forgetting its records.");
        return Ok(new { status = true });
    });

    // --- helpers -----------------------------------------------------------

    internal static bool CanConnect(string sessionStatus) =>
        MigrationSessionStateMachine.CanTransition(MigrationSessionTransition.Connect, sessionStatus);

    internal static bool CanStartMigration(string sessionStatus) =>
        MigrationSessionStateMachine.CanTransition(MigrationSessionTransition.StartRun, sessionStatus);

    internal static bool CanEditCategoryMappings(string sessionStatus) =>
        MigrationSessionStateMachine.CanTransition(
            MigrationSessionTransition.MapCategories, sessionStatus);

    internal static bool CanEditReleaseSelection(string sessionStatus) =>
        sessionStatus == MigrationSessionStatus.Scanned;

    internal static bool IsMigrationWorkActive(string sessionStatus) =>
        MigrationSessionStateMachine.IsWorkActive(sessionStatus);

    internal static async Task ResetWizardAsync(
        UsenetMigrationStore migrationStore, CancellationToken ct = default)
    {
        if (!await migrationStore.ResetAsync(ct).ConfigureAwait(false))
            throw new BadHttpRequestException(
                "Wait for the active migration task to finish, or cancel the migration, before resetting the wizard.");
    }

    internal static async Task<List<SubmissionIssueDto>> LoadSubmissionIssuesAsync(
        UsenetMigrationDbContext context, CancellationToken ct = default)
    {
        var rows = await context.Submissions.AsNoTracking()
            .Where(s => s.State == "failed" || s.State == "evicted")
            .Join(
                context.Releases.AsNoTracking(),
                submission => submission.StoreRef,
                release => release.StoreRef,
                (submission, release) => new
                {
                    submission.StoreRef,
                    release.SubmitFileName,
                    submission.State,
                    submission.Error,
                })
            .OrderBy(row => row.State == "failed" ? 0 : 1)
            .ThenBy(row => row.SubmitFileName)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows
            .Select(row => SubmissionIssueDto.From(
                row.StoreRef, row.SubmitFileName, row.State, row.Error))
            .ToList();
    }

    internal static IOrderedQueryable<MigrationRelease> ApplyReleaseSort(
        IQueryable<MigrationRelease> query, string? sort)
    {
        return sort switch
        {
            "bytes" => query.OrderByDescending(r => r.EstFetchBytesLazy),
            "-bytes" => query.OrderBy(r => r.EstFetchBytesLazy),
            "name" => query.OrderBy(r => r.SubmitFileName),
            "-name" => query.OrderByDescending(r => r.SubmitFileName),
            _ => query
                .OrderBy(r => !r.Included || r.Verdict == "red" ||
                              r.VerdictReasons.Contains(VerdictReason.AlreadyMigrated))
                .ThenBy(r => r.Verdict)
                .ThenBy(r => r.SubmitFileName),
        };
    }

    internal static bool CanStartScan(string sessionStatus) =>
        MigrationSessionStateMachine.CanTransition(
            MigrationSessionTransition.StartScan, sessionStatus);

    internal static bool CanResumeMigration(string sessionStatus) =>
        MigrationSessionStateMachine.CanTransition(
            MigrationSessionTransition.ResumeRun, sessionStatus);

    internal static bool CanPauseMigration(string sessionStatus) =>
        MigrationSessionStateMachine.CanTransition(
            MigrationSessionTransition.PauseRun, sessionStatus);

    internal static bool CanCancelMigration(string sessionStatus) =>
        MigrationSessionStateMachine.CanTransition(
            MigrationSessionTransition.BeginCancellation, sessionStatus);

    private static string RequireDir(string? path, string field)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new BadHttpRequestException($"{field} is required.");
        if (!Directory.Exists(path))
            throw new BadHttpRequestException($"{field} directory does not exist: {path}");
        return path;
    }

    private static string? OptionalDir(string? path, string field)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!Directory.Exists(path))
            throw new BadHttpRequestException($"{field} directory does not exist: {path}");
        return path;
    }

    private static string? OptionalFile(string? path, string field)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!System.IO.File.Exists(path))
            throw new BadHttpRequestException($"{field} file does not exist: {path}");
        return path;
    }

    /// <summary>Validates a directory the wizard must write to, creating it if absent.</summary>
    private static string EnsureWritableDir(string? path, string field)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new BadHttpRequestException($"{field} is required.");
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception e)
        {
            throw new BadHttpRequestException($"{field} is not writable: {e.Message}");
        }
        return path;
    }

    private static byte[] BuildCsv(IEnumerable<MigrationSymlinkRewrite> rows)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("SymlinkPath,OldTarget,NewTarget,Status,MatchMethod,StoreRef,Error");
        foreach (var r in rows)
        {
            sb.Append(Csv(r.SymlinkPath)).Append(',')
                .Append(Csv(r.OldTarget)).Append(',')
                .Append(Csv(r.NewTarget)).Append(',')
                .Append(Csv(r.Status)).Append(',')
                .Append(Csv(r.MatchMethod)).Append(',')
                .Append(Csv(r.StoreRef)).Append(',')
                .Append(Csv(r.Error)).Append('\n');
        }
        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // Quote when the field contains a delimiter, quote, or newline; double interior quotes.
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string[] Reasons(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}

// --- request / response DTOs ----------------------------------------------

public sealed record ConnectRequest(
    string? MetadataRoot,
    string? ConfigPath,
    string? StoreRoot,
    int? MaxQueueDepth,
    int? SubmitWorkers);

public sealed record CategoryMapRequest(List<CategoryMapEntry>? Mappings);

public sealed record CategoryMapEntry(string AltmountCategory, string? TargetCategory, string? Action);

public sealed record IncludeRequest(List<string>? StoreRefs, bool Included);

public sealed record HistoryCleanupRequest(bool? Confirm);

public sealed record ForgetMigrationDataRequest(bool? Confirm);

public sealed record SymlinkPlanRequest(string? LibraryRoot, string? BackupDir);

public sealed record SymlinkApplyRequest(bool? Confirm, bool? AcknowledgeUnreadable);

public sealed record SymlinkRestoreRequest(string? FileName, bool? Confirm);

public sealed record SubmissionIssueDto(
    string StoreRef,
    string SubmitFileName,
    string State,
    string Reason)
{
    public static SubmissionIssueDto From(
        string storeRef, string submitFileName, string state, string? error)
    {
        var reason = string.IsNullOrWhiteSpace(error)
            ? state == "evicted"
                ? "The submission disappeared from both the NzbDAV queue and history before it completed."
                : "NzbDAV reported that this release failed without providing a reason."
            : error;
        return new SubmissionIssueDto(storeRef, submitFileName, state, reason);
    }
}

public sealed record SymlinkRewriteDto(
    long Id,
    string SymlinkPath,
    string OldTarget,
    string? NewTarget,
    string Status,
    string? MatchMethod,
    string? StoreRef,
    string? Error)
{
    public static SymlinkRewriteDto From(MigrationSymlinkRewrite r) => new(
        r.Id, r.SymlinkPath, r.OldTarget, r.NewTarget, r.Status, r.MatchMethod, r.StoreRef, r.Error);
}

public sealed record ReleaseDto(
    string StoreRef,
    string SubmitFileName,
    string JobName,
    bool JobNameDiverges,
    string? AltmountCategory,
    string? TargetCategory,
    string? Verdict,
    string[] VerdictReasons,
    int MetaFileCount,
    long? TotalBytes,
    long EstFetchBytesLazy,
    long EstFetchBytesEager,
    bool IsRarRelease,
    bool HasPassword,
    string? Encryption,
    string? WorstFileStatus,
    bool Included,
    string? CollisionGroupKey)
{
    public static ReleaseDto From(MigrationRelease r)
    {
        string[] reasons;
        try
        {
            reasons = JsonSerializer.Deserialize<string[]>(r.VerdictReasons) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            reasons = Array.Empty<string>();
        }

        var alreadyMigrated = reasons.Contains(VerdictReason.AlreadyMigrated, StringComparer.Ordinal);

        return new ReleaseDto(
            r.StoreRef, r.SubmitFileName, r.JobName, r.JobNameDiverges,
            r.AltmountCategory, r.TargetCategory, alreadyMigrated ? null : r.Verdict, reasons,
            r.MetaFileCount, r.TotalBytes, r.EstFetchBytesLazy, r.EstFetchBytesEager,
            r.IsRarRelease, r.HasPassword, r.Encryption, r.WorstFileStatus,
            r.Included, r.CollisionGroupKey);
    }
}
