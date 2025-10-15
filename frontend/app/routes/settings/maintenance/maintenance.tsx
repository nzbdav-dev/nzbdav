import { Alert, Button, Form } from "react-bootstrap";
import styles from "./maintenance.module.css"
import { useCallback, useEffect, useState } from "react";
import { receiveMessage } from "~/utils/websocket-util";

const cleanupTaskTopic = { 'ctp': 'state' };

type MaintenanceProps = {
    savedConfig: Record<string, string>
};

export function Maintenance({ savedConfig }: MaintenanceProps) {
    // stateful variables
    const [connected, setConnected] = useState<boolean>(false);
    const [progress, setProgress] = useState<string | null>(null);
    const [isFetching, setIsFetching] = useState<boolean>(false);

    // derived variables
    const libraryDir = savedConfig["media.library-dir"];
    const isDone = progress?.startsWith("Done");
    const isFinished = progress?.startsWith("Done") || progress?.startsWith("Failed") || progress?.startsWith("Aborted");
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
        await fetch("/api/remove-unlinked-files");
        setIsFetching(false);
    }, [setIsFetching]);

    const onDryRun = useCallback(async (event: any) => {
        setIsFetching(true);
        await fetch("/api/remove-unlinked-files/dry-run");
        setIsFetching(false);
    }, [setIsFetching]);

    // view
    const dryRunButton =
        <Button
            className={styles["dryrun-button"]}
            disabled={!isRunButtonEnabled}
            onClick={onDryRun}
            variant="secondary"
            size="sm"
        >
            perform a dry-run
        </Button>;

    return (
        <div className={styles.container}>
            {!libraryDir &&
                <Alert variant="warning">
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
                <Alert variant="danger">
                    <span style={{ fontWeight: 'bold' }}>Danger</span>
                    <ul className={styles.list}>
                        <li className={styles["list-item"]}>
                            Make a backup of your NzbDAV database prior to running this task
                        </li>
                        <li className={styles["list-item"]}>
                            Files will be removed from the webdav and will not be recoverable without a backup
                        </li>
                    </ul>
                </Alert>
            }
            <div className={styles.task}>
                <Form.Group>
                    <Form.Label className={styles.title}>Removed Unlinked Files</Form.Label>
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
                            {isDone && <>
                                &nbsp;<a href="/api/remove-unlinked-files/audit">Audit.</a>
                            </>}
                        </div>
                    </div>
                    <Form.Text id="cleanup-task-progress-help" muted>
                        <br />
                        This task will scan your organized media library for all symlinked files.
                        Any file on the webdav that is not pointed to by your library will be deleted.
                        If you would like to see what would be deleted without running the task, you can {dryRunButton}.
                        The dry-run will not delete anything.
                    </Form.Text>
                </Form.Group>
            </div>
        </div>

    );
}