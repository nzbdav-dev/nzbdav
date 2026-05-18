import styles from "./queue-activity.module.css";
import type { QueueSlot } from "~/clients/backend-client.server";

export type QueueActivityProps = {
    slots: QueueSlot[],
};

const maxVisibleItems = 5;

function clampPercent(value: string): number {
    const parsed = Number(value);
    if (Number.isNaN(parsed)) return 0;
    return Math.min(100, Math.max(0, parsed));
}

export function QueueActivity({ slots }: QueueActivityProps) {
    const visible = slots.slice(0, maxVisibleItems);
    const overflow = slots.length - visible.length;

    return (
        <div className={styles.container}>
            <div className={styles.header}>
                <h3 className={styles.title}>Queue</h3>
                <span className={styles.count}>
                    {slots.length} {slots.length === 1 ? "item" : "items"}
                </span>
            </div>
            {slots.length === 0 && (
                <div className={styles.empty}>Queue is idle.</div>
            )}
            <div className={styles.items}>
                {visible.map(slot => {
                    const percent = clampPercent(slot.true_percentage);
                    return (
                        <div key={slot.nzo_id} className={styles.item}>
                            <div className={styles.itemHeader}>
                                <span className={styles.filename}>{slot.filename}</span>
                                <span className={styles.status}>{slot.status}</span>
                            </div>
                            <div className={styles.bar}>
                                <div
                                    className={styles.barFill}
                                    style={{ width: `${percent}%` }}
                                />
                            </div>
                        </div>
                    );
                })}
            </div>
            {overflow > 0 && (
                <div className={styles.more}>
                    + {overflow} more in queue
                </div>
            )}
        </div>
    );
}
