import { useEffect, useState } from "react";
import styles from "./live-active-streams.module.css";
import { receiveMessage } from "~/utils/websocket-util";
import { useNavigate } from "react-router";

const activeStreamsTopic = {'as': 'state'};

type StreamEntry = {
    Name: string;
    Downloaded: number;
    Speed: number;
};

function formatBytes(bytes: number): string {
    if (bytes === 0) return "0 B";
    const units = ["B", "KB", "MB", "GB", "TB"];
    const i = Math.floor(Math.log(bytes) / Math.log(1024));
    const value = bytes / Math.pow(1024, i);
    return `${value.toFixed(i > 0 ? 1 : 0)} ${units[i]}`;
}

export function LiveActiveStreams() {
    const navigate = useNavigate();
    const [streams, setStreams] = useState<StreamEntry[]>([]);

    useEffect(() => {
        let ws: WebSocket;
        let disposed = false;
        function connect() {
            ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
            ws.onmessage = receiveMessage((_, message) => {
                try { setStreams(JSON.parse(message)); }
                catch { setStreams([]); }
            });
            ws.onopen = () => ws.send(JSON.stringify(activeStreamsTopic));
            ws.onerror = () => { ws.close() };
            ws.onclose = onClose;
            return () => { disposed = true; ws.close(); }
        }
        function onClose(e: CloseEvent) {
            if (e.code == 1008) navigate('/login');
            !disposed && setTimeout(() => connect(), 1000);
            setStreams([]);
        }
        return connect();
    }, [setStreams]);

    if (streams.length === 0) return null;

    return (
        <div className={styles.container}>
            <div className={styles.header}>
                <div className={styles.title}>Active Streams</div>
                <div className={styles.count}>{streams.length}</div>
            </div>
            <div className={styles.list}>
                {streams.map((stream, i) => (
                    <div key={i}>
                        <div className={styles.name} title={stream.Name}>{stream.Name}</div>
                        <div className={styles.stats}>
                            <span>{formatBytes(stream.Downloaded)}</span>
                            <span>{formatBytes(stream.Speed)}/s</span>
                        </div>
                    </div>
                ))}
            </div>
        </div>
    );
}
