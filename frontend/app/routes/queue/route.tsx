import { redirect } from "react-router";
import type { Route } from "./+types/route";
import { sessionStorage } from "~/auth/authentication.server";
import styles from "./route.module.css"
import { Alert } from 'react-bootstrap';
import { backendClient, type HistorySlot, type QueueSlot } from "~/clients/backend-client.server";
import { EmptyQueue } from "./components/empty-queue/empty-queue";
import { HistoryTable } from "./components/history-table/history-table";
import { QueueTable } from "./components/queue-table/queue-table";
import { useCallback, useEffect, useState } from "react";
import { receiveMessage } from "~/utils/websocket-util";

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

export async function loader({ request }: Route.LoaderArgs) {
    var queuePromise = backendClient.getQueue();
    var historyPromise = backendClient.getHistory();
    var queue = await queuePromise;
    var history = await historyPromise;
    return {
        queueSlots: queue?.slots || [],
        historySlots: history?.slots || [],
    }
}

export default function Queue(props: Route.ComponentProps) {
    const [queueSlots, setQueueSlots] = useState<PresentationQueueSlot[]>(props.loaderData.queueSlots);
    const [historySlots, setHistorySlots] = useState<PresentationHistorySlot[]>(props.loaderData.historySlots);
    const error = props.actionData?.error;

    // queue events
    const onAddQueueSlot = useCallback((queueSlot: QueueSlot) => {
        setQueueSlots(slots => [...slots, queueSlot]);
    }, [setQueueSlots]);

    const onSelectQueueSlots = useCallback((ids: Set<string>, isSelected: boolean) => {
        setQueueSlots(slots => slots.map(x => ids.has(x.nzo_id) ? { ...x, isSelected } : x));
    }, [setQueueSlots]);

    const onRemovingQueueSlots = useCallback((ids: Set<string>, isRemoving: boolean) => {
        setQueueSlots(slots => slots.map(x => ids.has(x.nzo_id) ? { ...x, isRemoving } : x));
    }, [setQueueSlots]);

    const onRemoveQueueSlots = useCallback((ids: Set<string>) => {
        setQueueSlots(slots => slots.filter(x => !ids.has(x.nzo_id)));
    }, [setQueueSlots]);

    const onChangeQueueSlotStatus = useCallback((message: string) => {
        const [nzo_id, status] = message.split('|');
        setQueueSlots(slots => slots.map(x => x.nzo_id === nzo_id ? { ...x, status } : x));
    }, [setQueueSlots]);

    const onChangeQueueSlotPercentage = useCallback((message: string) => {
        const [nzo_id, percentage] = message.split('|');
        setQueueSlots(slots => slots.map(x => x.nzo_id === nzo_id ? { ...x, percentage } : x));
    }, [setQueueSlots]);

    // history events
    const onAddHistorySlot = useCallback((historySlot: HistorySlot) => {
        setHistorySlots(slots => [historySlot, ...slots]);
    }, [setHistorySlots]);

    const onSelectHistorySlots = useCallback((ids: Set<string>, isSelected: boolean) => {
        setHistorySlots(slots => slots.map(x => ids.has(x.nzo_id) ? { ...x, isSelected } : x));
    }, [setHistorySlots]);

    const onRemovingHistorySlots = useCallback((ids: Set<string>, isRemoving: boolean) => {
        setHistorySlots(slots => slots.map(x => ids.has(x.nzo_id) ? { ...x, isRemoving } : x));
    }, [setHistorySlots]);

    const onRemoveHistorySlots = useCallback((ids: Set<string>) => {
        setHistorySlots(slots => slots.filter(x => !ids.has(x.nzo_id)));
    }, [setHistorySlots]);

    // websocket
    const onWebsocketMessage = useCallback((topic: string, message: string) => {
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

    return (
        <div className={styles.container}>
            {/* error message */}
            {error &&
                <Alert variant="danger">
                    {error}
                </Alert>
            }

            {/* queue */}
            <div className={styles.section}>
                {queueSlots.length > 0 ?
                    <QueueTable queueSlots={queueSlots}
                        onIsSelectedChanged={onSelectQueueSlots}
                        onIsRemovingChanged={onRemovingQueueSlots}
                        onRemoved={onRemoveQueueSlots}
                    /> :
                    <EmptyQueue />}
            </div>

            {/* history */}
            {historySlots.length > 0 &&
                <div className={styles.section}>
                    <HistoryTable
                        historySlots={historySlots}
                        onIsSelectedChanged={onSelectHistorySlots}
                        onIsRemovingChanged={onRemovingHistorySlots}
                        onRemoved={onRemoveHistorySlots}
                    />
                </div>
            }
        </div>
    );
}

export async function action({ request }: Route.ActionArgs) {
    // ensure user is logged in
    let session = await sessionStorage.getSession(request.headers.get("cookie"));
    let user = session.get("user");
    if (!user) return redirect("/login");

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