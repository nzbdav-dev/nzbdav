import { Alert, Button, Form } from "react-bootstrap";
import styles from "./remove-unlinked-files.module.css"
import { useCallback, useEffect, useState } from "react";
import { receiveMessage } from "~/utils/websocket-util";

const cleanupTaskTopic = { 'ctp': 'state' };

type RemoveUnlinkedFilesProps = {
    savedConfig: Record<string, string>
};

export function RemoveUnlinkedFiles({ savedConfig }: RemoveUnlinkedFilesProps) {
    // stateful variables
    const [connected, setConnected] = useState<boolean>(false);
    const [progress, setProgress] = useState<string | null>(null);
    const [isFetching, setIsFetching] = useState<boolean>(false);
    const progressMessage = progress?.replace('Dry Run - ', '');

    // derived variables
    const libraryDir = savedConfig["media.library-dir"];
    const isDone = progressMessage?.startsWith("Done");
    const isFinished = progressMessage?.startsWith("Done") || progressMessage?.startsWith("Failed") || progressMessage?.startsWith("Aborted");
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
                        This task will scan your organized media library for all symlinked or *.strm linked files.
                        Any file on the webdav that is not pointed to by your library will be deleted.
                        If you would like to see what would be deleted without running the task, you can {dryRunButton}.
                        The dry-run will not delete anything.
                        <br />
                        <br />
                        Note: Files still present in the History table will not be removed when running this task.
                        It is assumed that files still present in the History table have not yet been imported by Arrs
                        and they are expected to not yet have a corresponding symlink/strm in the Library folder.
                        These files will remain intact until Arrs have a chance to process them and remove them from the
                        History table.
                    </Form.Text>
                </Form.Group>
            </div>
        </>
    );
}