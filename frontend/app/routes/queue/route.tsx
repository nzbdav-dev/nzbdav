import { Link } from "react-router";
import type { Route } from "./+types/route";
import styles from "./route.module.css"
import { Alert } from 'react-bootstrap';
import { backendClient, type HistorySlot, type QueueSlot } from "~/clients/backend-client.server";
import { HistoryTable } from "./components/history-table/history-table";
import { QueueTable } from "./components/queue-table/queue-table";
import { useCallback, useEffect, useState, useRef } from "react";
import { receiveMessage } from "~/utils/websocket-util";
import { useDropzone } from 'react-dropzone';

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

const maxItems = 100;
export async function loader({ request }: Route.LoaderArgs) {
    const queuePromise = backendClient.getQueue(maxItems);
    const historyPromise = backendClient.getHistory(maxItems);
    const configPromise = backendClient.getConfig(["api.categories", "api.manual-category"])
    const queue = await queuePromise;
    const history = await historyPromise;
    const config = await configPromise;
    const categoriesValue = config
        .find(x => x.configName === "api.categories")
        ?.configValue ?? "uncategorized,audio,software,tv,movies";
    const manualCategory = config
        .find(x => x.configName === "api.manual-category")
        ?.configValue ?? "uncategorized";
    let categories = categoriesValue.split(',').map(x => x.trim());
    if (!categories.includes(manualCategory)) {
        categories = [manualCategory, ...categories];
    }

    return {
        queueSlots: queue?.slots || [],
        historySlots: history?.slots || [],
        totalQueueCount: queue?.noofslots || 0,
        totalHistoryCount: history?.noofslots || 0,
        categories: categories,
        manualCategory: manualCategory,
    }
}

export default function Queue(props: Route.ComponentProps) {
    const [queueSlots, setQueueSlots] = useState<PresentationQueueSlot[]>(props.loaderData.queueSlots);
    const [historySlots, setHistorySlots] = useState<PresentationHistorySlot[]>(props.loaderData.historySlots);
    const [uploadingFiles, setUploadingFiles] = useState<UploadingFile[]>([]);
    const [manualCategory, setManualCategory] = useState<string>(props.loaderData.manualCategory);
    const uploadQueueRef = useRef<UploadingFile[]>([]);
    const manualCategoryRef = useRef<string>(manualCategory);
    const isUploadingRef = useRef(false);
    const disableLiveView = queueSlots.length == maxItems || historySlots.length == maxItems;
    const combinedQueueSlots = [...uploadingFiles.map(file => file.queueSlot), ...queueSlots];

    // category events
    const onManualCategoryChanged = useCallback((category: string) => {
        setManualCategory(category);
        manualCategoryRef.current = category;
    }, []);

    // queue events
    const onAddQueueSlot = useCallback((queueSlot: QueueSlot) => {
        uploadQueueRef.current = uploadQueueRef.current.filter(x => x.queueSlot.status === "uploading" || x.queueSlot.filename !== queueSlot.filename);
        setUploadingFiles(files => files.filter(f => f.queueSlot.filename !== queueSlot.filename));
        setQueueSlots(slots => [...slots, queueSlot]);
    }, [setQueueSlots]);

    const onSelectQueueSlots = useCallback((ids: Set<string>, isSelected: boolean) => {
        setUploadingFiles(files => files.map(x => ids.has(x.queueSlot.nzo_id) ? { ...x, queueSlot: { ...x.queueSlot, isSelected } } : x));
        setQueueSlots(slots => slots.map(x => ids.has(x.nzo_id) ? { ...x, isSelected } : x));
    }, [setQueueSlots]);

    const onRemovingQueueSlots = useCallback((ids: Set<string>, isRemoving: boolean) => {
        setQueueSlots(slots => slots.map(x => ids.has(x.nzo_id) ? { ...x, isRemoving } : x));
    }, [setQueueSlots]);

    const onRemoveQueueSlots = useCallback((ids: Set<string>) => {
        uploadQueueRef.current = uploadQueueRef.current.filter(x => x.queueSlot.status === "uploading" || !ids.has(x.queueSlot.nzo_id));
        setUploadingFiles(files => files.filter(x => x.queueSlot.status === "uploading" || !ids.has(x.queueSlot.nzo_id)));
        setQueueSlots(slots => slots.filter(x => !ids.has(x.nzo_id)));
    }, [setQueueSlots]);

    const onChangeQueueSlotStatus = useCallback((message: string) => {
        const [nzo_id, status] = message.split('|');
        setQueueSlots(slots => slots.map(x => x.nzo_id === nzo_id ? { ...x, status } : x));
    }, [setQueueSlots]);

    const onChangeQueueSlotPercentage = useCallback((message: string) => {
        const [nzo_id, true_percentage] = message.split('|');
        setQueueSlots(slots => slots.map(x => x.nzo_id === nzo_id ? { ...x, true_percentage } : x));
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

    // upload processing
    const processUploadQueue = useCallback(async () => {
        if (isUploadingRef.current || uploadQueueRef.current.length === 0) return;

        isUploadingRef.current = true;
        const fileToUpload = uploadQueueRef.current[0];

        setUploadingFiles(files => files.map(f =>
            f.queueSlot.nzo_id === fileToUpload.queueSlot.nzo_id
                ? { ...f, queueSlot: { ...f.queueSlot, status: 'uploading' } }
                : f
        ));

        try {
            const xhr = new XMLHttpRequest();
            const formData = new FormData();
            formData.append('nzbFile', fileToUpload.file, fileToUpload.file.name);

            xhr.responseType = 'json';
            xhr.upload.addEventListener('progress', (e) => {
                if (e.lengthComputable) {
                    const progress = Math.round((e.loaded / e.total) * 100);
                    setUploadingFiles(files => files.map(f =>
                        f.queueSlot.nzo_id === fileToUpload.queueSlot.nzo_id
                            ? {
                                ...f,
                                queueSlot: {
                                    ...f.queueSlot,
                                    percentage: progress.toString(),
                                    true_percentage: progress.toString()
                                }
                            }
                            : f
                    ));
                }
            });

            var response: any = await new Promise<void>((resolve, reject) => {
                xhr.addEventListener('load', () => {
                    if (xhr.status >= 200 && xhr.status < 300) {
                        resolve(xhr.response);
                    } else {
                        const errorMessage = xhr.response.error || `Upload failed with status ${xhr.status}`;
                        reject(new Error(errorMessage));
                    }
                });
                xhr.addEventListener('error', () => reject(new Error('Upload failed')));
                xhr.addEventListener('abort', () => reject(new Error('Upload aborted')));

                xhr.open('POST', `/api?mode=addfile&cat=${manualCategoryRef.current}&priority=0&pp=0`);
                xhr.send(formData);
            });

            if (response.status == false) {
                throw new Error(response.error);
            }

        } catch (error) {
            setUploadingFiles(files => files.map(f =>
                f.queueSlot.nzo_id === fileToUpload.queueSlot.nzo_id ? {
                    ...f,
                    queueSlot: {
                        ...f.queueSlot,
                        status: 'upload failed',
                        error: error instanceof Error ? error.message : 'Upload failed'
                    }
                } : f
            ));
        }

        uploadQueueRef.current = uploadQueueRef.current.filter(x => x !== fileToUpload);
        isUploadingRef.current = false;

        if (uploadQueueRef.current.length > 0) {
            processUploadQueue();
        }
    }, []);

    // trigger upload processing when files are added
    useEffect(() => {
        processUploadQueue();
    }, [uploadingFiles, processUploadQueue]);

    // dropzone
    const onDrop = useCallback((acceptedFiles: File[]) => {
        const newFiles: UploadingFile[] = acceptedFiles.map(file => ({
            file,
            queueSlot: {
                isUploading: true,
                nzo_id: `upload-${Date.now()}-${Math.random()}`,
                priority: 'Normal',
                filename: file.name,
                cat: manualCategoryRef.current,
                percentage: "0",
                true_percentage: "0",
                status: "pending",
                mb: (file.size / (1024 * 1024)).toFixed(2),
                mbleft: (file.size / (1024 * 1024)).toFixed(2),
            }
        }));

        setUploadingFiles(files => [...files, ...newFiles]);
        uploadQueueRef.current = [...uploadQueueRef.current, ...newFiles];
    }, []);

    const dropzone = useDropzone({
        accept: { 'application/x-nzb': ['.nzb'] },
        onDrop,
        noClick: true,
        noKeyboard: true,
    });

    // view
    return (
        <div className={styles.container}>

            {/* warning */}
            {disableLiveView &&
                <Alert className={styles.alert} variant="warning">
                    <b>Attention</b>
                    <ul className={styles.list}>
                        <li className={styles.listItem}>
                            Displaying the first {queueSlots.length} of {props.loaderData.totalQueueCount} queue items
                        </li>
                        <li className={styles.listItem}>
                            Displaying the first {historySlots.length} of {props.loaderData.totalHistoryCount} history items
                        </li>
                        <li className={styles.listItem}>
                            Live view is disabled. Manually <Link to={'/queue'}>refresh</Link> the page for updates.
                        </li>
                        <li className={styles.listItem}>
                            (This is a bandaid — Proper pagination will be added soon)
                        </li>
                    </ul>
                </Alert>
            }

            {/* queue */}
            <div className={styles.dropzone} {...dropzone.getRootProps()}>
                {dropzone.isDragActive && <div className={styles.activeDropzone} />}
                <input {...dropzone.getInputProps()} />
                <QueueTable
                    queueSlots={combinedQueueSlots}
                    totalQueueCount={props.loaderData.totalQueueCount + uploadingFiles.length}
                    categories={props.loaderData.categories}
                    manualCategory={manualCategory}
                    onIsSelectedChanged={onSelectQueueSlots}
                    onIsRemovingChanged={onRemovingQueueSlots}
                    onRemoved={onRemoveQueueSlots}
                    onManualCategoryChanged={onManualCategoryChanged}
                />
            </div>

            {/* history */}
            {historySlots.length > 0 &&
                <HistoryTable
                    historySlots={historySlots}
                    totalHistoryCount={props.loaderData.totalHistoryCount}
                    onIsSelectedChanged={onSelectHistorySlots}
                    onIsRemovingChanged={onRemovingHistorySlots}
                    onRemoved={onRemoveHistorySlots}
                />
            }
        </div >
    );
}

export type PresentationHistorySlot = HistorySlot & {
    isSelected?: boolean,
    isRemoving?: boolean,
}

export type PresentationQueueSlot = QueueSlot & {
    isUploading?: boolean,
    isSelected?: boolean,
    isRemoving?: boolean,
    error?: string,
}

export type UploadingFile = {
    file: File,
    queueSlot: PresentationQueueSlot,
}