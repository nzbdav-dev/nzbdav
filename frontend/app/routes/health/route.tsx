import type { Route } from "./+types/route";
import styles from "./route.module.css"
import { backendClient } from "~/clients/backend-client.server";
import { HealthTable } from "./components/health-table/health-table";
import { HealthStats } from "./components/health-stats/health-stats";
import { useCallback, useEffect, useState } from "react";
import { receiveMessage } from "~/utils/websocket-util";
import { Alert, Badge, Button, Modal, Table } from "react-bootstrap";
import { Truncate } from "../queue/components/truncate/truncate";

const topicNames = {
    healthItemStatus: 'hs',
    healthItemProgress: 'hp',
}
const topicSubscriptions = {
    [topicNames.healthItemStatus]: 'event',
    [topicNames.healthItemProgress]: 'event',
}

export async function loader() {
    const enabledKey = 'repair.enable';
    const [queueData, historyData, config] = await Promise.all([
        backendClient.getHealthCheckQueue(30),
        backendClient.getHealthCheckHistory(100),
        backendClient.getConfig([enabledKey])
    ]);

    return {
        uncheckedCount: queueData.uncheckedCount,
        queueItems: queueData.items,
        historyStats: historyData.stats,
        historyItems: historyData.items,
        isEnabled: config
            .filter(x => x.configName === enabledKey)
            .filter(x => x.configValue.toLowerCase() === "true")
            .length > 0
    };
}

export default function Health({ loaderData }: Route.ComponentProps) {
    const { isEnabled } = loaderData;
    const [historyStats, setHistoryStats] = useState(loaderData.historyStats);
    const [queueItems, setQueueItems] = useState(loaderData.queueItems);
    const [uncheckedCount, setUncheckedCount] = useState(loaderData.uncheckedCount);
    const [modalVisible, setModalVisible] = useState(false);
    const [modalFilter, setModalFilter] = useState<"deleted" | "repaired" | null>(null);
    const [modalItems, setModalItems] = useState<HealthCheckResult[]>([]);
    const [modalPage, setModalPage] = useState(1);
    const [modalHasMore, setModalHasMore] = useState(false);
    const [modalLoading, setModalLoading] = useState(false);
    const [modalCsvUrl, setModalCsvUrl] = useState<string | null>(null);

    // effects
    useEffect(() => {
        if (queueItems.length >= 15) return;
        const refetchData = async () => {
            var response = await fetch('/api/get-health-check-queue?pageSize=30');
            if (response.ok) {
                const healthCheckQueue = await response.json();
                setQueueItems(healthCheckQueue.items);
                setUncheckedCount(healthCheckQueue.uncheckedCount);
            }
        };
        refetchData();
    }, [queueItems, setQueueItems])

    // events
    const onHealthItemStatus = useCallback(async (message: string) => {
        const [davItemId, healthResult, repairAction] = message.split('|');
        setQueueItems(x => x.filter(item => item.id !== davItemId));
        setUncheckedCount(x => x - 1);
        setHistoryStats(x => {
            const healthResultNum = Number(healthResult);
            const repairActionNum = Number(repairAction);

            // attempt to find and update a matching statistic
            let updated = false;
            const newStats = x.map(stat => {
                if (stat.result === healthResultNum && stat.repairStatus === repairActionNum) {
                    updated = true;
                    return { ...stat, count: stat.count + 1 };
                }
                return stat;
            });

            // if no statistic was updated, add a new one
            if (!updated) {
                return [
                    ...x,
                    {
                        result: healthResultNum,
                        repairStatus: repairActionNum,
                        count: 1
                    }
                ];
            }

            // if an update occurred, return the modified array
            return newStats;
        });
        // refresh recent history to reflect the new entry
    }, [setQueueItems, setHistoryStats]);

    const onHealthItemProgress = useCallback((message: string) => {
        const [davItemId, progress] = message.split('|');
        if (progress === "done") return;
        setQueueItems(queueItems => {
            var index = queueItems.findIndex(x => x.id === davItemId);
            if (index === -1) return queueItems;
            return queueItems
                .filter((_, i) => i >= index)
                .map(item => item.id === davItemId
                    ? { ...item, progress: Number(progress) }
                    : item
                )
        });
    }, [setQueueItems]);

    // websocket
    const onWebsocketMessage = useCallback((topic: string, message: string) => {
        if (topic == topicNames.healthItemStatus)
            onHealthItemStatus(message);
        else if (topic == topicNames.healthItemProgress)
            onHealthItemProgress(message);
    }, [
        onHealthItemStatus,
        onHealthItemProgress
    ]);

    useEffect(() => {
        let ws: WebSocket;
        let disposed = false;
        function connect() {
            ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
            ws.onmessage = receiveMessage(onWebsocketMessage);
            ws.onopen = () => { ws.send(JSON.stringify(topicSubscriptions)); }
            ws.onclose = () => { !disposed && setTimeout(() => connect(), 1000); };
            ws.onerror = () => { ws.close() };
            return () => { disposed = true; ws.close(); }
        }

        return connect();
    }, [onWebsocketMessage]);

    const fetchFilteredHistory = useCallback(async (filter: "deleted" | "repaired", page: number, append: boolean) => {
        setModalLoading(true);
        try {
            const status = filter === "deleted" ? RepairAction.Deleted : RepairAction.Repaired;
            const response = await fetch(`/api/get-health-check-history?pageSize=200&page=${page}&repairStatus=${status}`);
            if (response.ok) {
                const data: HealthCheckHistoryResponse = await response.json();
                setModalItems(prev => append ? [...prev, ...data.items] : data.items);
                setModalHasMore(Boolean(data.hasMore));
                setModalPage(page);
            }
        } finally {
            setModalLoading(false);
        }
    }, []);

    const onStatClick = useCallback((filter: "deleted" | "repaired") => {
        setModalFilter(filter);
        setModalVisible(true);
        fetchFilteredHistory(filter, 1, false);
    }, [fetchFilteredHistory]);

    const onLoadMore = useCallback(() => {
        if (!modalFilter || modalLoading || !modalHasMore) return;
        fetchFilteredHistory(modalFilter, modalPage + 1, true);
    }, [modalFilter, modalPage, modalHasMore, modalLoading, fetchFilteredHistory]);

    const onCloseModal = useCallback(() => {
        setModalVisible(false);
        setModalItems([]);
        setModalFilter(null);
        setModalHasMore(false);
        setModalPage(1);
    }, []);

    useEffect(() => {
        if (modalCsvUrl) URL.revokeObjectURL(modalCsvUrl);
        const csv = toCsv(modalItems);
        const blob = new Blob([csv], { type: "text/csv" });
        setModalCsvUrl(URL.createObjectURL(blob));
        return () => { if (modalCsvUrl) URL.revokeObjectURL(modalCsvUrl); };
    }, [modalItems]);

    return (
        <div className={styles.container}>
            <div className={styles.section}>
                <HealthStats stats={historyStats} onStatClick={onStatClick} />
            </div>
            {isEnabled && uncheckedCount > 20 &&
                <Alert className={styles.alert} variant={'warning'}>
                    <b>Attention</b>
                    <ul className={styles.list}>
                        <li className={styles.listItem}>
                            You have ~{uncheckedCount} files whose health has never been determined.
                        </li>
                        <li className={styles.listItem}>
                            The queue will run an initial health check of these files.
                        </li>
                        <li className={styles.listItem}>
                            Under normal operation, health checks will occur much less frequently.
                        </li>
                    </ul>
                </Alert>
            }
            <div className={styles.section}>
                <HealthTable isEnabled={isEnabled} healthCheckItems={queueItems.filter((_, index) => index < 10)} />
            </div>
            <HistoryModal
                show={modalVisible}
                onHide={onCloseModal}
                items={modalItems}
                filter={modalFilter}
                hasMore={modalHasMore}
                onLoadMore={onLoadMore}
                loading={modalLoading}
                csvUrl={modalCsvUrl}
            />
        </div>
    );
}

type HistoryModalProps = {
    show: boolean,
    onHide: () => void,
    items: HealthCheckResult[],
    filter: "deleted" | "repaired" | null,
    hasMore: boolean,
    onLoadMore: () => void,
    loading: boolean,
    csvUrl: string | null,
};

function HistoryModal({ show, onHide, items, filter, hasMore, onLoadMore, loading, csvUrl }: HistoryModalProps) {
    const onCopy = async () => {
        try {
            await navigator.clipboard.writeText(toCsv(items));
        } catch {
            // ignore copy errors
        }
    };

    const title = filter === "deleted" ? "Deleted" : filter === "repaired" ? "Repaired" : "History";

    return (
        <Modal show={show} onHide={onHide} dialogClassName={styles.modalWide} centered scrollable>
            <Modal.Header closeButton>
                <Modal.Title>{title} Items</Modal.Title>
            </Modal.Header>
            <Modal.Body className={styles.modalBody}>
                {items.length === 0
                    ? <div className={styles.historyEmpty}>No items to show.</div>
                    : (
                        <div className={styles.modalTableContainer}>
                            <Table className={styles.modalTable} responsive>
                                <thead>
                                    <tr>
                                        <th>Name</th>
                                        <th>Message</th>
                                        <th>When</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {items.map(item => (
                                        <tr key={item.id}>
                                            <td className={styles.historyCell}>
                                                <div className={styles.nameContainer}>
                                                    <div className={styles.name}><Truncate>{getName(item.path)}</Truncate></div>
                                                    <div className={styles.path}><Truncate>{item.path}</Truncate></div>
                                                </div>
                                            </td>
                                            <td className={styles.historyCell}><Truncate>{simplifyMessage(item.message)}</Truncate></td>
                                            <td className={styles.historyCell}>{formatDate(item.createdAt)}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </Table>
                        </div>
                    )
                }
            </Modal.Body>
            <Modal.Footer className={styles.modalFooter}>
                <div className={styles.historyButtons}>
                    <Button size="sm" variant="secondary" onClick={onCopy} disabled={items.length === 0}>Copy CSV</Button>
                    {csvUrl && <a className={styles.historyButtonLink} href={csvUrl} download={`${title.toLowerCase()}-history.csv`}>Download CSV</a>}
                </div>
                <div className={styles.modalActions}>
                    {hasMore && <Button variant="outline-primary" onClick={onLoadMore} disabled={loading}>Load more</Button>}
                    <Button variant="secondary" onClick={onHide}>Close</Button>
                </div>
            </Modal.Footer>
        </Modal>
    );
}

function renderAction(action: RepairAction) {
    const map: Record<number, { text: string, variant: string }> = {
        [RepairAction.Repaired]: { text: "Repaired (Arr)", variant: "success" },
        [RepairAction.Deleted]: { text: "Deleted", variant: "danger" },
        [RepairAction.ActionNeeded]: { text: "Needs Replacement", variant: "warning" },
        [RepairAction.None]: { text: "None", variant: "secondary" },
    };
    const meta = map[action] ?? map[RepairAction.None];
    return <Badge bg={meta.variant} className={styles.historyBadge}>{meta.text}</Badge>;
}

function formatDate(dateString: string) {
    try {
        const datetime = new Date(dateString);
        const now = new Date();
        const sameDay = datetime.toDateString() === now.toDateString();
        return sameDay
            ? datetime.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
            : datetime.toLocaleString();
    } catch {
        return "Unknown";
    }
}

function toCsv(items: HealthCheckResult[]) {
    const header = ["createdAt", "path", "action", "message"];
    const rows = items.map(x => [
        x.createdAt,
        `"${(x.path || "").replace(/"/g, '""')}"`,
        RepairAction[x.repairStatus],
        `"${(x.message || "").replace(/"/g, '""')}"`,
    ]);
    return [header.join(","), ...rows.map(r => r.join(","))].join("\n");
}

function getName(path: string) {
    if (!path) return "";
    const parts = path.split("/").filter(Boolean);
    return parts.length ? parts[parts.length - 1] : path;
}

function simplifyMessage(message: string | null) {
    if (!message) return "";
    const firstSentence = message.split(".")[0];
    return firstSentence.trim().length ? firstSentence.trim() : message;
}

// lightweight shared types/enums to avoid importing server-only modules in client bundle
type HealthCheckHistoryResponse = {
    items: HealthCheckResult[],
    stats: unknown,
    hasMore?: boolean,
    page?: number,
};

type HealthCheckResult = {
    id: string,
    createdAt: string,
    path: string,
    repairStatus: RepairAction,
    message: string | null
};

enum RepairAction {
    None = 0,
    Repaired = 1,
    Deleted = 2,
    ActionNeeded = 3,
}
