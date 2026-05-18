import type { Route } from "./+types/route";
import styles from "./route.module.css";
import { backendClient } from "~/clients/backend-client.server";
import { HealthStats } from "~/routes/health/components/health-stats/health-stats";
import { ConnectionActivity } from "./components/connection-activity/connection-activity";
import { QueueActivity } from "./components/queue-activity/queue-activity";
import { useCallback, useEffect, useState } from "react";
import { receiveMessage } from "~/utils/websocket-util";
import { useNavigate } from "react-router";
import type { QueueSlot } from "~/clients/backend-client.server";

// Provider roster shape exposed to the browser. Usenet credentials
// (User/Pass) are intentionally stripped server-side in the loader and
// never reach the client bundle.
export type DashboardProvider = {
    index: number,
    host: string,
    type: number,
    maxConnections: number,
};

// Mirrors backend NzbWebDAV.Models.ProviderType
const ProviderType = {
    Disabled: 0,
    Pooled: 1,
    BackupAndStats: 2,
    BackupOnly: 3,
} as const;

// Per-provider live connection counts, keyed by provider index.
export type ConnectionState = Record<number, { live: number, idle: number }>;

const topicSubscriptions = {
    cxs: 'state',  // usenet connections
    qs: 'state',   // queue item status
    qp: 'state',   // queue item progress
    qa: 'event',   // queue item added
    qr: 'event',   // queue item removed
};

function parseProviders(configValue: string | undefined): DashboardProvider[] {
    if (!configValue) return [];
    try {
        const parsed = JSON.parse(configValue) as {
            Providers?: Array<{ Host?: string, Type?: number, MaxConnections?: number }>
        };
        return (parsed.Providers ?? [])
            .map((provider, index) => ({
                index,
                host: provider.Host || `provider ${index + 1}`,
                type: provider.Type ?? ProviderType.Disabled,
                maxConnections: provider.MaxConnections ?? 0,
            }))
            .filter(provider => provider.type !== ProviderType.Disabled);
    } catch {
        return [];
    }
}

export async function loader() {
    const [historyData, queueData, config] = await Promise.all([
        backendClient.getHealthCheckHistory(),
        backendClient.getQueue(100),
        backendClient.getConfig(["usenet.providers"]),
    ]);

    const providersValue = config
        .find(x => x.configName === "usenet.providers")
        ?.configValue;

    return {
        historyStats: historyData.stats,
        queueSlots: queueData?.slots ?? [],
        // Credentials are dropped here — only host/type/limits cross the wire.
        providers: parseProviders(providersValue),
    };
}

export default function Dashboard({ loaderData }: Route.ComponentProps) {
    const navigate = useNavigate();
    const { historyStats, providers } = loaderData;

    const [queueSlots, setQueueSlots] = useState<QueueSlot[]>(loaderData.queueSlots);
    const [connections, setConnections] = useState<ConnectionState>({});

    const onWebsocketMessage = useCallback((topic: string, message: string) => {
        if (topic === 'cxs') {
            // providerIndex|live|idle|totalLive|max|totalIdle
            const [providerIndex, live, idle] = message.split('|').map(Number);
            if (Number.isNaN(providerIndex)) return;
            setConnections(prev => ({ ...prev, [providerIndex]: { live, idle } }));
        } else if (topic === 'qs') {
            const [nzoId, status] = message.split('|');
            setQueueSlots(prev => prev.map(slot =>
                slot.nzo_id === nzoId ? { ...slot, status } : slot));
        } else if (topic === 'qp') {
            const [nzoId, truePercentage] = message.split('|');
            setQueueSlots(prev => prev.map(slot =>
                slot.nzo_id === nzoId ? { ...slot, true_percentage: truePercentage } : slot));
        } else if (topic === 'qa') {
            const slot = JSON.parse(message) as QueueSlot;
            setQueueSlots(prev => prev.some(x => x.nzo_id === slot.nzo_id)
                ? prev
                : [...prev, slot]);
        } else if (topic === 'qr') {
            const removed = new Set(message.split(','));
            setQueueSlots(prev => prev.filter(slot => !removed.has(slot.nzo_id)));
        }
    }, []);

    useEffect(() => {
        let ws: WebSocket;
        let disposed = false;
        function connect() {
            ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
            ws.onmessage = receiveMessage(onWebsocketMessage);
            ws.onopen = () => ws.send(JSON.stringify(topicSubscriptions));
            ws.onerror = () => ws.close();
            ws.onclose = (e: CloseEvent) => {
                if (e.code === 1008) navigate('/login');
                else if (!disposed) setTimeout(connect, 1000);
            };
            return () => { disposed = true; ws.close(); };
        }
        return connect();
    }, [onWebsocketMessage, navigate]);

    return (
        <div className={styles.container}>
            <h2 className={styles.heading}>Dashboard</h2>
            <div className={styles.stack}>
                <ConnectionActivity providers={providers} connections={connections} />
                <QueueActivity slots={queueSlots} />
                <HealthStats stats={historyStats} />
            </div>
        </div>
    );
}
