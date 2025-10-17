import type { Route } from "./+types/route";
import styles from "./route.module.css"
import { backendClient } from "~/clients/backend-client.server";
import { HealthTable } from "./components/health-table/health-table";
import { HealthStats } from "./components/health-stats/health-stats";
import { useCallback, useEffect, useState } from "react";
import { receiveMessage } from "~/utils/websocket-util";

const topicNames = {
    healthItemStatus: 'hs',
    healthItemProgress: 'hp',
}
const topicSubscriptions = {
    [topicNames.healthItemStatus]: 'event',
    [topicNames.healthItemProgress]: 'state',
}

export async function loader() {
    const enabledKey = 'repair.enable';
    const [queueData, historyData, config] = await Promise.all([
        backendClient.getHealthCheckQueue(30),
        backendClient.getHealthCheckHistory(),
        backendClient.getConfig([enabledKey])
    ]);

    return {
        queueItems: queueData.items,
        historyStats: historyData.stats,
        historyItems: historyData.items,
        isEnabled: config
            .filter(x => x.configName === enabledKey)
            .filter(x => x.configValue.toLowerCase() === "true")
            .length > 0
    };
}

export default function Health({ loaderData }: Route.ComponentProps) {
    const { isEnabled } = loaderData;
    const [historyStats, setHistoryStats] = useState(loaderData.historyStats);
    const [queueItems, setQueueItems] = useState(loaderData.queueItems);

    // effects
    useEffect(() => {
        if (queueItems.length >= 15) return;
        const refetchData = async () => {
            var response = await fetch('/api/get-health-check-queue?pageSize=30');
            if (response.ok) {
                const healthCheckQueue = await response.json();
                setQueueItems(healthCheckQueue.items);
            }
        };
        refetchData();
    }, [queueItems, setQueueItems])

    // events
    const onHealthItemStatus = useCallback(async (message: string) => {
        const [davItemId, healthResult, repairAction] = message.split('|');
        setQueueItems(x => x.filter(item => item.id !== davItemId));

        setHistoryStats(x => {
            const healthResultNum = Number(healthResult);
            const repairActionNum = Number(repairAction);

            // attempt to find and update a matching statistic
            let updated = false;
            const newStats = x.map(stat => {
                if (stat.result === healthResultNum && stat.repairStatus === repairActionNum) {
                    updated = true;
                    return { ...stat, count: stat.count + 1 };
                }
                return stat;
            });

            // if no statistic was updated, add a new one
            if (!updated) {
                return [
                    ...x,
                    {
                        result: healthResultNum,
                        repairStatus: repairActionNum,
                        count: 1
                    }
                ];
            }

            // if an update occurred, return the modified array
            return newStats;
        });
    }, [setQueueItems, setHistoryStats]);

    const onHealthItemProgress = useCallback((message: string) => {
        const [davItemId, progress] = message.split('|');
        setQueueItems(x => x.map(item =>
            item.id === davItemId
                ? { ...item, progress: Number(progress) }
                : item
        ));
    }, [setQueueItems]);

    // websocket
    const onWebsocketMessage = useCallback((topic: string, message: string) => {
        if (topic == topicNames.healthItemStatus)
            onHealthItemStatus(message);
        else if (topic == topicNames.healthItemProgress)
            onHealthItemProgress(message);
    }, [
        onHealthItemStatus,
        onHealthItemProgress
    ]);

    useEffect(() => {
        let ws: WebSocket;
        let disposed = false;
        function connect() {
            ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
            ws.onmessage = receiveMessage(onWebsocketMessage);
            ws.onopen = () => { ws.send(JSON.stringify(topicSubscriptions)); }
            ws.onclose = () => { !disposed && setTimeout(() => connect(), 1000); };
            ws.onerror = () => { ws.close() };
            return () => { disposed = true; ws.close(); }
        }

        return connect();
    }, [onWebsocketMessage]);

    return (
        <div className={styles.container}>
            <div className={styles.section}>
                <HealthStats stats={historyStats} />
            </div>
            <div className={styles.section}>
                <HealthTable isEnabled={isEnabled} healthCheckItems={queueItems.filter((_, index) => index < 10)} />
            </div>
        </div>
    );
}