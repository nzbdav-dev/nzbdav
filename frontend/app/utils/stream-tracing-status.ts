export type StreamTracingStatus = {
    enabled: boolean;
    source: string;
    expiresAtUnixMs: number;
    capacity: number;
    eventCount: number;
    sessionCount: number;
    retained: boolean;
    retainedUntilUnixMs: number;
    retainedEventCount: number;
    overwrittenEventCount: number;
    oldestRetainedSequence: number;
    newestRetainedSequence: number;
    oldestRetainedAtUnixMs: number;
    newestRetainedAtUnixMs: number;
    overflowed: boolean;
};

export function toStreamTracingStatus(data: Record<string, unknown>): StreamTracingStatus {
    return {
        enabled: Boolean(data.enabled),
        source: typeof data.source === "string" ? data.source : "env",
        expiresAtUnixMs: Number(data.expiresAtUnixMs ?? 0),
        capacity: Number(data.capacity ?? 0),
        eventCount: Number(data.eventCount ?? 0),
        sessionCount: Number(data.sessionCount ?? 0),
        retained: Boolean(data.retained),
        retainedUntilUnixMs: Number(data.retainedUntilUnixMs ?? 0),
        retainedEventCount: Number(data.retainedEventCount ?? 0),
        overwrittenEventCount: Number(data.overwrittenEventCount ?? 0),
        oldestRetainedSequence: Number(data.oldestRetainedSequence ?? 0),
        newestRetainedSequence: Number(data.newestRetainedSequence ?? 0),
        oldestRetainedAtUnixMs: Number(data.oldestRetainedAtUnixMs ?? 0),
        newestRetainedAtUnixMs: Number(data.newestRetainedAtUnixMs ?? 0),
        overflowed: Boolean(data.overflowed),
    };
}
