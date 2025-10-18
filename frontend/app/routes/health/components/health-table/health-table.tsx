import { Table, Badge } from "react-bootstrap";
import type { HealthCheckQueueItem } from "~/clients/backend-client.server";
import styles from "./health-table.module.css";
import { Truncate } from "~/routes/queue/components/truncate/truncate";
import { ProgressBadge } from "~/routes/queue/components/status-badge/status-badge";
import { progress } from "~/routes/queue/components/status-badge/status-badge.module.css";

export type HealthTableProps = {
    isEnabled: boolean,
    healthCheckItems: HealthCheckQueueItem[],
}

export function HealthTable({ isEnabled, healthCheckItems }: HealthTableProps) {

    return (
        <div className={styles.container}>
            <div className={styles.header}>
                <h3 className={styles.title}>Health Check Queue</h3>
                <div className={styles.count}>
                    Only {healthCheckItems.length} shown
                </div>
            </div>

            {!isEnabled ? (
                <div className={styles.emptyState}>
                    <div className={styles.emptyIcon}>🩺💙💪</div>
                    <div className={styles.emptyTitle}>Enable Repairs In Settings</div>
                    <div className={styles.emptyDescription}>
                        Once you enable repairs, all mounted usenet files will be queued for continuous health monitoring
                    </div>
                </div>
            ) : healthCheckItems.length === 0 ? (
                <div className={styles.emptyState}>
                    <div className={styles.emptyIcon}>🩺💙💪</div>
                    <div className={styles.emptyTitle}>No Items To Health-Check</div>
                    <div className={styles.emptyDescription}>
                        Once you begin processing nzbs, the mounted usenet files will be queued for continuous health monitoring
                    </div>
                </div>
            ) : (
                <div className={styles.tableContainer}>
                    <Table className={styles.table} responsive>
                        <thead className={styles.desktop}>
                            <tr>
                                <th>Name</th>
                                <th className={styles.desktop}>Created</th>
                                <th className={styles.desktop}>Last Check</th>
                                <th className={styles.desktop}>Next Check</th>
                            </tr>
                        </thead>
                        <tbody>
                            {healthCheckItems.map(item => (
                                <tr key={item.id} className={styles.tableRow}>
                                    <td className={styles.nameCell}>
                                        <div className={styles.nameContainer}>
                                            <div className={styles.name}><Truncate>{item.name}</Truncate></div>
                                            <div className={styles.path}><Truncate>{item.path}</Truncate></div>
                                            <div className={styles.mobile}>
                                                <DateDetailsTable item={item} />
                                            </div>
                                        </div>
                                    </td>
                                    <td className={`${styles.dateCell} ${styles.desktop}`}>
                                        {formatDateBadge(item.releaseDate, 'Unknown', 'info')}
                                    </td>
                                    <td className={`${styles.dateCell} ${styles.desktop}`}>
                                        {formatDateBadge(item.lastHealthCheck, 'Never', 'warning')}
                                    </td>
                                    <td className={`${styles.dateCell} ${styles.desktop}`}>
                                        {item.progress > 0
                                            ? <ProgressBadge className={styles.dateBadge} color={"#333"} percentNum={100 + item.progress}>{item.progress}%</ProgressBadge>
                                            : formatDateBadge(item.nextHealthCheck, 'ASAP', 'success')
                                        }
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </Table>
                </div>
            )}
        </div>
    );
}

function DateDetailsTable({ item }: { item: HealthCheckQueueItem }) {
    return (
        <div className={styles.dateDetailsTable}>
            <div className={styles.dateDetailsRow}>
                <div className={styles.dateDetailsLabel}>Created</div>
                <div className={styles.dateDetailsValue}>
                    {formatDateBadge(item.releaseDate, 'Unknown', 'info')}
                </div>
            </div>
            <div className={styles.dateDetailsRow}>
                <div className={styles.dateDetailsLabel}>Last Health Check</div>
                <div className={styles.dateDetailsValue}>
                    {formatDateBadge(item.lastHealthCheck, 'Never', 'warning')}
                </div>
            </div>
            <div className={styles.dateDetailsRow}>
                <div className={styles.dateDetailsLabel}>Next Health Check</div>
                <div className={styles.dateDetailsValue}>
                    {item.progress > 0
                        ? <ProgressBadge className={styles.dateBadge} color={"#333"} percentNum={100 + item.progress}>{item.progress}%</ProgressBadge>
                        : formatDateBadge(item.nextHealthCheck, 'ASAP', 'success')
                    }
                </div>
            </div>
        </div>
    );
}

function formatDate(dateString: string | null, fallback: string) {
    try {
        if (!dateString) return fallback;
        const now = new Date();
        const datetime = new Date(dateString);
        return isSameDate(datetime, now)
            ? datetime.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
            : datetime.toLocaleDateString();
    } catch {
        return 'Unknown';
    }
};

function formatDateBadge(dateString: string | null, fallback: string, variant: 'info' | 'warning' | 'success') {
    const dateText = formatDate(dateString, fallback);
    return <Badge bg={variant} className={styles.dateBadge}>{dateText}</Badge>;
};

function isSameDate(one: Date, two: Date) {
    return (
        one.getFullYear() === two.getFullYear() &&
        one.getMonth() === two.getMonth() &&
        one.getDate() === two.getDate()
    );
}