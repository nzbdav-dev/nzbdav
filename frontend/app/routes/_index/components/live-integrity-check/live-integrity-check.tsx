import { useEffect, useState } from "react";
import styles from "./live-integrity-check.module.css";
import { receiveMessage } from "~/utils/websocket-util";

const integrityProgressTopic = { 'icp': 'state' };

export function LiveIntegrityCheck() {
    const [progressMessage, setProgressMessage] = useState<string | null>(null);
    const [progressData, setProgressData] = useState<{
        current: number;
        total: number;
        valid: number;
        corrupt: number;
        isRunning: boolean;
        status: string;
    } | null>(null);

    // Parse progress message to extract useful data
    useEffect(() => {
        if (!progressMessage) {
            setProgressData(null);
            return;
        }

        console.debug("Live integrity check parsing message:", progressMessage);

        // Check if check is running based on progress message
        const isRunning = !!progressMessage && 
            !progressMessage.startsWith("complete") && 
            !progressMessage.startsWith("failed") && 
            !progressMessage.startsWith("cancelled") &&
            progressMessage !== "no_files";

        if (!isRunning) {
            setProgressData(null);
            return;
        }

        // Parse different message formats
        let current = 0;
        let total = 0;
        let valid = 0;
        let corrupt = 0;
        let status = progressMessage;

        // Look for patterns like "5/150 (3 corrupt)" - the main progress format
        const progressMatch = progressMessage.match(/^(\d+)\/(\d+)\s+\((\d+)\s+corrupt\)/);
        if (progressMatch) {
            current = parseInt(progressMatch[1]);
            total = parseInt(progressMatch[2]);
            corrupt = parseInt(progressMatch[3]);
            valid = current - corrupt; // Valid files = total processed - corrupt
            status = `${current}/${total} files`;
            if (corrupt > 0) {
                status += ` (${corrupt} corrupt)`;
            }
        }

        // Look for complete patterns like "complete: 150/150 checked, 0 corrupt files found"
        const completeMatch = progressMessage.match(/^complete:\s+(\d+)\/(\d+)\s+checked,\s+(\d+)\s+corrupt/);
        if (completeMatch) {
            current = parseInt(completeMatch[1]);
            total = parseInt(completeMatch[2]);
            corrupt = parseInt(completeMatch[3]);
            valid = current - corrupt; // Valid files = total processed - corrupt
            status = `Complete: ${current}/${total}`;
            if (corrupt > 0) {
                status += ` (${corrupt} corrupt)`;
            }
        }

        // Fallback: just show the message
        if (current === 0 && total === 0) {
            if (progressMessage === "starting") {
                status = "Starting...";
            } else if (progressMessage.includes("scan")) {
                status = "Scanning files...";
            } else {
                status = progressMessage;
            }
        }

        const data = {
            current,
            total,
            valid,
            corrupt,
            isRunning,
            status
        };
        
        console.debug("Live integrity check parsed data:", data);
        setProgressData(data);
    }, [progressMessage]);

    useEffect(() => {
        let ws: WebSocket;
        let disposed = false;
        function connect() {
            ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
            ws.onmessage = receiveMessage((_, message) => {
                // Parse message and run ID (format: "message:runId")
                const parts = message ? message.split(':') : [];
                const messageText = parts[0] || message || '';
                setProgressMessage(messageText);
            });
            ws.onopen = () => ws.send(JSON.stringify(integrityProgressTopic));
            ws.onclose = () => { 
                if (!disposed) setTimeout(() => connect(), 1000); 
                setProgressMessage(null);
            };
            ws.onerror = () => { ws.close() };
            return () => { disposed = true; ws.close(); }
        }
        return connect();
    }, []);

    // Don't render if no check is running
    if (!progressData?.isRunning) {
        return null;
    }

    // Calculate percentages for each segment
    const validPercent = progressData.total > 0 
        ? (progressData.valid / progressData.total) * 100 
        : 0;
    const corruptPercent = progressData.total > 0 
        ? (progressData.corrupt / progressData.total) * 100 
        : 0;
    const totalProcessedPercent = progressData.total > 0 
        ? (progressData.current / progressData.total) * 100 
        : 0;
        
    console.debug("Progress bar calculation:", {
        current: progressData.current,
        total: progressData.total,
        valid: progressData.valid,
        corrupt: progressData.corrupt,
        validPercent,
        corruptPercent,
        totalProcessedPercent
    });

    return (
        <div className={styles.container}>
            <div className={styles.title}>
                🔍 Integrity Check
            </div>
            <div className={styles.bar}>
                <div className={styles.max} />
                <div className={styles.validProgress} style={{ width: `${validPercent}%` }} />
                <div className={styles.corruptProgress} style={{ width: `${corruptPercent}%`, left: `${validPercent}%` }} />
            </div>
            <div className={styles.caption}>
                {progressData.status}
            </div>
            {progressData.total > 0 && (
                <div className={styles.caption}>
                    {Math.round(totalProcessedPercent)}% complete
                    {progressData.valid > 0 && ` • ${progressData.valid} valid`}
                    {progressData.corrupt > 0 && ` • ${progressData.corrupt} corrupt`}
                </div>
            )}
        </div>
    );
}
