import { Table, Badge } from "react-bootstrap";
import type { HealthCheckQueueItem } from "~/clients/backend-client.server";
import styles from "./health-table.module.css";
import { Truncate } from "~/routes/queue/components/truncate/truncate";

export type HealthTableProps = {
    healthCheckItems: HealthCheckQueueItem[],
}

export function HealthTable({ healthCheckItems }: HealthTableProps) {

    return (
        <div className={styles.container}>
            <div className={styles.header}>
                <h3 className={styles.title}>Health Check Queue</h3>
                <div className={styles.count}>
                    Only {healthCheckItems.length} shown
                </div>
            </div>

            {healthCheckItems.length === 0 ? (
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
                                <th className={styles.desktop}>Release Date</th>
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
                                        {formatDateBadge(item.nextHealthCheck, 'ASAP', 'success')}
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
                <div className={styles.dateDetailsLabel}>Release Date</div>
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
                    {formatDateBadge(item.nextHealthCheck, 'ASAP', 'success')}
                </div>
            </div>
        </div>
    );
}

function formatDate(dateString: string | null, fallback: string) {
    try {
        return dateString ? new Date(dateString).toLocaleDateString() : fallback;
    } catch {
        return fallback;
    }
};

function formatDateBadge(dateString: string | null, fallback: string, variant: 'info' | 'warning' | 'success') {
    const dateText = formatDate(dateString, fallback);
    return <Badge bg={variant} className={styles.dateBadge}>{dateText}</Badge>;
};