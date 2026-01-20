import { ActionButton } from "../action-button/action-button"
import { useCallback, useState } from "react"
import { ConfirmModal } from "../confirm-modal/confirm-modal"
import type { PresentationQueueSlot } from "../../route"
import type { TriCheckboxState } from "../tri-checkbox/tri-checkbox"
import { PageRow, PageTable } from "../page-table/page-table"
import { PageSection } from "../page-section/page-section"
import { EmptyQueue } from "../empty-queue/empty-queue"
import styles from "../../route.module.css"

export type QueueTableProps = {
    queueSlots: PresentationQueueSlot[],
    totalQueueCount: number,
    onIsSelectedChanged: (nzo_ids: Set<string>, isSelected: boolean) => void,
    onIsRemovingChanged: (nzo_ids: Set<string>, isRemoving: boolean) => void,
    onRemoved: (nzo_ids: Set<string>) => void,
}

export function QueueTable({ queueSlots, totalQueueCount, onIsSelectedChanged, onIsRemovingChanged, onRemoved }: QueueTableProps) {
    const [isConfirmingRemoval, setIsConfirmingRemoval] = useState(false);
    var selectedCount = queueSlots.filter(x => !!x.isSelected).length;
    var headerCheckboxState: TriCheckboxState = selectedCount === 0 ? 'none' : selectedCount === queueSlots.length ? 'all' : 'some';

    const onSelectAll = useCallback((isSelected: boolean) => {
        onIsSelectedChanged(new Set<string>(queueSlots.map(x => x.nzo_id)), isSelected);
    }, [queueSlots, onIsSelectedChanged]);

    const onRemove = useCallback(() => {
        setIsConfirmingRemoval(true);
    }, [setIsConfirmingRemoval]);

    const onCancelRemoval = useCallback(() => {
        setIsConfirmingRemoval(false);
    }, [setIsConfirmingRemoval]);

    const onConfirmRemoval = useCallback(async () => {
        // immediately remove uploading items
        const uploading_nzo_ids = new Set<string>(queueSlots.filter(x => x.isUploading && !!x.isSelected).map(x => x.nzo_id));
        onRemoved(uploading_nzo_ids);

        // call backend to remove queued items
        const queued_nzo_ids = new Set<string>(queueSlots.filter(x => !x.isUploading && !!x.isSelected).map(x => x.nzo_id));
        setIsConfirmingRemoval(false);
        onIsRemovingChanged(queued_nzo_ids, true);
        try {
            const url = `/api?mode=queue&name=delete`;
            const response = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json;charset=UTF-8',
                },
                body: JSON.stringify({ nzo_ids: Array.from(queued_nzo_ids) }),
            });
            if (response.ok) {
                const data = await response.json();
                if (data.status === true) {
                    onRemoved(queued_nzo_ids);
                    return;
                }
            }
        } catch { }
        onIsRemovingChanged(queued_nzo_ids, false);
    }, [queueSlots, setIsConfirmingRemoval, onIsRemovingChanged, onRemoved]);

    var sectionTitle = (
        <div className={styles.sectionTitle}>
            <h3>Queue</h3>
            {headerCheckboxState !== 'none' &&
                <ActionButton type="delete" onClick={onRemove} />
            }
        </div>
    );

    return (
        <PageSection title={sectionTitle} badgeText={`${queueSlots.length} of ${totalQueueCount}`}>
            {queueSlots?.length == 0 ? (
                <EmptyQueue />
            ) : (
                <PageTable headerCheckboxState={headerCheckboxState} onHeaderCheckboxChange={onSelectAll}>
                    {queueSlots.map(slot =>
                        <QueueRow
                            key={slot.nzo_id}
                            slot={slot}
                            onIsSelectedChanged={(id, isSelected) => onIsSelectedChanged(new Set<string>([id]), isSelected)}
                            onIsRemovingChanged={(id, isRemoving) => onIsRemovingChanged(new Set<string>([id]), isRemoving)}
                            onRemoved={(id) => onRemoved(new Set([id]))}
                        />
                    )}
                </PageTable>
            )}

            <ConfirmModal
                show={isConfirmingRemoval}
                title="Remove From Queue?"
                message={`${selectedCount} item(s) will be removed`}
                onConfirm={onConfirmRemoval}
                onCancel={onCancelRemoval} />
        </PageSection>
    );
}

type QueueRowProps = {
    slot: PresentationQueueSlot
    onIsSelectedChanged: (nzo_id: string, isSelected: boolean) => void,
    onIsRemovingChanged: (nzo_id: string, isRemoving: boolean) => void,
    onRemoved: (nzo_id: string) => void
}

export function QueueRow({ slot, onIsSelectedChanged, onIsRemovingChanged, onRemoved }: QueueRowProps) {
    // state
    const [isConfirmingRemoval, setIsConfirmingRemoval] = useState(false);
    const isActivelyUploading = slot.isUploading && slot.status == "uploading";

    // events
    const onRemove = useCallback(() => {
        // immediately remove uploading items, without need of confirmation.
        if (slot.isUploading) {
            onRemoved(slot.nzo_id);
            return;
        }

        setIsConfirmingRemoval(true);
    }, [setIsConfirmingRemoval]);

    const onCancelRemoval = useCallback(() => {
        setIsConfirmingRemoval(false);
    }, [setIsConfirmingRemoval]);

    const onConfirmRemoval = useCallback(async () => {
        if (slot.isUploading) return;
        setIsConfirmingRemoval(false);
        onIsRemovingChanged(slot.nzo_id, true);
        try {
            const url = '/api?mode=queue&name=delete'
                + `&value=${encodeURIComponent(slot.nzo_id)}`;
            const response = await fetch(url);
            if (response.ok) {
                const data = await response.json();
                if (data.status === true) {
                    onRemoved(slot.nzo_id);
                    return;
                }
            }
        } catch { }
        onIsRemovingChanged(slot.nzo_id, false);
    }, [slot.nzo_id, setIsConfirmingRemoval, onIsRemovingChanged, onRemoved]);

    // view
    return (
        <>
            <PageRow
                isUploading={!!slot.isUploading}
                isSelected={!!slot.isSelected}
                isRemoving={!!slot.isRemoving}
                name={slot.filename}
                category={slot.cat}
                status={slot.status}
                percentage={slot.true_percentage}
                fileSizeBytes={Number(slot.mb) * 1024 * 1024}
                actions={<ActionButton type="delete" disabled={!!slot.isRemoving || isActivelyUploading} onClick={onRemove} />}
                onRowSelectionChanged={isSelected => onIsSelectedChanged(slot.nzo_id, isSelected)}
                error={slot.error}
            />
            <ConfirmModal
                show={isConfirmingRemoval}
                title="Remove From Queue?"
                message={slot.filename}
                onConfirm={onConfirmRemoval}
                onCancel={onCancelRemoval} />
        </>
    )
}