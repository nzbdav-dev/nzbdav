import type { Route } from "./+types/route";
import styles from "./route.module.css"
import { backendClient } from "~/clients/backend-client.server";
import { HealthStats } from "./components/health-stats/health-stats";

export async function loader() {
    const [queueData, historyData] = await Promise.all([
        backendClient.getHealthCheckQueue(),
        backendClient.getHealthCheckHistory()
    ]);

    return {
        queueItems: queueData.items,
        historyStats: historyData.stats,
        historyItems: historyData.items
    };
}

export default function Health({ loaderData }: Route.ComponentProps) {
    const { queueItems, historyStats, historyItems } = loaderData;

    return (
        <div className={styles.container}>
            <div className={styles.section}>
                <HealthStats stats={historyStats} />
            </div>
        </div>
    );
}