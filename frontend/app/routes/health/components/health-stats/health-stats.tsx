import styles from "./health-stats.module.css";
import type { HealthCheckStats } from "~/clients/backend-client.server";

export type HealthStatsProps = {
    stats: HealthCheckStats[];
}

enum HealthResult {
    Healthy = 0,
    Unhealthy = 1,
}

enum RepairAction {
    None = 0,
    Repaired = 1,
    Deleted = 2,
    ActionNeeded = 3,
}

export function HealthStats({ stats }: HealthStatsProps) {
    // Calculate totals from HealthCheckStats array
    const totalChecked = stats
        .reduce((sum, stat) => sum + stat.count, 0);
    const healthy = stats
        .filter(stat => stat.result === HealthResult.Healthy)
        .reduce((sum, stat) => sum + stat.count, 0);
    const repaired = stats
        .filter(stat => stat.repairStatus === RepairAction.Repaired)
        .reduce((sum, stat) => sum + stat.count, 0);
    const deleted = stats
        .filter(stat => stat.repairStatus === RepairAction.Deleted)
        .reduce((sum, stat) => sum + stat.count, 0);

    const getPercentage = (count: number) => {
        return totalChecked > 0 ? Math.round((count / totalChecked) * 100) : 0;
    };

    return (
        <div className={styles.container}>
            <div className={styles.header}>
                <h3 className={styles.title}>Health Overview</h3>
                <div className={styles.statusIndicator}>
                    <span className={styles.statusLabel}>Last 30 Days</span>
                </div>
            </div>

            <div className={styles.statsGrid}>
                <div className={styles.statCard}>
                    <div className={styles.statNumber}>{totalChecked}</div>
                    <div className={styles.statLabel}>Total Checked</div>
                </div>

                <div className={styles.statCard}>
                    <div className={styles.statNumber} style={{ color: '#198754' }}>{healthy}</div>
                    <div className={styles.statLabel}>Healthy ({getPercentage(healthy)}%)</div>
                </div>

                <div className={styles.statCard}>
                    <div className={styles.statNumber} style={{ color: '#17a2b8' }}>{repaired}</div>
                    <div className={styles.statLabel}>Repaired ({getPercentage(repaired)}%)</div>
                </div>

                <div className={styles.statCard}>
                    <div className={styles.statNumber} style={{ color: '#dc3545' }}>{deleted}</div>
                    <div className={styles.statLabel}>Deleted ({getPercentage(deleted)}%)</div>
                </div>
            </div>
        </div>
    );
}