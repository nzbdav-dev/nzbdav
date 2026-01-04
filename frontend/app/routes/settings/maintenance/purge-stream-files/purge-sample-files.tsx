import { Alert, Button, Form } from "react-bootstrap";
import styles from "./purge-sample-files.module.css"
import { useCallback, useEffect, useState } from "react";
import { receiveMessage } from "~/utils/websocket-util";

const purgeSamplesTaskTopic = { 'pstp': 'state' };

type PurgeSampleFilesProps = {
    savedConfig: Record<string, string>
};

export function PurgeSampleFiles({ savedConfig }: PurgeSampleFilesProps) {
    // stateful variables
    const [connected, setConnected] = useState<boolean>(false);
    const [progress, setProgress] = useState<string | null>(null);
    const [isFetching, setIsFetching] = useState<boolean>(false);

    // derived variables
    const isFinished = progress?.startsWith("Done") || progress?.startsWith("Failed") || progress?.startsWith("Aborted");
    const isDone = progress?.startsWith("Done");
    const isRunning = !isFinished && (isFetching || progress !== null);
    const isRunButtonEnabled = connected && !isRunning;
    const runButtonVariant = isRunButtonEnabled ? 'success' : 'secondary';
    const runButtonLabel = isRunning ? "⌛ Running.." : '▶ Run Task';

    // effects
    useEffect(() => {
        let ws: WebSocket;
        let disposed = false;
        function connect() {
            ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
            ws.onmessage = receiveMessage((_, message) => setProgress(message));
            ws.onopen = () => { setConnected(true); ws.send(JSON.stringify(purgeSamplesTaskTopic)); }
            ws.onclose = () => { !disposed && setTimeout(() => connect(), 1000); setProgress(null) };
            ws.onerror = () => { ws.close() };
            return () => { disposed = true; ws.close(); }
        }
        return connect();
    }, [setProgress, setConnected]);

    // events
    const onRun = useCallback(async () => {
        setIsFetching(true);
        await fetch("/api/purge-sample-files");
        setIsFetching(false);
    }, [setIsFetching]);

    const onDryRun = useCallback(async (event: any) => {
        setIsFetching(true);
        await fetch("/api/purge-sample-files/dry-run");
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
        <>
            <Alert className={styles.alert} variant="danger">
                <span style={{ fontWeight: 'bold' }}>Danger</span>
                <ul className={styles.list}>
                    <li className={styles["list-item"]}>
                        Make a backup of your NzbDAV database prior to running this task
                    </li>
                    <li className={styles["list-item"]}>
                        Sample files will be removed from the database and will not be recoverable without a backup
                    </li>
                </ul>
            </Alert>
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
                            {isDone && <>
                                &nbsp;<a href="/api/purge-sample-files/audit">Audit.</a>
                            </>}
                        </div>
                    </div>
                    <Form.Text id="purge-sample-task-progress-help" muted>
                        <br />
                        This task will scan your database for all DavItems ending with '-sample.mkv'.
                        Any matching entry will be deleted.
                        If you would like to see what would be deleted without running the task, you can {dryRunButton}.
                        The dry-run will not delete anything.
                    </Form.Text>
                </Form.Group>
            </div>
        </>
    );
}
