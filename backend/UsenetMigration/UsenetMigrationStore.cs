using Microsoft.EntityFrameworkCore;
using NzbWebDAV.UsenetMigration.Model;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models.UsenetMigration;

namespace NzbWebDAV.UsenetMigration;

public sealed record MigrationDataSummary(int Runs, int Releases, int Files);

internal sealed record MigrationConnectionValues(
    string MetadataRoot,
    string? ConfigPath,
    string? StoreRoot,
    int? MaxQueueDepth,
    int? SubmitWorkers);

internal sealed record MigrationCategoryMappingChange(
    string AltmountCategory,
    string? TargetCategory,
    string Action);

/// <summary>
/// Owns the <see cref="UsenetMigrationDbContext"/>
/// singleton session row's lifecycle, applies the migration on first use, and
/// hands out fresh contexts for the bulk scan/submission work. Registered as a
/// singleton. It constructs contexts directly, as
/// <see cref="Services.HistoryRetentionService"/> does, rather
/// than resolving a scoped context, because the migration DB is separate from the
/// request-scoped <see cref="DavDatabaseContext"/>.
/// </summary>
public sealed class UsenetMigrationStore
{
    /// <summary>The pinned singleton session id enforced by CK_SessionState_Singleton.</summary>
    public const int SessionId = 1;

    /// <summary>
    /// Context factory. Overridable so every operation can target the same
    /// in-memory or temporary database during tests.
    /// </summary>
    internal Func<UsenetMigrationDbContext> ContextFactory { get; set; } =
        static () => new UsenetMigrationDbContext();

    internal Func<bool> DatabaseFileExists { get; set; } =
        static () => File.Exists(UsenetMigrationDbContext.DatabaseFilePath);

    private readonly SemaphoreSlim _databaseInitialization = new(1, 1);
    private bool _databaseInitialized;

    /// <summary>Opens a fresh context for callers doing their own bulk unit of work.</summary>
    public UsenetMigrationDbContext NewContext() => ContextFactory();

    /// <summary>Applies the migration so the SQLite file and schema exist (idempotent).</summary>
    public async Task EnsureDatabaseAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _databaseInitialized))
            return;

        await _databaseInitialization.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_databaseInitialized)
                return;

            await using var ctx = ContextFactory();
            await ctx.Database.MigrateAsync(ct).ConfigureAwait(false);
            await GetOrCreateSessionAsync(ctx, ct).ConfigureAwait(false);
            Volatile.Write(ref _databaseInitialized, true);
        }
        finally
        {
            _databaseInitialization.Release();
        }
    }

    // --- preferences -------------------------------------------------------

    /// <summary>Loads the saved Step 1 and Step 6 values, if any.</summary>
    public async Task<MigrationPreferences?> GetPreferencesAsync(CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        return await ctx.Preferences.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == SessionId, ct)
            .ConfigureAwait(false);
    }

    /// <summary>Upserts the singleton preferences row.</summary>
    public async Task<MigrationPreferences> UpdatePreferencesAsync(
        Action<MigrationPreferences> mutate, CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        var preferences = await ctx.Preferences
            .FirstOrDefaultAsync(x => x.Id == SessionId, ct)
            .ConfigureAwait(false);
        if (preferences is null)
        {
            preferences = new MigrationPreferences { Id = SessionId };
            ctx.Preferences.Add(preferences);
        }

        mutate(preferences);
        preferences.UpdatedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        return preferences;
    }

    /// <summary>
    /// Applies a validated Step 1 connection as one transaction. The guarded
    /// status write is the transaction's first mutation, so a competing active
    /// operation either wins before any roots/categories change or observes the
    /// fully committed connection.
    /// </summary>
    internal async Task<MigrationSessionTransitionResult> ApplyConnectionAsync(
        MigrationConnectionValues values,
        IReadOnlyList<AltmountCategory> categories,
        CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        await GetOrCreateSessionAsync(ctx, ct).ConfigureAwait(false);
        ctx.ChangeTracker.Clear();
        await using var transaction = await ctx.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var transition = await TryTransitionSessionAsync(
                ctx, MigrationSessionTransition.Connect, ct)
            .ConfigureAwait(false);
        if (!transition.Succeeded)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return transition;
        }

        var now = DateTime.UtcNow;
        var session = await ctx.SessionState.SingleAsync(s => s.Id == SessionId, ct)
            .ConfigureAwait(false);
        session.AltmountMetadataRoot = values.MetadataRoot;
        session.AltmountConfigPath = values.ConfigPath;
        session.AltmountStoreRoot = values.StoreRoot;
        if (values.MaxQueueDepth is > 0) session.MaxQueueDepth = values.MaxQueueDepth.Value;
        if (values.SubmitWorkers is > 0) session.SubmitWorkers = values.SubmitWorkers.Value;
        session.UpdatedAt = now;

        var preferences = await ctx.Preferences
            .FirstOrDefaultAsync(p => p.Id == SessionId, ct)
            .ConfigureAwait(false);
        if (preferences is null)
        {
            preferences = new MigrationPreferences { Id = SessionId };
            ctx.Preferences.Add(preferences);
        }

        preferences.AltmountMetadataRoot = values.MetadataRoot;
        preferences.AltmountConfigPath = values.ConfigPath;
        preferences.AltmountStoreRoot = values.StoreRoot;
        if (values.MaxQueueDepth is > 0) preferences.MaxQueueDepth = values.MaxQueueDepth.Value;
        if (values.SubmitWorkers is > 0) preferences.SubmitWorkers = values.SubmitWorkers.Value;
        preferences.UpdatedAt = now;

        await SeedCategoryMapFromConfigAsync(ctx, categories, now, ct).ConfigureAwait(false);
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return transition;
    }

    /// <summary>
    /// Saves Step 6 paths and claims plan generation in one transaction, so the
    /// runner cannot observe <c>linking</c> with stale or partially updated paths.
    /// </summary>
    internal async Task<MigrationSessionTransitionResult> StartSymlinkPlanAsync(
        string libraryRoot,
        string backupDir,
        CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        await GetOrCreateSessionAsync(ctx, ct).ConfigureAwait(false);
        ctx.ChangeTracker.Clear();
        await using var transaction = await ctx.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var transition = await TryTransitionSessionAsync(
                ctx, MigrationSessionTransition.StartLinkPlan, ct)
            .ConfigureAwait(false);
        if (transition.Outcome != MigrationSessionTransitionOutcome.Applied)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return transition;
        }

        var now = DateTime.UtcNow;
        var session = await ctx.SessionState.SingleAsync(s => s.Id == SessionId, ct)
            .ConfigureAwait(false);
        session.SymlinkLibraryRoot = libraryRoot;
        session.SymlinkBackupDir = backupDir;
        session.UpdatedAt = now;

        var preferences = await ctx.Preferences
            .FirstOrDefaultAsync(p => p.Id == SessionId, ct)
            .ConfigureAwait(false);
        if (preferences is null)
        {
            preferences = new MigrationPreferences { Id = SessionId };
            ctx.Preferences.Add(preferences);
        }

        preferences.SymlinkLibraryRoot = libraryRoot;
        preferences.SymlinkBackupDir = backupDir;
        preferences.UpdatedAt = now;

        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return transition;
    }

    // --- session -----------------------------------------------------------

    /// <summary>Loads the singleton session, creating it (Id=1) on first call.</summary>
    public async Task<MigrationSessionState> GetSessionAsync(CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        return await GetOrCreateSessionAsync(ctx, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads-or-creates the singleton session, applies <paramref name="mutate"/>,
    /// stamps <see cref="MigrationSessionState.UpdatedAt"/>, persists, and returns
    /// the saved row.
    /// </summary>
    public async Task<MigrationSessionState> UpdateSessionAsync(
        Action<MigrationSessionState> mutate, CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        var session = await GetOrCreateSessionAsync(ctx, ct).ConfigureAwait(false);
        mutate(session);
        session.UpdatedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        return session;
    }

    /// <summary>
    /// Atomically claims a legal state transition. Competing operations that
    /// require the same source state cannot both succeed. Repeating the same
    /// operation after it reached its target is reported as idempotent success.
    /// </summary>
    internal async Task<MigrationSessionTransitionResult> TryTransitionSessionAsync(
        MigrationSessionTransition transition,
        CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        await GetOrCreateSessionAsync(ctx, ct).ConfigureAwait(false);
        ctx.ChangeTracker.Clear();
        return await TryTransitionSessionAsync(ctx, transition, ct).ConfigureAwait(false);
    }

    private static async Task<MigrationSessionTransitionResult> TryTransitionSessionAsync(
        UsenetMigrationDbContext ctx,
        MigrationSessionTransition transition,
        CancellationToken ct)
    {
        var rule = MigrationSessionStateMachine.GetRule(transition);
        var sourceStatuses = rule.SourceStatuses.ToArray();
        var now = DateTime.UtcNow;
        var changed = await ctx.SessionState
            .Where(s => s.Id == SessionId && sourceStatuses.Contains(s.Status))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.Status, rule.TargetStatus)
                .SetProperty(s => s.UpdatedAt, now), ct)
            .ConfigureAwait(false);

        var currentStatus = await ctx.SessionState.AsNoTracking()
            .Where(s => s.Id == SessionId)
            .Select(s => s.Status)
            .SingleAsync(ct)
            .ConfigureAwait(false);

        var outcome = changed == 1
            ? MigrationSessionTransitionOutcome.Applied
            : string.Equals(currentStatus, rule.TargetStatus, StringComparison.Ordinal)
                ? MigrationSessionTransitionOutcome.AlreadyApplied
                : MigrationSessionTransitionOutcome.Rejected;
        return new MigrationSessionTransitionResult(outcome, currentStatus);
    }

    /// <summary>Captures scan settings only while the claimed scan is still active.</summary>
    internal async Task<bool> CaptureScanStartAsync(
        bool lazyRarEnabled,
        bool windowsSafePaths,
        CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        var now = DateTime.UtcNow;
        var changed = await ctx.SessionState
            .Where(s => s.Id == SessionId && s.Status == MigrationSessionStatus.Scanning)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.ScanStartedAt, now)
                .SetProperty(s => s.ScanCompletedAt, (DateTime?)null)
                .SetProperty(s => s.ScanLazyRarEnabled, lazyRarEnabled)
                .SetProperty(s => s.ScanWindowsSafePaths, windowsSafePaths)
                .SetProperty(s => s.UpdatedAt, now), ct)
            .ConfigureAwait(false);
        return changed == 1;
    }

    internal async Task CompleteScanCancellationAsync(CancellationToken ct = default)
    {
        await TryTransitionSessionAsync(
                MigrationSessionTransition.CompleteScanCancellation, ct)
            .ConfigureAwait(false);
    }

    internal async Task<MigrationSessionTransitionResult> CompleteEmptyRunAsync(
        CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        await GetOrCreateSessionAsync(ctx, ct).ConfigureAwait(false);
        ctx.ChangeTracker.Clear();
        await using var transaction = await ctx.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var transition = await TryTransitionSessionAsync(
                ctx, MigrationSessionTransition.CompleteEmptyRun, ct)
            .ConfigureAwait(false);
        if (!transition.Succeeded)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return transition;
        }

        var now = DateTime.UtcNow;
        await ctx.SessionState
            .Where(s => s.Id == SessionId && s.Status == MigrationSessionStatus.Complete)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.RunCompletedAt, now)
                .SetProperty(s => s.UpdatedAt, now), ct)
            .ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return transition;
    }

    /// <summary>
    /// Starts a durable migration run, or resumes the current one after a pause.
    /// The run survives subsequent wizard resets as provenance.
    /// </summary>
    public async Task<long> BeginRunAsync(CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        var session = await GetOrCreateSessionAsync(ctx, ct).ConfigureAwait(false);
        if (session.CurrentRunId is { } currentRunId)
        {
            var current = await ctx.MigrationRuns
                .FirstOrDefaultAsync(r => r.Id == currentRunId, ct)
                .ConfigureAwait(false);
            if (current is { Status: "running" })
                return current.Id;
        }

        var now = DateTime.UtcNow;
        var run = new MigrationRun
        {
            SourceType = "altmount",
            Status = "running",
            StartedAt = now,
        };
        ctx.MigrationRuns.Add(run);
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        session.CurrentRunId = run.Id;
        session.RunStartedAt = now;
        session.RunCompletedAt = null;
        session.UpdatedAt = now;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        return run.Id;
    }

    /// <summary>Completes the active run and the current wizard session together.</summary>
    public async Task CompleteRunAsync(CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        await GetOrCreateSessionAsync(ctx, ct).ConfigureAwait(false);
        ctx.ChangeTracker.Clear();
        await using var transaction = await ctx.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        // This guarded write is the transaction's first mutation. Pause/cancel
        // and completion therefore cannot both publish a terminal outcome.
        var transition = await TryTransitionSessionAsync(
                ctx, MigrationSessionTransition.CompleteRun, ct)
            .ConfigureAwait(false);
        if (transition.Outcome != MigrationSessionTransitionOutcome.Applied)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return;
        }

        var now = DateTime.UtcNow;
        var session = await ctx.SessionState.SingleAsync(s => s.Id == SessionId, ct)
            .ConfigureAwait(false);
        if (session.CurrentRunId is { } runId)
        {
            var run = await ctx.MigrationRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
                .ConfigureAwait(false);
            if (run is not null)
            {
                run.Status = "completed";
                run.CompletedAt = now;
            }
        }

        session.RunCompletedAt = now;
        session.UpdatedAt = now;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Requests cancellation without making it terminal. The runner keeps this
    /// state until the current external queue boundary has drained and its final
    /// durable claim has been recovered and reconciled.
    /// </summary>
    public async Task<string> BeginCancellationAsync(CancellationToken ct = default)
    {
        var result = await TryTransitionSessionAsync(
                MigrationSessionTransition.BeginCancellation, ct)
            .ConfigureAwait(false);
        return result.CurrentStatus;
    }

    /// <summary>
    /// Publishes terminal cancellation after the runner has drained and
    /// reconciled the current queue boundary. A subsequent run therefore
    /// requires a successful new scan.
    /// </summary>
    public async Task CompleteCancellationAsync(CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        await GetOrCreateSessionAsync(ctx, ct).ConfigureAwait(false);
        ctx.ChangeTracker.Clear();
        await using var transaction = await ctx.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var transition = await TryTransitionSessionAsync(
                ctx, MigrationSessionTransition.CompleteCancellation, ct)
            .ConfigureAwait(false);
        if (transition.Outcome != MigrationSessionTransitionOutcome.Applied)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return;
        }

        var now = DateTime.UtcNow;
        var session = await ctx.SessionState.SingleAsync(s => s.Id == SessionId, ct)
            .ConfigureAwait(false);

        if (session.CurrentRunId is { } runId)
        {
            var run = await ctx.MigrationRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
                .ConfigureAwait(false);
            if (run is { Status: "running" })
            {
                run.Status = "cancelled";
                run.CompletedAt = now;
            }
        }

        session.RunCompletedAt = now;
        session.UpdatedAt = now;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads-or-creates the singleton session on a caller-supplied context (so the
    /// mutation participates in that context's unit of work).
    /// </summary>
    public static async Task<MigrationSessionState> GetOrCreateSessionAsync(
        UsenetMigrationDbContext ctx, CancellationToken ct = default)
    {
        var session = await ctx.SessionState
            .FirstOrDefaultAsync(x => x.Id == SessionId, ct)
            .ConfigureAwait(false);
        if (session is not null) return session;

        var now = DateTime.UtcNow;
        await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT OR IGNORE INTO "SessionState"
                     ("Id", "Status", "MaxQueueDepth", "SubmitWorkers", "CreatedAt", "UpdatedAt")
                 VALUES ({SessionId}, {"idle"}, {20}, {1}, {now}, {now});
                 """,
                ct)
            .ConfigureAwait(false);

        // INSERT OR IGNORE makes singleton creation atomic across the parallel
        // status/summary/categories requests issued when Step 1 first opens.
        return await ctx.SessionState
            .SingleAsync(x => x.Id == SessionId, ct)
            .ConfigureAwait(false);
    }

    // --- scan artifacts ----------------------------------------------------

    /// <summary>
    /// Wipes every artifact of a prior scan (releases cascade to their files and
    /// submissions; scan errors are cleared too), so a re-scan starts clean.
    /// Category mappings and the session row are preserved.
    /// </summary>
    public static async Task ClearScanArtifactsAsync(
        UsenetMigrationDbContext ctx, CancellationToken ct = default)
    {
        // Submissions/ReleaseFiles FK-cascade from Releases, but clear them
        // explicitly so the delete does not depend on cascade being honoured by
        // the provider's bulk path.
        await ctx.Submissions.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await ctx.ReleaseFiles.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await ctx.Releases.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await ctx.ScanErrors.ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Records a non-fatal scan error.</summary>
    public async Task RecordScanErrorAsync(
        string? path, string kind, string message, CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        ctx.ScanErrors.Add(new MigrationScanError
        {
            Path = path,
            Kind = kind,
            Message = message,
            OccurredAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // --- category map ------------------------------------------------------

    /// <summary>Returns the persisted category-mapping rows.</summary>
    public async Task<List<MigrationCategoryMap>> GetCategoryMapAsync(CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        return await ctx.CategoryMap.AsNoTracking()
            .OrderBy(c => c.AltmountCategory)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Seeds config-discovered category rows without clobbering any target mapping
    /// the user has already chosen. Existing rows keep their
    /// <see cref="MigrationCategoryMap.TargetCategory"/> and
    /// <see cref="MigrationCategoryMap.Action"/>; only the source-side metadata is
    /// refreshed.
    /// </summary>
    public async Task SeedCategoryMapFromConfigAsync(
        IReadOnlyList<AltmountCategory> categories, CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        await SeedCategoryMapFromConfigAsync(ctx, categories, DateTime.UtcNow, ct)
            .ConfigureAwait(false);
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedCategoryMapFromConfigAsync(
        UsenetMigrationDbContext ctx,
        IReadOnlyList<AltmountCategory> categories,
        DateTime now,
        CancellationToken ct)
    {
        var existing = await ctx.CategoryMap
            .ToDictionaryAsync(c => c.AltmountCategory, c => c, ct)
            .ConfigureAwait(false);

        foreach (var cat in categories)
        {
            if (existing.TryGetValue(cat.Name, out var row))
            {
                row.AltmountDir = cat.Dir;
                row.AltmountType = cat.Type;
                row.UpdatedAt = now;
            }
            else
            {
                ctx.CategoryMap.Add(new MigrationCategoryMap
                {
                    AltmountCategory = cat.Name,
                    AltmountDir = cat.Dir,
                    AltmountType = cat.Type,
                    TargetCategory = null,
                    Action = "migrate",
                    DiscoveredBy = "config",
                    UpdatedAt = now,
                });
            }
        }
    }

    /// <summary>
    /// Applies the Step 2 mapping batch and its transition back to mapped in one
    /// transaction, preventing a scan from observing a partially saved batch.
    /// </summary>
    internal async Task<MigrationSessionTransitionResult> ApplyCategoryMappingsAsync(
        IReadOnlyList<MigrationCategoryMappingChange> mappings,
        CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        await GetOrCreateSessionAsync(ctx, ct).ConfigureAwait(false);
        ctx.ChangeTracker.Clear();
        await using var transaction = await ctx.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var transition = await TryTransitionSessionAsync(
                ctx, MigrationSessionTransition.MapCategories, ct)
            .ConfigureAwait(false);
        if (!transition.Succeeded)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return transition;
        }

        var now = DateTime.UtcNow;
        foreach (var mapping in mappings)
        {
            var row = await ctx.CategoryMap
                .FirstOrDefaultAsync(c => c.AltmountCategory == mapping.AltmountCategory, ct)
                .ConfigureAwait(false);
            if (row is null)
            {
                row = new MigrationCategoryMap
                {
                    AltmountCategory = mapping.AltmountCategory,
                    DiscoveredBy = "scan",
                };
                ctx.CategoryMap.Add(row);
            }

            row.TargetCategory = mapping.TargetCategory;
            row.Action = mapping.Action;
            row.UpdatedAt = now;
        }

        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return transition;
    }

    /// <summary>
    /// Persists the user's target mapping/action for one Altmount category
    /// (upsert keyed on <see cref="MigrationCategoryMap.AltmountCategory"/>).
    /// </summary>
    public async Task SetCategoryMappingAsync(
        string altmountCategory, string? targetCategory, string action, CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        var row = await ctx.CategoryMap
            .FirstOrDefaultAsync(c => c.AltmountCategory == altmountCategory, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            row = new MigrationCategoryMap { AltmountCategory = altmountCategory, DiscoveredBy = "scan" };
            ctx.CategoryMap.Add(row);
        }

        row.TargetCategory = string.IsNullOrWhiteSpace(targetCategory) ? null : targetCategory;
        row.Action = action;
        row.UpdatedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // --- reset -------------------------------------------------------------

    /// <summary>Counts the durable provenance retained across normal wizard resets.</summary>
    public async Task<MigrationDataSummary> GetMigrationDataSummaryAsync(CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        return new MigrationDataSummary(
            await ctx.MigrationRuns.CountAsync(ct).ConfigureAwait(false),
            await ctx.MigratedReleases.CountAsync(ct).ConfigureAwait(false),
            await ctx.MigratedFiles.CountAsync(ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Clears the current wizard session — scan artifacts, category map, symlink
    /// plan, and session row — while retaining completed migration provenance.
    /// NEVER touches <c>DavItems</c>, SAB history, or migrated content.
    /// </summary>
    public async Task<bool> ResetAsync(CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        await GetOrCreateSessionAsync(ctx, ct).ConfigureAwait(false);
        ctx.ChangeTracker.Clear();
        await using var transaction = await ctx.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (!await TryClaimInactiveSessionAsync(ctx, ct).ConfigureAwait(false))
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return false;
        }

        var current = await ctx.SessionState.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == SessionId, ct)
            .ConfigureAwait(false);
        if (current?.CurrentRunId is { } runId)
        {
            var run = await ctx.MigrationRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
                .ConfigureAwait(false);
            if (run is { Status: "running" })
            {
                run.Status = "reset";
                run.CompletedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }

        await ClearScanArtifactsAsync(ctx, ct).ConfigureAwait(false);
        await ctx.SymlinkRewrites.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await ctx.CategoryMap.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await ctx.SessionState.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        ctx.ChangeTracker.Clear();

        await GetOrCreateSessionAsync(ctx, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Forgets every migration record and current wizard artifact. This is a
    /// metadata-only reset: mounted DAV content and SAB history live in other
    /// stores and are never modified here.
    /// </summary>
    public async Task<bool> ForgetAllMigrationRecordsAsync(CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        await GetOrCreateSessionAsync(ctx, ct).ConfigureAwait(false);
        ctx.ChangeTracker.Clear();
        await using var transaction = await ctx.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (!await TryClaimInactiveSessionAsync(ctx, ct).ConfigureAwait(false))
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return false;
        }

        await ClearScanArtifactsAsync(ctx, ct).ConfigureAwait(false);
        await ctx.SymlinkRewrites.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await ctx.CategoryMap.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await ctx.SessionState.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await ctx.MigratedFiles.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await ctx.MigratedReleases.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await ctx.MigrationRuns.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        ctx.ChangeTracker.Clear();

        await GetOrCreateSessionAsync(ctx, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static async Task<bool> TryClaimInactiveSessionAsync(
        UsenetMigrationDbContext ctx,
        CancellationToken ct)
    {
        var inactiveStatuses = MigrationSessionStatus.All
            .Where(status => !MigrationSessionStateMachine.IsWorkActive(status))
            .ToArray();
        var changed = await ctx.SessionState
            .Where(s => s.Id == SessionId && inactiveStatuses.Contains(s.Status))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.UpdatedAt, DateTime.UtcNow), ct)
            .ConfigureAwait(false);
        return changed == 1;
    }

    // --- submissions -------------------------------------------------------

    /// <summary>
    /// Durably claims a pending release before its external queue mutation. The
    /// assigned Nzo id is retained across retries, allowing crash recovery to
    /// locate the exact queue/history item before deciding whether to resubmit.
    /// </summary>
    public async Task<MigrationSubmission> ClaimSubmissionAsync(
        string storeRef, CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        var submission = await ctx.Submissions
            .SingleOrDefaultAsync(x => x.StoreRef == storeRef, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Migration submission '{storeRef}' does not exist.");

        if (submission.State is not "pending")
            throw new InvalidOperationException(
                $"Migration submission '{storeRef}' cannot be claimed from state '{submission.State}'.");

        if (!Guid.TryParse(submission.NzoId, out var nzoId))
            nzoId = Guid.NewGuid();

        submission.NzoId = nzoId.ToString();
        submission.State = "submitting";
        submission.Attempt++;
        submission.Error = null;
        submission.UpdatedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        return submission;
    }

    /// <summary>
    /// Loads-or-creates a submission row for <paramref name="storeRef"/>, applies
    /// <paramref name="mutate"/>, stamps <see cref="MigrationSubmission.UpdatedAt"/>,
    /// and persists. Used by the worker pool and reconciler to advance lifecycle
    /// state.
    /// </summary>
    public async Task<MigrationSubmission> UpdateSubmissionAsync(
        string storeRef, Action<MigrationSubmission> mutate, CancellationToken ct = default)
    {
        await using var ctx = ContextFactory();
        var submission = await ctx.Submissions
            .FirstOrDefaultAsync(x => x.StoreRef == storeRef, ct)
            .ConfigureAwait(false);
        if (submission is null)
        {
            submission = new MigrationSubmission { StoreRef = storeRef };
            ctx.Submissions.Add(submission);
        }

        mutate(submission);
        submission.UpdatedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        return submission;
    }
}
