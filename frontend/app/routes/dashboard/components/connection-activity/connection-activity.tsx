import styles from "./connection-activity.module.css";
import type { ConnectionState, DashboardProvider } from "../../route";

export type ConnectionActivityProps = {
    providers: DashboardProvider[],
    connections: ConnectionState,
};

// Mirrors backend NzbWebDAV.Models.ProviderType
const ProviderType = {
    Pooled: 1,
    BackupAndStats: 2,
    BackupOnly: 3,
} as const;

function providerTypeLabel(type: number): string {
    if (type === ProviderType.BackupAndStats) return "Backup";
    if (type === ProviderType.BackupOnly) return "Backup";
    return "Primary";
}

export function ConnectionActivity({ providers, connections }: ConnectionActivityProps) {
    return (
        <div className={styles.container}>
            <h3 className={styles.title}>Usenet Connections</h3>
            {providers.length === 0 && (
                <div className={styles.empty}>
                    No usenet providers configured.
                </div>
            )}
            <div className={styles.providers}>
                {providers.map(provider => (
                    <ProviderRow
                        key={provider.index}
                        provider={provider}
                        connection={connections[provider.index]}
                    />
                ))}
            </div>
        </div>
    );
}

type ProviderRowProps = {
    provider: DashboardProvider,
    connection: { live: number, idle: number } | undefined,
};

function ProviderRow({ provider, connection }: ProviderRowProps) {
    const live = connection?.live ?? 0;
    const idle = connection?.idle ?? 0;
    const active = Math.max(0, live - idle);
    const max = Math.max(1, provider.maxConnections);
    const livePercent = Math.min(100, (live / max) * 100);
    const activePercent = Math.min(100, (active / max) * 100);

    return (
        <div className={styles.provider}>
            <div className={styles.providerHeader}>
                <span className={styles.host}>{provider.host}</span>
                <span className={styles.badge}>{providerTypeLabel(provider.type)}</span>
            </div>
            <div className={styles.bar}>
                <div className={styles.barLive} style={{ width: `${livePercent}%` }} />
                <div className={styles.barActive} style={{ width: `${activePercent}%` }} />
            </div>
            <div className={styles.caption}>
                {active} active &middot; {live} connected &middot; {provider.maxConnections} max
            </div>
        </div>
    );
}
