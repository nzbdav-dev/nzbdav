import { useCallback, useEffect } from "react";
import type { HistoryEvents, QueueEvents } from "./events-controller";
import { receiveMessage } from "~/utils/websocket-util";

const topicNames = {
    queueItemStatus: 'qs',
    queueItemPercentage: 'qp',
    queueItemAdded: 'qa',
    queueItemRemoved: 'qr',
    historyItemAdded: 'ha',
    historyItemRemoved: 'hr',
};

const topicSubscriptions = {
    [topicNames.queueItemStatus]: 'state',
    [topicNames.queueItemPercentage]: 'state',
    [topicNames.queueItemAdded]: 'event',
    [topicNames.queueItemRemoved]: 'event',
    [topicNames.historyItemAdded]: 'event',
    [topicNames.historyItemRemoved]: 'event',
};

export function initializeQueueHistoryWebsocket(
    queueEvents: QueueEvents,
    historyEvents: HistoryEvents,
    disableLiveView: boolean,
) {
    const onWebsocketMessage = useCallback((topic: string, message: string) => {
        if (disableLiveView) return;
        if (topic == topicNames.queueItemAdded)
            queueEvents.onAddQueueSlot(JSON.parse(message));
        else if (topic == topicNames.queueItemRemoved)
            queueEvents.onRemoveQueueSlots(new Set<string>(message.split(',')));
        else if (topic == topicNames.queueItemStatus)
            queueEvents.onChangeQueueSlotStatus(message);
        else if (topic == topicNames.queueItemPercentage)
            queueEvents.onChangeQueueSlotPercentage(message);
        else if (topic == topicNames.historyItemAdded)
            historyEvents.onAddHistorySlot(JSON.parse(message));
        else if (topic == topicNames.historyItemRemoved)
            historyEvents.onRemoveHistorySlots(new Set<string>(message.split(',')));
    }, [
        queueEvents,
        historyEvents,
        disableLiveView
    ]);

    useEffect(() => {
        if (disableLiveView) return;
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
}