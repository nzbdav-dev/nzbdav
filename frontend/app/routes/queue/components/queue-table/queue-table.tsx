import { ActionButton } from "../action-button/action-button"
import { memo, useCallback, useMemo, useState } from "react"
import { ConfirmModal } from "~/components/confirm-modal/confirm-modal"
import type { PresentationQueueSlot } from "../../route"
import type { TriCheckboxState } from "../tri-checkbox/tri-checkbox"
import { PageRow, PageTable } from "../page-table/page-table"
import { PageSection } from "../page-section/page-section"
import { EmptyQueue } from "../empty-queue/empty-queue"
import { SimpleDropdown } from "../simple-dropdown/simple-dropdown"
import styles from "../../route.module.css"
import { WideViewport } from "../wide-viewport/wide-viewport"
import { ThinViewport } from "../thin-viewport/thin-viewport"

export type QueueTableProps = {
    queueSlots: PresentationQueueSlot[],
    totalQueueCount: number,
    categories: string[],
    manualCategoryRef: React.RefObject<string>,
    onIsSelectedChanged: (nzo_ids: Set<string>, isSelected: boolean) => void,
    onIsRemovingChanged: (nzo_ids: Set<string>, isRemoving: boolean) => void,
    onRemoved: (nzo_ids: Set<string>) => void,
    onUploadClicked?: () => void;
}

export function QueueTable({
    queueSlots,
    totalQueueCount,
    categories,
    manualCategoryRef,
    onIsSelectedChanged,
    onIsRemovingChanged,
    onRemoved,
    onUploadClicked,
}: QueueTableProps) {
    const [isConfirmingRemoval, setIsConfirmingRemoval] = useState(false);
    var selectedCount = queueSlots.filter(x => !!x.isSelected).length;
    var headerCheckboxState: TriCheckboxState = selectedCount === 0 ? 'none' : selectedCount === queueSlots.length ? 'all' : 'some';

    // row events
    const onRowIsSelectedChanged = useCallback((id: string, isSelected: boolean) => {
        onIsSelectedChanged(new Set<string>([id]), isSelected);
    }, [onIsSelectedChanged]);

    const onRowIsRemovingChanged = useCallback((id: string, isRemoving: boolean) => {
        onIsRemovingChanged(new Set<string>([id]), isRemoving);
    }, [onIsSelectedChanged]);

    const onRowRemoved = useCallback((id: string) => {
        onRemoved(new Set([id]));
    }, [onRemoved]);

    // table events
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


    // view
    const categoryDropdown = useMemo(() => (
        <div title="Choose the category for manual nzb uploads.">
            <SimpleDropdown options={categories} valueRef={manualCategoryRef} />
        </div>
    ), [categories]);

    const sectionTitle = (
        <div className={styles.sectionTitle}>
            <h3 onClick={onUploadClicked} style={{ cursor: 'pointer' }}>
                Queue
            </h3>
            {headerCheckboxState !== 'none' &&
                <ActionButton type="delete" onClick={onRemove} />
            }
            <WideViewport width="450px">
                <div style={{ marginLeft: '10px' }}>
                    {categoryDropdown}
                </div>
            </WideViewport>
        </div>
    );

    const sectionSubTitle = (
        <ThinViewport width="450px">
            {categoryDropdown}
        </ThinViewport>
    );

    return (
        <PageSection title={sectionTitle} subTitle={sectionSubTitle}>
            {queueSlots?.length == 0 ? (
                <EmptyQueue onUploadClicked={onUploadClicked} />
            ) : (
                <PageTable headerCheckboxState={headerCheckboxState} onHeaderCheckboxChange={onSelectAll}>
                    {queueSlots.map(slot =>
                        <QueueRow
                            key={slot.nzo_id}
                            slot={slot}
                            onIsSelectedChanged={onRowIsSelectedChanged}
                            onIsRemovingChanged={onRowIsRemovingChanged}
                            onRemoved={onRowRemoved}
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

export const QueueRow = memo(({ slot, onIsSelectedChanged, onIsRemovingChanged, onRemoved }: QueueRowProps) => {
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
});