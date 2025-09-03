import { Alert, Button, Form } from "react-bootstrap";
import styles from "./maintenance.module.css"
import { useCallback, useEffect, useState } from "react";
import { receiveMessage } from "~/utils/websocket-util";

const symlinksTaskTopic = { 'stp': 'state' };

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
    const isFinished = progress?.startsWith("complete") || progress?.startsWith("failed");
    const isRunning = !isFinished && (isFetching || progress !== null);
    const isRunButtonEnabled = !!libraryDir && connected && !isRunning;
    const runButtonVariant = isRunButtonEnabled ? 'success' : 'secondary';
    const runButtonLabel = isRunning ? "⌛ Running.." : '▶ Run Task';
    let [retargetted, processed] = ["0", "0"];
    if (isRunning && progress?.includes("/")) {
        const parts = progress.split("/");
        retargetted = parts[0];
        processed = parts[1];
    }

    // effects
    useEffect(() => {
        let ws: WebSocket;
        let disposed = false;
        function connect() {
            ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
            ws.onmessage = receiveMessage((_, message) => setProgress(message));
            ws.onopen = () => { setConnected(true); ws.send(JSON.stringify(symlinksTaskTopic)); }
            ws.onclose = () => { !disposed && setTimeout(() => connect(), 1000); setProgress(null) };
            ws.onerror = () => { ws.close() };
            return () => { disposed = true; ws.close(); }
        }
        return connect();
    }, [setProgress, setConnected]);

    // events
    const onRun = useCallback(async () => {
        setIsFetching(true);
        await fetch("/api/migrate-library-symlinks");
        setIsFetching(false);
    }, [setIsFetching]);

    // view
    return (
        <div className={styles.container}>
            {!libraryDir &&
                <Alert variant="warning">
                    Warning
                    <ul className={styles.list}>
                        <li className={styles["list-item"]}>
                            You must first configure the Library Directory setting before running this task.
                            Head over to the Library tab.
                        </li>
                    </ul>
                </Alert>
            }
            {libraryDir &&
                <Alert variant="danger">
                    <span style={{ fontWeight: 'bold' }}>Danger</span>
                    <ul className={styles.list}>
                        <li className={styles["list-item"]}>
                            Make a backup of your organized media library
                            symlinks before running this task
                        </li>
                        <li className={styles["list-item"]}>
                            Symlinks will be deleted and recreated with the
                            same PUID/PGID that the NzbDAV process is running
                            under
                        </li>
                    </ul>
                </Alert>
            }
            <div className={styles.task}>
                <Form.Group>
                    <Form.Label className={styles.title}>Update Symlink Targets</Form.Label>
                    <div className={styles.run}>
                        <Button variant={runButtonVariant} onClick={onRun} disabled={!isRunButtonEnabled}>
                            {runButtonLabel}
                        </Button>
                        {isRunning &&
                            <div className={styles["task-progress"]}>
                                Processed: {processed} <br />
                                Re-targetted: {retargetted}
                            </div>
                        }
                        {isFinished &&
                            <div className={styles["task-progress"]}>
                                {progress}
                            </div>
                        }
                    </div>
                    <Form.Text id="symlink-task-progress-help" muted>
                        <br />
                        Prior to version 0.3.x, all symlink targets would point to the `/content`
                        folder within the webdav. This caused performance issues with RClone since
                        `/content/tv` often contained thousands of children, which RClone
                        handles <a href="https://github.com/rclone/rclone/issues/8759">inefficiently</a>.
                        <br /><br />
                        Symlinks in version 0.3.x and onward now point to the `/.ids` folder within the
                        webdav, which ensures that no single directory contains any more than a few
                        children. This improves RClone performance and allows the library to scale better.
                        <br /><br />
                        This task recreates previously imported symlinks and re-targets them to the
                        `/.ids` folder. It only needs to happen once. This task will be deprecated and
                        removed when we reach 1.0 release. Be sure to run this task and rebuild your
                        symlinks by then. If you've never used version 0.2.x, then there is no need to
                        run this task. Those who have only used version 0.3.x onward should be good
                        from the start.
                    </Form.Text>
                </Form.Group>
            </div>
        </div>

    );
}