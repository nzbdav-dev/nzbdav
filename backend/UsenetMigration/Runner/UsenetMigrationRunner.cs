using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NzbWebDAV.Extensions;
using NzbWebDAV.UsenetMigration.Symlinks;
using NzbWebDAV.Config;
using NzbWebDAV.Queue;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.UsenetMigration.Runner;

/// <summary>
/// The migration's single <see cref="BackgroundService"/> — the state machine that
/// drives the wizard forward from the session's <c>Status</c>. The API advances
/// that status and interrupts the current submission epoch on pause/cancel;
/// this loop performs the actual work, so a process restart resumes wherever
/// the status left off:
/// <list type="bullet">
/// <item><c>scanning</c> → runs a full scan (idempotent; rebuilds all artifacts).</item>
/// <item><c>running</c> → submits up to the queue-depth gate, then reconciles;
///   marks <c>complete</c> when no submission remains in flight.</item>
/// <item><c>paused</c> → reconciles in-flight submissions only, submits nothing.</item>
/// <item><c>cancelling</c> → recovers and reconciles the drained submission
///   boundary, then publishes terminal <c>cancelled</c>.</item>
/// </list>
/// Owns the scan runner, worker pool, and reconciler; nothing else touches the
/// submit/reconcile cadence.
/// </summary>
public sealed class UsenetMigrationRunner : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(3);

    private readonly UsenetMigrationStore _store;
    private readonly ConfigManager _configManager;
    private readonly AltmountScanRunner _scanRunner;
    private readonly SubmissionWorkerPool _workerPool;
    private readonly SubmissionReconciler _reconciler;
    private readonly SymlinkPlanner _symlinkPlanner;
    private readonly SymlinkRewriter _symlinkRewriter;
    private readonly SymlinkRestoreService _symlinkRestoreService;
    private readonly SubmissionOperationGate _scanGate = new();
    private readonly SubmissionOperationGate _submissionGate = new();
    private readonly SubmissionOperationGate _restoreGate = new();

    public UsenetMigrationRunner(
        UsenetMigrationStore store,
        QueueManager queueManager,
        ConfigManager configManager,
        WebsocketManager websocketManager)
    {
        _store = store;
        _configManager = configManager;
        _scanRunner = new AltmountScanRunner(store, configManager);
        _workerPool = new SubmissionWorkerPool(store, queueManager, configManager, websocketManager);
        _reconciler = new SubmissionReconciler(store);
        _symlinkPlanner = new SymlinkPlanner(store, configManager);
        _symlinkRewriter = new SymlinkRewriter(store);
        _symlinkRestoreService = new SymlinkRestoreService(store);
    }

    /// <summary>
    /// Stops the current submit batch before it can begin another external
    /// <c>AddFileAsync</c>. The current individual submission is allowed to finish
    /// so its NzbDAV queue id can still be persisted by the worker.
    /// </summary>
    internal void InterruptSubmissionBatch() => _submissionGate.Interrupt();
    internal void InterruptScan() => _scanGate.Interrupt();

    internal AltmountScanRunner ScanRunnerForTests => _scanRunner;
    internal SubmissionWorkerPool WorkerPoolForTests => _workerPool;
    internal SubmissionReconciler ReconcilerForTests => _reconciler;
    internal SymlinkPlanner SymlinkPlannerForTests => _symlinkPlanner;
    internal SymlinkRewriter SymlinkRewriterForTests => _symlinkRewriter;
    internal SymlinkRestoreService SymlinkRestoreServiceForTests => _symlinkRestoreService;
    internal Task TickOnceForTestsAsync(CancellationToken ct = default) => TickAsync(ct);

    /// <summary>
    /// Runs restore behind both a durable state claim and an in-process operation
    /// boundary. Request cancellation is intentionally ignored after the claim so
    /// a disconnected client cannot interrupt filesystem work before DB updates.
    /// </summary>
    internal async Task<SymlinkRestoreSummary> RestoreSymlinksAsync(
        string fileName,
        CancellationToken requestCancellationToken = default)
    {
        using var restoreOperation = _restoreGate.Begin(CancellationToken.None);
        var transition = await _store.TryTransitionSessionAsync(
                MigrationSessionTransition.StartRestore, requestCancellationToken)
            .ConfigureAwait(false);
        if (transition.Outcome != MigrationSessionTransitionOutcome.Applied)
        {
            throw new InvalidOperationException(
                $"Cannot restore symlinks while migration operation '{transition.CurrentStatus}' is active.");
        }

        try
        {
            return await _symlinkRestoreService.RestoreAsync(fileName, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            await _store.TryTransitionSessionAsync(
                    MigrationSessionTransition.CompleteRestore, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stay dormant until an existing migration DB is present or an
        // authenticated migration API request creates it on first use.
        while (!stoppingToken.IsCancellationRequested && !_store.DatabaseFileExists())
        {
            try
            {
                await Task.Delay(TickInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _store.EnsureDatabaseAsync(stoppingToken).ConfigureAwait(false);
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                if (e.TryGetKnownErrorMessage(out var reason))
                {
                    Log.Warning(
                        "Usenet migration database could not be initialised; runner idle. Reason: {Reason}",
                        reason);
                    Log.Debug(e, "Usenet migration database initialisation failure stack");
                }
                else
                {
                    Log.Error(e, "Usenet migration DB could not be initialised; runner idle.");
                }
                await Task.Delay(TickInterval, stoppingToken).ConfigureAwait(false);
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(TickInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested || SigtermUtil.IsSigtermTriggered())
            {
                return;
            }
            catch (Exception e)
            {
                e.LogWarningKnownOrStack("Usenet migration tick failed.");
                await Task.Delay(TickInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var session = await _store.GetSessionAsync(ct).ConfigureAwait(false);
        switch (session.Status)
        {
            case "scanning":
                await RunScanAsync(ct).ConfigureAwait(false);
                break;

            case "scan_cancelling":
                if (!_scanGate.IsActive)
                    await _store.CompleteScanCancellationAsync(ct).ConfigureAwait(false);
                break;

            case "running":
                await RunTickAsync(ct).ConfigureAwait(false);
                break;

            case "paused":
                await _reconciler.ReconcileAsync(ct).ConfigureAwait(false);
                break;

            case "cancelling":
                if (_submissionGate.IsActive)
                    break;

                // Recover before reconciling in case AddFile committed but the
                // worker did not persist success at the drained boundary.
                await _workerPool.RecoverClaimsAsync(ct).ConfigureAwait(false);
                await _reconciler.ReconcileAsync(ct).ConfigureAwait(false);
                await _store.CompleteCancellationAsync(ct).ConfigureAwait(false);
                break;

            case "linking":
                await RunStep6OperationAsync(
                        "symlink plan",
                        token => _symlinkPlanner.PlanAsync(token),
                        MigrationSessionTransition.CompleteLinkPlan,
                        ct)
                    .ConfigureAwait(false);
                break;

            case "applying":
                await RunStep6OperationAsync(
                        "symlink apply",
                        token => _symlinkRewriter.ApplyAsync(token),
                        MigrationSessionTransition.CompleteApply,
                        ct)
                    .ConfigureAwait(false);
                break;

            case "restoring":
                // A restore normally runs in its originating request while this
                // in-memory gate is active. Without it, the process was interrupted
                // after the durable claim; return to Review for an idempotent retry.
                if (!_restoreGate.IsActive)
                {
                    Log.Warning(
                        "An interrupted symlink restore was returned to Review. Retry the restore to reconcile remaining links.");
                    await _store.TryTransitionSessionAsync(
                            MigrationSessionTransition.CompleteRestore, ct)
                        .ConfigureAwait(false);
                }
                break;
        }
    }

    private async Task RunStep6OperationAsync<T>(
        string operationName,
        Func<CancellationToken, Task<T>> operation,
        MigrationSessionTransition completionTransition,
        CancellationToken ct)
    {
        try
        {
            await operation(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Keep the durable active state so a restart can resume the operation.
            throw;
        }
        catch (Exception e)
        {
            Log.Warning(
                "Usenet migration {Operation} stopped and returned to Review. Reason: {Reason}",
                operationName, e.Message);
            Log.Debug(e, "Usenet migration {Operation} failure stack", operationName);
        }

        await _store.TryTransitionSessionAsync(completionTransition, ct).ConfigureAwait(false);
    }

    private async Task RunScanAsync(CancellationToken ct)
    {
        var interrupted = false;
        using (var scanOperation = _scanGate.Begin(ct))
        {
            // Cancellation may claim scan_cancelling after TickAsync observed
            // scanning but before this operation boundary became visible.
            var session = await _store.GetSessionAsync(ct).ConfigureAwait(false);
            if (session.Status is not "scanning")
                return;

            try
            {
                interrupted = await _scanRunner.RunAsync(scanOperation.Token).ConfigureAwait(false) is null;
            }
            catch (OperationCanceledException) when (
                scanOperation.Token.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                interrupted = true;
            }
        }

        if (interrupted)
            await _store.CompleteScanCancellationAsync(ct).ConfigureAwait(false);
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        // The two global settings sampled during Scan must still hold, or the
        // computed naming/paths may be stale. If either changed, fall back to
        // Review rather than submitting against different rules.
        if (await GlobalsDriftedAsync(ct).ConfigureAwait(false))
        {
            await _store.TryTransitionSessionAsync(
                    MigrationSessionTransition.ReturnRunToReview, ct)
                .ConfigureAwait(false);
            Log.Warning(
                "Usenet migration paused to Review: a global (lazy-RAR parsing or Windows-safe paths) " +
                "changed since Scan. Re-scan before running.");
            return;
        }

        using var submissionOperation = _submissionGate.Begin(ct);

        // A pause/cancel can land after TickAsync read "running" but before this
        // batch begins. Re-read after publishing the operation token so the API
        // can either cancel this operation or make this check fail.
        var session = await _store.GetSessionAsync(ct).ConfigureAwait(false);
        if (session.Status is not "running")
            return;

        await _store.BeginRunAsync(ct).ConfigureAwait(false);
        await _workerPool.SubmitBatchAsync(submissionOperation.Token, ct).ConfigureAwait(false);
        await _reconciler.ReconcileAsync(ct).ConfigureAwait(false);
        await MaybeCompleteAsync(ct).ConfigureAwait(false);
    }

    private async Task<bool> GlobalsDriftedAsync(CancellationToken ct)
    {
        var session = await _store.GetSessionAsync(ct).ConfigureAwait(false);
        // Nothing captured yet ⇒ no basis to compare; let the run proceed.
        if (session.ScanLazyRarEnabled is null && session.ScanWindowsSafePaths is null)
            return false;

        var lazyNow = _configManager.IsLazyRarParsingEnabled();
        var safeNow = PathSanitizer.IsWindowsSafePathsEnabled;
        return (session.ScanLazyRarEnabled is { } lazyThen && lazyThen != lazyNow)
               || (session.ScanWindowsSafePaths is { } safeThen && safeThen != safeNow);
    }

    /// <summary>Marks the session complete once no submission is still in flight.</summary>
    private async Task MaybeCompleteAsync(CancellationToken ct)
    {
        await using var ctx = _store.NewContext();
        var inFlight = await ctx.Submissions
            .CountAsync(s => s.State == "pending" || s.State == "submitting"
                || s.State == "submitted" || s.State == "processing", ct)
            .ConfigureAwait(false);
        if (inFlight > 0)
            return;

        await _store.CompleteRunAsync(ct).ConfigureAwait(false);

        Log.Information("Usenet migration complete: no submissions remain in flight.");
    }
}

/// <summary>
/// Owns the cancellation epoch for one runner submit batch. Interrupting an
/// epoch cancels its token; a later resume starts a distinct, uncancelled epoch.
/// </summary>
internal sealed class SubmissionOperationGate
{
    private readonly object _lock = new();
    private CancellationTokenSource? _active;
    private long _epoch;

    internal SubmissionOperation Begin(CancellationToken hostStoppingToken)
    {
        lock (_lock)
        {
            if (_active is not null)
                throw new InvalidOperationException("A migration submission batch is already active.");

            var source = CancellationTokenSource.CreateLinkedTokenSource(hostStoppingToken);
            _active = source;
            return new SubmissionOperation(this, source, ++_epoch);
        }
    }

    internal void Interrupt()
    {
        lock (_lock)
        {
            _epoch++;
            _active?.Cancel();
        }
    }

    internal bool IsActive
    {
        get
        {
            lock (_lock)
                return _active is not null;
        }
    }

    private void End(CancellationTokenSource source)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_active, source))
                _active = null;
            source.Dispose();
        }
    }

    internal sealed class SubmissionOperation(
        SubmissionOperationGate owner,
        CancellationTokenSource source,
        long epoch) : IDisposable
    {
        private int _disposed;

        internal CancellationToken Token => source.Token;
        internal long Epoch { get; } = epoch;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.End(source);
        }
    }
}
