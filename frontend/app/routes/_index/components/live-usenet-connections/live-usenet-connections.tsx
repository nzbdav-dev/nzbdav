import { useEffect, useState } from "react";
import styles from "./live-usenet-connections.module.css";
import { receiveMessage } from "~/utils/websocket-util";
import { useNavigate } from "react-router";

const usenetConnectionsTopic = {'cxs': 'state'};

export function LiveUsenetConnections() {
    const navigate = useNavigate();
    const [connections, setConnections] = useState<string | null>(null);
    const parts = (connections || "0|0|0|0|1|0|none").split("|");
    const [_0, _1, _2, live, max, idle, usageBreakdown] = parts;
    const liveNum = Number(live);
    const maxNum = Number(max);
    const idleNum = Number(idle);
    const active = liveNum - idleNum;
    const activePercent = 100 * (active / maxNum);
    const livePercent = 100 * (liveNum / maxNum);

    // Parse usage breakdown (e.g., "Queue=5,Streaming=3,HealthCheck=2,BufferedStreaming=5")
    const usageMap: Record<string, number> = {};
    if (usageBreakdown && usageBreakdown !== 'none') {
        usageBreakdown.split(',').forEach(part => {
            const [type, count] = part.split('=');
            if (type && count) {
                // Map BufferedStreaming to a more user-friendly name
                const displayType = type === 'BufferedStreaming' ? 'Buffered' : type;
                usageMap[displayType] = Number(count);
            }
        });
    }

    useEffect(() => {
        let ws: WebSocket;
        let disposed = false;
        function connect() {
            ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
            ws.onmessage = receiveMessage((_, message) => setConnections(message));
            ws.onopen = () => ws.send(JSON.stringify(usenetConnectionsTopic));
            ws.onerror = () => { ws.close() };
            ws.onclose = onClose;
            return () => { disposed = true; ws.close(); }
        }
        function onClose(e: CloseEvent) {
            if (e.code == 1008) navigate('/login');
            !disposed && setTimeout(() => connect(), 1000);
            setConnections(null);
        }
        return connect();
    }, [setConnections]);

    return (
        <div className={styles.container}>
            <div className={styles.title}>
                Usenet Connections
            </div>
            <div className={styles.bar}>
                <div className={styles.max} />
                <div className={styles.live} style={{ width: `${livePercent}%` }} />
                <div className={styles.active} style={{ width: `${activePercent}%` }} />
            </div>
            <div className={styles.caption}>
                {connections && `${liveNum} connected / ${maxNum} max`}
                {!connections && `Loading...`}
            </div>
            {connections &&
                <div className={styles.caption}>
                    ( {active} active )
                </div>
            }
            {connections && Object.keys(usageMap).length > 0 &&
                <div className={styles.usageBreakdown}>
                    {Object.entries(usageMap).map(([type, count]) => (
                        <div key={type} className={styles.usageItem}>
                            <span className={styles.usageType} data-type={type}>{type}:</span>
                            <span className={styles.usageCount}>{count}</span>
                        </div>
                    ))}
                </div>
            }
        </div>
    );
}