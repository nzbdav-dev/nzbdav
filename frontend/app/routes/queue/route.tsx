import { redirect, useLocation, useNavigate } from "react-router";
import type { Route } from "./+types/route";
import styles from "./route.module.css"
import { Alert } from 'react-bootstrap';
import { backendClient, type HistorySlot, type QueueSlot } from "~/clients/backend-client.server";
import { EmptyQueue } from "./components/empty-queue/empty-queue";
import { HistoryTable } from "./components/history-table/history-table";
import { QueueTable } from "./components/queue-table/queue-table";
import { useCallback, useEffect, useState } from "react";
import { receiveMessage } from "~/utils/websocket-util";
import { isAuthenticated } from "~/auth/authentication.server";
import { Pagination } from "./components/pagination/pagination";

const topicNames = {
    queueItemStatus: 'qs',
    queueItemPercentage: 'qp',
    queueItemAdded: 'qa',
    queueItemRemoved: 'qr',
    historyItemAdded: 'ha',
    historyItemRemoved: 'hr',
}
const topicSubscriptions = {
    [topicNames.queueItemStatus]: 'state',
    [topicNames.queueItemPercentage]: 'state',
    [topicNames.queueItemAdded]: 'event',
    [topicNames.queueItemRemoved]: 'event',
    [topicNames.historyItemAdded]: 'event',
    [topicNames.historyItemRemoved]: 'event',
}

const defaultPageSize = 50;
export async function loader({ request }: Route.LoaderArgs) {
    const url = new URL(request.url);
    const queuePage = Number(url.searchParams.get("queuePage") || 1);
    const historyPage = Number(url.searchParams.get("historyPage") || 1);
    const queueSize = Number(url.searchParams.get("queueSize") || defaultPageSize);
    const historySize = Number(url.searchParams.get("historySize") || defaultPageSize);

    const queuePromise = backendClient.getQueue(queueSize, queuePage);
    const historyPromise = backendClient.getHistory(historySize, historyPage);
    const queue = await queuePromise;
    const history = await historyPromise;
    return {
        queueSlots: queue?.slots || [],
        historySlots: history?.slots || [],
        totalQueueCount: queue?.noofslots || 0,
        totalHistoryCount: history?.noofslots || 0,
        queuePage: queuePage,
        historyPage: historyPage,
        queueSize: queueSize,
        historySize: historySize,
    }
}

export default function Queue(props: Route.ComponentProps) {
    const navigate = useNavigate();
    const location = useLocation();
    const [queueSlots, setQueueSlots] = useState<PresentationQueueSlot[]>(props.loaderData.queueSlots);
    const [historySlots, setHistorySlots] = useState<PresentationHistorySlot[]>(props.loaderData.historySlots);
    const [queuePage, setQueuePage] = useState<number>(props.loaderData.queuePage);
    const [historyPage, setHistoryPage] = useState<number>(props.loaderData.historyPage);
    const [queueSize, setQueueSize] = useState<number>(props.loaderData.queueSize);
    const [historySize, setHistorySize] = useState<number>(props.loaderData.historySize);
    const [totalQueueCount, setTotalQueueCount] = useState<number>(props.loaderData.totalQueueCount);
    const [totalHistoryCount, setTotalHistoryCount] = useState<number>(props.loaderData.totalHistoryCount);
    const disableLiveView = queuePage !== 1 || historyPage !== 1;
    const error = props.actionData?.error;

    // keep state in sync with loader data when URL changes
    useEffect(() => {
        setQueueSlots(props.loaderData.queueSlots);
        setHistorySlots(props.loaderData.historySlots);
        setQueuePage(props.loaderData.queuePage);
        setHistoryPage(props.loaderData.historyPage);
        setQueueSize(props.loaderData.queueSize);
        setHistorySize(props.loaderData.historySize);
        setTotalQueueCount(props.loaderData.totalQueueCount);
        setTotalHistoryCount(props.loaderData.totalHistoryCount);
    }, [props.loaderData]);

    // queue events
    const onAddQueueSlot = useCallback((queueSlot: QueueSlot) => {
        setTotalQueueCount(x => x + 1);
        if (queuePage !== 1) return; // ignore live adds off page 1
        setQueueSlots(slots => [...slots, queueSlot]);
    }, [setQueueSlots, queuePage, setTotalQueueCount]);

    const onSelectQueueSlots = useCallback((ids: Set<string>, isSelected: boolean) => {
        setQueueSlots(slots => slots.map(x => ids.has(x.nzo_id) ? { ...x, isSelected } : x));
    }, [setQueueSlots]);

    const onRemovingQueueSlots = useCallback((ids: Set<string>, isRemoving: boolean) => {
        setQueueSlots(slots => slots.map(x => ids.has(x.nzo_id) ? { ...x, isRemoving } : x));
    }, [setQueueSlots]);

    const onRemoveQueueSlots = useCallback((ids: Set<string>) => {
        setTotalQueueCount(x => Math.max(0, x - ids.size));
        setQueueSlots(slots => slots.filter(x => !ids.has(x.nzo_id)));
    }, [setQueueSlots, setTotalQueueCount]);

    const onChangeQueueSlotStatus = useCallback((message: string) => {
        const [nzo_id, status] = message.split('|');
        if (queuePage !== 1) return;
        setQueueSlots(slots => slots.map(x => x.nzo_id === nzo_id ? { ...x, status } : x));
    }, [setQueueSlots, queuePage]);

    const onChangeQueueSlotPercentage = useCallback((message: string) => {
        const [nzo_id, true_percentage] = message.split('|');
        if (queuePage !== 1) return;
        setQueueSlots(slots => slots.map(x => x.nzo_id === nzo_id ? { ...x, true_percentage } : x));
    }, [setQueueSlots, queuePage]);

    // history events
    const onAddHistorySlot = useCallback((historySlot: HistorySlot) => {
        setTotalHistoryCount(x => x + 1);
        if (historyPage !== 1) return;
        setHistorySlots(slots => [historySlot, ...slots]);
    }, [setHistorySlots, historyPage, setTotalHistoryCount]);

    const onSelectHistorySlots = useCallback((ids: Set<string>, isSelected: boolean) => {
        setHistorySlots(slots => slots.map(x => ids.has(x.nzo_id) ? { ...x, isSelected } : x));
    }, [setHistorySlots]);

    const onRemovingHistorySlots = useCallback((ids: Set<string>, isRemoving: boolean) => {
        setHistorySlots(slots => slots.map(x => ids.has(x.nzo_id) ? { ...x, isRemoving } : x));
    }, [setHistorySlots]);

    const onRemoveHistorySlots = useCallback((ids: Set<string>) => {
        setTotalHistoryCount(x => Math.max(0, x - ids.size));
        if (historyPage !== 1) return;
        setHistorySlots(slots => slots.filter(x => !ids.has(x.nzo_id)));
    }, [setHistorySlots, historyPage, setTotalHistoryCount]);

    // websocket
    const onWebsocketMessage = useCallback((topic: string, message: string) => {
        if (disableLiveView) return;
        if (topic == topicNames.queueItemAdded)
            onAddQueueSlot(JSON.parse(message));
        else if (topic == topicNames.queueItemRemoved)
            onRemoveQueueSlots(new Set<string>(message.split(',')));
        else if (topic == topicNames.queueItemStatus)
            onChangeQueueSlotStatus(message);
        else if (topic == topicNames.queueItemPercentage)
            onChangeQueueSlotPercentage(message);
        else if (topic == topicNames.historyItemAdded)
            onAddHistorySlot(JSON.parse(message));
        else if (topic == topicNames.historyItemRemoved)
            onRemoveHistorySlots(new Set<string>(message.split(',')));
    }, [
        onAddQueueSlot,
        onRemoveQueueSlots,
        onChangeQueueSlotStatus,
        onChangeQueueSlotPercentage,
        onAddHistorySlot,
        onRemoveHistorySlots,
        disableLiveView
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
    }, [onWebsocketMessage, disableLiveView]);

    return (
        <div className={styles.container}>
            {/* error message */}
            {error &&
                <Alert variant="danger">
                    {error}
                </Alert>
            }

            {/* warning */}
            <Alert className={styles.alert} variant={'warning'}>
                <b>Attention</b>
                <ul className={styles.list}>
                    <li className={styles.listItem}>
                        Live updates apply only to page 1 for queue and history.
                    </li>
                </ul>
            </Alert>

            {/* queue */}
            <div className={styles.section}>
                {queueSlots.length > 0 ? (
                    <QueueTable
                        queueSlots={queueSlots}
                        onIsSelectedChanged={onSelectQueueSlots}
                        onIsRemovingChanged={onRemovingQueueSlots}
                        onRemoved={onRemoveQueueSlots}
                        pagination={
                            <Pagination
                                page={queuePage}
                                pageSize={queueSize}
                                total={totalQueueCount}
                                onPageChange={(p) => {
                                    setQueuePage(p);
                                    const params = new URLSearchParams(location.search);
                                    params.set("queuePage", String(p));
                                    params.set("queueSize", String(queueSize));
                                    navigate({ pathname: location.pathname, search: params.toString() }, { replace: true });
                                }}
                                onPageSizeChange={(size) => {
                                    setQueueSize(size);
                                    setQueuePage(1);
                                    const params = new URLSearchParams(location.search);
                                    params.set("queuePage", "1");
                                    params.set("queueSize", String(size));
                                    navigate({ pathname: location.pathname, search: params.toString() }, { replace: true });
                                }}
                            />
                        }
                    />
                ) : (
                    <>
                        <div className={styles["section-title"]}>
                            <h3>Queue</h3>
                        </div>
                        <EmptyQueue />
                    </>
                )}
            </div>

            {/* history */}
            {historySlots.length > 0 &&
                <div className={styles.section}>
                    <HistoryTable
                        historySlots={historySlots}
                        onIsSelectedChanged={onSelectHistorySlots}
                        onIsRemovingChanged={onRemovingHistorySlots}
                        onRemoved={onRemoveHistorySlots}
                        pagination={
                            <Pagination
                                page={historyPage}
                                pageSize={historySize}
                                total={totalHistoryCount}
                                onPageChange={(p) => {
                                    setHistoryPage(p);
                                    const params = new URLSearchParams(location.search);
                                    params.set("historyPage", String(p));
                                    params.set("historySize", String(historySize));
                                    navigate({ pathname: location.pathname, search: params.toString() }, { replace: true });
                                }}
                                onPageSizeChange={(size) => {
                                    setHistorySize(size);
                                    setHistoryPage(1);
                                    const params = new URLSearchParams(location.search);
                                    params.set("historyPage", "1");
                                    params.set("historySize", String(size));
                                    navigate({ pathname: location.pathname, search: params.toString() }, { replace: true });
                                }}
                            />
                        }
                    />
                </div>
            }
        </div>
    );
}

export async function action({ request }: Route.ActionArgs) {
    // ensure user is logged in
    if (!await isAuthenticated(request)) return redirect("/login");

    try {
        const formData = await request.formData();
        const nzbFile = formData.get("nzbFile");
        if (nzbFile instanceof File) {
            await backendClient.addNzb(nzbFile);
        } else {
            return { error: "Error uploading nzb." }
        }
    } catch (error) {
        if (error instanceof Error) {
            return { error: error.message };
        }
        throw error;
    }
}

export type PresentationHistorySlot = HistorySlot & {
    isSelected?: boolean,
    isRemoving?: boolean,
}

export type PresentationQueueSlot = QueueSlot & {
    isSelected?: boolean,
    isRemoving?: boolean,
}
