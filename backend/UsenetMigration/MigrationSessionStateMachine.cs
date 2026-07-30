namespace NzbWebDAV.UsenetMigration;

/// <summary>Canonical persisted values for the migration wizard session state.</summary>
internal static class MigrationSessionStatus
{
    internal const string Idle = "idle";
    internal const string Connected = "connected";
    internal const string Mapped = "mapped";
    internal const string Scanning = "scanning";
    internal const string ScanCancelling = "scan_cancelling";
    internal const string Scanned = "scanned";
    internal const string Running = "running";
    internal const string Paused = "paused";
    internal const string Cancelling = "cancelling";
    internal const string Cancelled = "cancelled";
    internal const string Complete = "complete";
    internal const string Linking = "linking";
    internal const string Linked = "linked";
    internal const string Applying = "applying";
    internal const string Restoring = "restoring";

    internal static IReadOnlyList<string> All { get; } =
    [
        Idle,
        Connected,
        Mapped,
        Scanning,
        ScanCancelling,
        Scanned,
        Running,
        Paused,
        Cancelling,
        Cancelled,
        Complete,
        Linking,
        Linked,
        Applying,
        Restoring,
    ];
}

/// <summary>
/// Named session transitions. Controllers request an operation rather than
/// supplying ad-hoc source and destination strings.
/// </summary>
internal enum MigrationSessionTransition
{
    Connect,
    MapCategories,
    StartScan,
    CancelScan,
    CompleteScanCancellation,
    CompleteScan,
    StartRun,
    CompleteEmptyRun,
    PauseRun,
    ResumeRun,
    BeginCancellation,
    CompleteCancellation,
    CompleteRun,
    ReturnRunToReview,
    StartLinkPlan,
    CompleteLinkPlan,
    StartApply,
    CompleteApply,
    StartRestore,
    CompleteRestore,
}

internal sealed record MigrationSessionTransitionRule(
    string TargetStatus,
    IReadOnlyList<string> SourceStatuses);

internal enum MigrationSessionTransitionOutcome
{
    Applied,
    AlreadyApplied,
    Rejected,
}

internal sealed record MigrationSessionTransitionResult(
    MigrationSessionTransitionOutcome Outcome,
    string CurrentStatus)
{
    internal bool Succeeded => Outcome is not MigrationSessionTransitionOutcome.Rejected;
}

/// <summary>
/// The migration wizard's legal state graph. The store uses these rules for an
/// atomic compare-and-set, so two operations claiming the same resting state
/// cannot both become active.
/// </summary>
internal static class MigrationSessionStateMachine
{
    private static readonly HashSet<string> ActiveStatuses =
    [
        MigrationSessionStatus.Scanning,
        MigrationSessionStatus.ScanCancelling,
        MigrationSessionStatus.Running,
        MigrationSessionStatus.Paused,
        MigrationSessionStatus.Cancelling,
        MigrationSessionStatus.Linking,
        MigrationSessionStatus.Applying,
        MigrationSessionStatus.Restoring,
    ];

    internal static MigrationSessionTransitionRule GetRule(MigrationSessionTransition transition) =>
        transition switch
        {
            MigrationSessionTransition.Connect => new(
                MigrationSessionStatus.Connected,
                [
                    MigrationSessionStatus.Idle,
                    MigrationSessionStatus.Connected,
                    MigrationSessionStatus.Mapped,
                    MigrationSessionStatus.Scanned,
                    MigrationSessionStatus.Complete,
                    MigrationSessionStatus.Cancelled,
                    MigrationSessionStatus.Linked,
                ]),
            MigrationSessionTransition.MapCategories => new(
                MigrationSessionStatus.Mapped,
                [
                    MigrationSessionStatus.Connected,
                    MigrationSessionStatus.Mapped,
                    MigrationSessionStatus.Scanned,
                ]),
            MigrationSessionTransition.StartScan => new(
                MigrationSessionStatus.Scanning,
                [
                    MigrationSessionStatus.Connected,
                    MigrationSessionStatus.Mapped,
                    MigrationSessionStatus.Scanned,
                    MigrationSessionStatus.Complete,
                    MigrationSessionStatus.Cancelled,
                    MigrationSessionStatus.Linked,
                ]),
            MigrationSessionTransition.CancelScan => new(
                MigrationSessionStatus.ScanCancelling,
                [MigrationSessionStatus.Scanning]),
            MigrationSessionTransition.CompleteScanCancellation => new(
                MigrationSessionStatus.Mapped,
                [MigrationSessionStatus.ScanCancelling]),
            MigrationSessionTransition.CompleteScan => new(
                MigrationSessionStatus.Scanned,
                [MigrationSessionStatus.Scanning]),
            MigrationSessionTransition.StartRun => new(
                MigrationSessionStatus.Running,
                [MigrationSessionStatus.Scanned]),
            MigrationSessionTransition.CompleteEmptyRun => new(
                MigrationSessionStatus.Complete,
                [MigrationSessionStatus.Scanned]),
            MigrationSessionTransition.PauseRun => new(
                MigrationSessionStatus.Paused,
                [MigrationSessionStatus.Running]),
            MigrationSessionTransition.ResumeRun => new(
                MigrationSessionStatus.Running,
                [MigrationSessionStatus.Paused]),
            MigrationSessionTransition.BeginCancellation => new(
                MigrationSessionStatus.Cancelling,
                [MigrationSessionStatus.Running, MigrationSessionStatus.Paused]),
            MigrationSessionTransition.CompleteCancellation => new(
                MigrationSessionStatus.Cancelled,
                [MigrationSessionStatus.Cancelling]),
            MigrationSessionTransition.CompleteRun => new(
                MigrationSessionStatus.Complete,
                [MigrationSessionStatus.Running]),
            MigrationSessionTransition.ReturnRunToReview => new(
                MigrationSessionStatus.Scanned,
                [MigrationSessionStatus.Running]),
            MigrationSessionTransition.StartLinkPlan => new(
                MigrationSessionStatus.Linking,
                [MigrationSessionStatus.Complete, MigrationSessionStatus.Linked]),
            MigrationSessionTransition.CompleteLinkPlan => new(
                MigrationSessionStatus.Linked,
                [MigrationSessionStatus.Linking]),
            MigrationSessionTransition.StartApply => new(
                MigrationSessionStatus.Applying,
                [MigrationSessionStatus.Linked]),
            MigrationSessionTransition.CompleteApply => new(
                MigrationSessionStatus.Linked,
                [MigrationSessionStatus.Applying]),
            MigrationSessionTransition.StartRestore => new(
                MigrationSessionStatus.Restoring,
                [MigrationSessionStatus.Linked]),
            MigrationSessionTransition.CompleteRestore => new(
                MigrationSessionStatus.Linked,
                [MigrationSessionStatus.Restoring]),
            _ => throw new ArgumentOutOfRangeException(nameof(transition), transition, null),
        };

    internal static bool CanTransition(MigrationSessionTransition transition, string currentStatus) =>
        GetRule(transition).SourceStatuses.Contains(currentStatus, StringComparer.Ordinal);

    internal static bool IsWorkActive(string status) => ActiveStatuses.Contains(status);
}
