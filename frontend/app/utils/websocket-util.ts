import { useEffect, useRef } from "react";
import { useNavigate } from "react-router";

export function receiveMessage(
    onMessage: (topic: string, message: string) => void
): (event: MessageEvent) => void {
    return (event) => {
        var parsed = JSON.parse(event.data);
        onMessage(parsed.Topic, parsed.Message);
    }
}

export function useWebsocketTopic(
    topic: string,
    onMessage: (message: string) => void,
    onDisconnect?: () => void,
) {
    const navigate = useNavigate();
    const onMessageRef = useRef(onMessage);
    const onDisconnectRef = useRef(onDisconnect);
    onMessageRef.current = onMessage;
    onDisconnectRef.current = onDisconnect;

    useEffect(() => {
        let ws: WebSocket;
        let disposed = false;
        let reconnectTimer: ReturnType<typeof setTimeout> | undefined;
        function connect() {
            if (disposed) return;
            ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
            ws.onmessage = receiveMessage((messageTopic, message) => {
                if (messageTopic === topic) onMessageRef.current(message);
            });
            ws.onopen = () => ws.send(JSON.stringify({ [topic]: 'state' }));
            ws.onerror = () => { ws.close() };
            ws.onclose = onClose;
        }
        function onClose(e: CloseEvent) {
            if (e.code == 1008) navigate('/login');
            if (!disposed) reconnectTimer = setTimeout(connect, 1000);
            onDisconnectRef.current?.();
        }
        connect();
        return () => {
            disposed = true;
            clearTimeout(reconnectTimer);
            ws.close();
        };
    }, [topic]);
}
