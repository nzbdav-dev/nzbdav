import { useCallback, useEffect, useState } from "react";
import { Alert, Button, Icon } from "~/components/ui";
import { useWebsocketTopic } from "~/utils/shared-websocket";
import {
    toStreamTracingStatus,
    type StreamTracingStatus,
} from "~/utils/stream-tracing-status";

const TOPIC = "strt";

function formatRemaining(expiresAtUnixMs: number, nowMs: number): string {
    if (!expiresAtUnixMs) return "until restart";
    const remainingMs = expiresAtUnixMs - nowMs;
    if (remainingMs <= 0) return "expiring…";
    const totalMinutes = Math.ceil(remainingMs / 60_000);
    if (totalMinutes >= 60) {
        const hours = Math.floor(totalMinutes / 60);
        const minutes = totalMinutes % 60;
        return minutes > 0 ? `${hours}h ${minutes}m left` : `${hours}h left`;
    }
    return `${totalMinutes}m left`;
}

export function StreamTracingBanner() {
    const [status, setStatus] = useState<StreamTracingStatus | null>(null);
    const [now, setNow] = useState(() => Date.now());
    const [busy, setBusy] = useState(false);

    useWebsocketTopic(TOPIC, "state", (message) => {
        try {
            const parsed = JSON.parse(message) as Record<string, unknown>;
            setStatus(toStreamTracingStatus(parsed));
        } catch {
            // ignore malformed payloads
        }
    });

    useEffect(() => {
        if (!status?.enabled) return;
        const id = window.setInterval(() => setNow(Date.now()), 60_000);
        return () => window.clearInterval(id);
    }, [status?.enabled]);

    const turnOff = useCallback(async () => {
        setBusy(true);
        try {
            const form = new FormData();
            form.append("enabled", "false");
            const response = await fetch("/settings/stream-tracing", { method: "POST", body: form });
            if (response.ok) {
                const next = await response.json() as Record<string, unknown>;
                setStatus(toStreamTracingStatus(next));
            }
        } finally {
            setBusy(false);
        }
    }, []);

    if (!status?.enabled) return null;

    const remaining = formatRemaining(status.expiresAtUnixMs, now);
    const counts = `${status.eventCount.toLocaleString()} events across ${status.sessionCount.toLocaleString()} sessions`;
    const fillRatio = status.capacity > 0 ? status.retainedEventCount / status.capacity : 0;
    let fillNote = "";
    if (status.overflowed) {
        fillNote = " · buffer full, oldest events discarded";
    } else if (fillRatio >= 0.8) {
        fillNote = " · trace buffer nearly full";
    }

    return (
        <Alert variant="warning" className="mb-4 items-center justify-between gap-3 text-sm">
            <div className="flex min-w-0 items-start gap-2">
                <Icon name="bug_report" className="mt-0.5 shrink-0 !text-[20px]" />
                <span>
                    Developer stream tracing is on ({remaining}
                    {status.source === "ui" ? "" : ", from STREAM_TRACE_EVENTS"}). {counts}
                    {fillNote}.
                    Tracing uses RAM only and resets on restart.
                </span>
            </div>
            <Button variant="ghost" size="small" disabled={busy} onClick={() => void turnOff()}>
                Turn off
            </Button>
        </Alert>
    );
}
