import { useCallback } from "react";
import styles from "./empty-queue.module.css"

type LinkClickEvent = React.MouseEvent<HTMLAnchorElement, MouseEvent>;
interface EmptyQueueProps {
    onUploadClicked?: () => void;
}

export function EmptyQueue(props: EmptyQueueProps) {
    const onUploadClicked = useCallback((e: LinkClickEvent) => {
        e.preventDefault();
        props.onUploadClicked?.call(null);
    }, [props.onUploadClicked]);

    return (
        <div className={styles.emptyState}>
            <div className={styles.emptyIcon}>🪅🎉🥳</div>
            <div className={styles.emptyTitle}>Empty Queue!</div>
            <div className={styles.emptyDescription}>
                <a href="#" onClick={onUploadClicked}>Upload an nzb file</a> to get started
            </div>
        </div>
    );
}