import { useEffect, useState } from "react";
import styles from "./live-download-speed.module.css";
import { receiveMessage } from "~/utils/websocket-util";
import { useNavigate } from "react-router";

const downloadSpeedTopic = { 'dls': 'state' };
const speedUnitStorageKey = 'nzbdav-speed-unit';

type SpeedUnit = 'MB' | 'KB';

type SpeedSession = {
    name: string,
    category: 'stream' | 'queue' | string,
    rate: number,
};

type SpeedReport = {
    total: number,
    streams: number,
    sessions: SpeedSession[],
};

export function LiveDownloadSpeed() {
    const navigate = useNavigate();
    const [report, setReport] = useState<SpeedReport | null>(null);
    const [unit, setUnit] = useState<SpeedUnit>('MB');

    useEffect(() => {
        const storedUnit = localStorage.getItem(speedUnitStorageKey);
        if (storedUnit === 'MB' || storedUnit === 'KB') setUnit(storedUnit);
    }, [setUnit]);

    function toggleUnit() {
        const newUnit = unit === 'MB' ? 'KB' : 'MB';
        setUnit(newUnit);
        localStorage.setItem(speedUnitStorageKey, newUnit);
    }

    useEffect(() => {
        let ws: WebSocket;
        let disposed = false;
        function connect() {
            ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
            ws.onmessage = receiveMessage((_, message) => {
                try { setReport(JSON.parse(message)); }
                catch { setReport(null); }
            });
            ws.onopen = () => ws.send(JSON.stringify(downloadSpeedTopic));
            ws.onerror = () => { ws.close() };
            ws.onclose = onClose;
            return () => { disposed = true; ws.close(); }
        }
        function onClose(e: CloseEvent) {
            if (e.code == 1008) navigate('/login');
            !disposed && setTimeout(() => connect(), 1000);
            setReport(null);
        }
        return connect();
    }, [setReport]);

    function formatRate(bytesPerSecond: number): string {
        return unit === 'MB'
            ? `${(bytesPerSecond / (1024 * 1024)).toFixed(2)} MB/s`
            : `${Math.round(bytesPerSecond / 1024).toLocaleString()} KB/s`;
    }

    const streamSessions = report?.sessions.filter(x => x.category === 'stream') || [];
    const queueSessions = report?.sessions.filter(x => x.category === 'queue') || [];

    return (
        <div className={styles.container}>
            <div className={styles.title}>
                Usenet Download Speed
            </div>
            <div className={styles.caption} onClick={toggleUnit} title="Click to toggle between MB/s and KB/s">
                {report && <>Total: {formatRate(report.total)}</>}
                {!report && `Loading...`}
            </div>
            {report && queueSessions.length > 0 &&
                <div className={styles.caption} onClick={toggleUnit} title="Click to toggle between MB/s and KB/s">
                    Queue: {formatRate(queueSessions.reduce((sum, session) => sum + session.rate, 0))}
                </div>
            }
            <SessionSection title="Streams:" sessions={streamSessions} formatRate={formatRate} onClick={toggleUnit} />
        </div>
    );
}

type SessionSectionProps = {
    title: string,
    sessions: SpeedSession[],
    formatRate: (bytesPerSecond: number) => string,
    onClick: () => void,
};

function SessionSection({ title, sessions, formatRate, onClick }: SessionSectionProps) {
    if (sessions.length === 0) return null;
    const totalRate = sessions.reduce((sum, session) => sum + session.rate, 0);
    return (
        <div className={styles.section}>
            <div className={styles["section-title"]} onClick={onClick} title="Click to toggle between MB/s and KB/s">
                {title} {formatRate(totalRate)}
            </div>
            {sessions.map((session, index) => (
                <div className={styles.session} key={index} title={session.name}>
                    <span className={styles.name}>{session.name}</span>
                    <span className={styles.rate}>{formatRate(session.rate)}</span>
                </div>
            ))}
        </div>
    );
}
