import styles from "./empty-queue.module.css"

export function EmptyQueue() {
    return (
        <div className={styles.emptyState}>
            <div className={styles.emptyIcon}>🪅🎉🥳</div>
            <div className={styles.emptyTitle}>Empty Queue!</div>
            <div className={styles.emptyDescription}>
                Upload an nzb file to get started
            </div>
        </div>
    );
}