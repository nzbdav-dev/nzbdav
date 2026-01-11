import { Alert, Button, Form } from "react-bootstrap";
import styles from "./recreate-strm-files.module.css";
import { useCallback, useEffect, useState } from "react";
import { receiveMessage } from "~/utils/websocket-util";

const cleanupTaskTopic = { 'crst': 'state' };

type RecreateStrmFilesProps = {
    savedConfig: Record<string, string>
};

export function RecreateStrmFiles({ savedConfig }: RecreateStrmFilesProps) {
    // stateful variables
    const [connected, setConnected] = useState<boolean>(false);
    const [progress, setProgress] = useState<string | null>(null);
    const [isFetching, setIsFetching] = useState<boolean>(false);

    // derived variables
    const completeDlDir = savedConfig["api.completed-downloads-dir"];
    const isFinished = progress?.startsWith("Done") || progress?.startsWith("Failed");
    const isRunning = !isFinished && (isFetching || progress !== null);
    const isRunButtonEnabled = !!completeDlDir && connected && !isRunning;
    const runButtonVariant = isRunButtonEnabled ? 'success' : 'secondary';
    const runButtonLabel = isRunning ? "⌛ Running.." : '▶ Run Task';

    // effects
    useEffect(() => {
        let ws: WebSocket;
        let disposed = false;
        function connect() {
            ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
            ws.onmessage = receiveMessage((_, message) => setProgress(message));
            ws.onopen = () => { setConnected(true); ws.send(JSON.stringify(cleanupTaskTopic)); }
            ws.onclose = () => { !disposed && setTimeout(() => connect(), 1000); setProgress(null) };
            ws.onerror = () => { ws.close() };
            return () => { disposed = true; ws.close(); }
        }
        return connect();
    }, [setProgress, setConnected]);

    // events
    const onRun = useCallback(async () => {
        setIsFetching(true);
        await fetch("/api/recreate-strm-files");
        setIsFetching(false);
    }, [setIsFetching]);

    return (
        <>
            {!completeDlDir &&
                <Alert className={styles.alert} variant="warning">
                    Warning
                    <ul className={styles.list}>
                        <li className={styles["list-item"]}>
                            You must first configure the Completed Downloads Directory setting before running this task.
                            Head over to the SABnzbd tab with selected 'STRM Files' Import Strategy.
                        </li>
                    </ul>
                </Alert>
            }
            <div className={styles.task}>
                <Form.Group>
                    <div className={styles.run}>
                        <Button
                            className={styles["run-button"]}
                            variant={runButtonVariant}
                            onClick={onRun}
                            disabled={!isRunButtonEnabled}
                        >
                            {runButtonLabel}
                        </Button>
                        <div className={styles["task-progress"]}>
                            {progress}
                        </div>
                    </div>
                    <Form.Text id="cleanup-task-progress-help" muted>
                        <br />
                        This task will recreate *.strm files for all available media using the current settings.
                    </Form.Text>
                </Form.Group>
            </div>
        </>
    );
}