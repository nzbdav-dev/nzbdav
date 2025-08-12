import { useEffect, useState } from "react";
import styles from "./live-usenet-connections.module.css";

const usenetConnectionsTopic = "cxs";

export function LiveUsenetConnections() {
    const [connections, setConnections] = useState<string | null>(null);
    const parts = (connections || "0|1|0").split("|");
    const [live, max, idle] = parts.map(x => Number(x));
    const active = live - idle;
    const activePercent = 100 * (active / max);
    const livePercent = 100 * (live / max);

    useEffect(() => {
        const ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
        ws.onmessage = (event) => setConnections(event.data);
        ws.onopen = () => ws.send(usenetConnectionsTopic);
        return () => ws.close();
    }, [setConnections]);

    return (
        <div className={styles.container}>
            <div className={styles.title}>
                Usenet Connections
            </div>
            <div className={styles.max}>
                <div className={styles.live} style={{ width: `${livePercent}%` }} />
                <div className={styles.active} style={{ width: `${activePercent}%` }} />
            </div>
            <div className={styles.caption}>
                {connections && `${live} connected / ${max} max`}
                {!connections && `Loading...`}
            </div>
            {connections &&
                <div className={styles.caption}>
                    ( {active} active )
                </div>
            }
        </div>
    );
}