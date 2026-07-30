namespace NzbWebDAV.Services.StreamTrace;

/// <summary>
/// Snapshot of the process-local stream-tracing state. Tracing is ephemeral:
/// UI enablement never persists past a restart, and a TTL auto-disables it.
/// </summary>
public sealed record StreamTraceStatus(
    bool Enabled,
    string Source,
    long ExpiresAtUnixMs,
    int Capacity,
    long EventCount,
    int SessionCount,
    bool Retained,
    long RetainedUntilUnixMs,
    long RetainedEventCount,
    long OverwrittenEventCount,
    long OldestRetainedSequence,
    long NewestRetainedSequence,
    long OldestRetainedAtUnixMs,
    long NewestRetainedAtUnixMs)
{
    /// <summary>The ring wrapped, so the capture is a tail of the reproduction, not all of it.</summary>
    public bool Overflowed => OverwrittenEventCount > 0;
}

internal sealed record StreamTraceSnapshot(
    StreamTraceStatus Status,
    int RetainedSessionCount,
    IReadOnlyList<StreamTraceExportSessionSummary> Sessions,
    IReadOnlyList<StreamTraceEvent> Events);

internal sealed record StreamTraceExportSessionSummary(
    Guid SessionId,
    string? Path,
    long FirstAt,
    long LastAt,
    int? EventCount,
    int RetainedEventCount,
    bool EventsComplete,
    string? LastKind);
