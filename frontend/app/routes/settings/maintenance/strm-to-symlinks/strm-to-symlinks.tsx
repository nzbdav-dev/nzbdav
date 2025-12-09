import { Alert, Button, Form } from "react-bootstrap";
import styles from "./strm-to-symlinks.module.css";
import { useCallback, useEffect, useState } from "react";
import { receiveMessage } from "~/utils/websocket-util";

const cleanupTaskTopic = { 'st2sy': 'state' };

type ConvertStrmToSymlinksProps = {
    savedConfig: Record<string, string>
};

export function ConvertStrmToSymlinks({ savedConfig }: ConvertStrmToSymlinksProps) {
    // stateful variables
    const [connected, setConnected] = useState<boolean>(false);
    const [progress, setProgress] = useState<string | null>(null);
    const [isFetching, setIsFetching] = useState<boolean>(false);

    // derived variables
    const libraryDir = savedConfig["media.library-dir"];
    const isDone = progress?.startsWith("Done");
    const isFinished = progress?.startsWith("Done") || progress?.startsWith("Failed");
    const isRunning = !isFinished && (isFetching || progress !== null);
    const isRunButtonEnabled = !!libraryDir && connected && !isRunning;
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
        await fetch("/api/convert-strm-to-symlinks");
        setIsFetching(false);
    }, [setIsFetching]);

    return (
        <>
            {!libraryDir &&
                <Alert className={styles.alert} variant="warning">
                    Warning
                    <ul className={styles.list}>
                        <li className={styles["list-item"]}>
                            You must first configure the Library Directory setting before running this task.
                            Head over to the Repairs tab.
                        </li>
                    </ul>
                </Alert>
            }
            {libraryDir &&
                <Alert className={styles.alert} variant="danger">
                    <span style={{ fontWeight: 'bold' }}>Danger</span>
                    <ul className={styles.list}>
                        <li className={styles["list-item"]}>
                            Make a backup of your entire Library Dir prior to running this task
                        </li>
                        <li className={styles["list-item"]}>
                            Strm files will be deleted from `{libraryDir}` and will not be recoverable without a backup.
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
                        This task will scan your organized media library for all *.strm files.
                        Every *.strm file that links to nzbdav media will be deleted and be replaced by a symlink.
                        The newly created symlinks will all point to the corresponding file within your rclone mount.
                    </Form.Text>
                </Form.Group>
            </div>
        </>
    );
}