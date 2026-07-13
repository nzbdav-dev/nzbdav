import { useEffect, useState } from "react";
import styles from "./live-download-speed.module.css";
import { useWebsocketTopic } from "~/utils/websocket-util";

const speedUnitStorageKey = 'nzbdav-speed-unit';

type SpeedUnit = 'MB' | 'KB';

type SpeedSession = {
    name: string,
    category: 'stream' | 'queue' | string,
    rate: number,
};

type SpeedReport = {
    total: number,
    sessions: SpeedSession[],
};

export function LiveDownloadSpeed() {
    const [report, setReport] = useState<SpeedReport | null>(null);
    const [unit, setUnit] = useState<SpeedUnit>('MB');

    useEffect(() => {
        const storedUnit = localStorage.getItem(speedUnitStorageKey);
        if (storedUnit === 'MB' || storedUnit === 'KB') setUnit(storedUnit);
    }, []);

    function toggleUnit() {
        const newUnit = unit === 'MB' ? 'KB' : 'MB';
        setUnit(newUnit);
        localStorage.setItem(speedUnitStorageKey, newUnit);
    }

    useWebsocketTopic('dls', (message) => {
        try {
            const parsed = JSON.parse(message);
            setReport(Array.isArray(parsed?.sessions) ? parsed : null);
        }
        catch { setReport(null); }
    }, () => setReport(null));

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
                {report && <><span>Total:</span><span className={styles.rate}>{formatRate(report.total)}</span></>}
                {!report && `Loading...`}
            </div>
            {report && queueSessions.length > 0 &&
                <div className={styles.caption} onClick={toggleUnit} title="Click to toggle between MB/s and KB/s">
                    <span>Queue:</span>
                    <span className={styles.rate}>{formatRate(queueSessions.reduce((sum, session) => sum + session.rate, 0))}</span>
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
                <span>{title}</span>
                <span className={styles.rate}>{formatRate(totalRate)}</span>
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
