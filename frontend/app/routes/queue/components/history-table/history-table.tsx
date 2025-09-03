import pageStyles from "../../route.module.css"
import { ActionButton } from "../action-button/action-button"
import { PageRow, PageTable } from "../page-table/page-table"
import { useCallback, useState } from "react"
import { ConfirmModal } from "../confirm-modal/confirm-modal"
import { Link } from "react-router"
import { type TriCheckboxState } from "../tri-checkbox/tri-checkbox"
import type { PresentationHistorySlot } from "../../route"

export type HistoryTableProps = {
    historySlots: PresentationHistorySlot[],
    onIsSelectedChanged: (nzo_ids: Set<string>, isSelected: boolean) => void,
    onIsRemovingChanged: (nzo_ids: Set<string>, isRemoving: boolean) => void,
    onRemoved: (nzo_ids: Set<string>) => void,
}

export function HistoryTable({ historySlots, onIsSelectedChanged, onIsRemovingChanged, onRemoved }: HistoryTableProps) {
    const [isConfirmingRemoval, setIsConfirmingRemoval] = useState(false);
    var selectedCount = historySlots.filter(x => !!x.isSelected).length;
    var headerCheckboxState: TriCheckboxState = selectedCount === 0 ? 'none' : selectedCount === historySlots.length ? 'all' : 'some';

    const onSelectAll = useCallback((isSelected: boolean) => {
        onIsSelectedChanged(new Set<string>(historySlots.map(x => x.nzo_id)), isSelected);
    }, [historySlots, onIsSelectedChanged]);

    const onRemove = useCallback(() => {
        setIsConfirmingRemoval(true);
    }, [setIsConfirmingRemoval]);

    const onCancelRemoval = useCallback(() => {
        setIsConfirmingRemoval(false);
    }, [setIsConfirmingRemoval]);

    const onConfirmRemoval = useCallback(async (deleteCompletedFiles?: boolean) => {
        var nzo_ids = new Set<string>(historySlots.filter(x => !!x.isSelected).map(x => x.nzo_id));
        setIsConfirmingRemoval(false);
        onIsRemovingChanged(nzo_ids, true);
        try {
            const url = `/api?mode=history&name=delete&del_completed_files=${deleteCompletedFiles ? 1 : 0}`;
            const response = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json;charset=UTF-8',
                },
                body: JSON.stringify({ nzo_ids: Array.from(nzo_ids) }),
            });
            if (response.ok) {
                const data = await response.json();
                if (data.status === true) {
                    onRemoved(nzo_ids);
                    return;
                }
            }
        } catch { }
        onIsRemovingChanged(nzo_ids, false);
    }, [historySlots, setIsConfirmingRemoval, onIsRemovingChanged, onRemoved]);

    return (
        <>
            <div className={pageStyles["section-title"]}>
                <h3>History</h3>
                {headerCheckboxState !== 'none' &&
                    <ActionButton type="delete" onClick={onRemove} />
                }
            </div>
            <PageTable headerCheckboxState={headerCheckboxState} onHeaderCheckboxChange={onSelectAll}>
                {historySlots.map(slot =>
                    <HistoryRow
                        key={slot.nzo_id}
                        slot={slot}
                        onIsSelectedChanged={(id, isSelected) => onIsSelectedChanged(new Set<string>([id]), isSelected)}
                        onIsRemovingChanged={(id, isRemoving) => onIsRemovingChanged(new Set<string>([id]), isRemoving)}
                        onRemoved={(id) => onRemoved(new Set([id]))}
                    />
                )}
            </PageTable>

            <ConfirmModal
                show={isConfirmingRemoval}
                title="Remove From History?"
                message={`${selectedCount} item(s) will be removed`}
                checkboxMessage="Delete mounted files"
                onConfirm={onConfirmRemoval}
                onCancel={onCancelRemoval} />
        </>
    );
}


type HistoryRowProps = {
    slot: PresentationHistorySlot,
    onIsSelectedChanged: (nzo_id: string, isSelected: boolean) => void,
    onIsRemovingChanged: (nzo_id: string, isRemoving: boolean) => void,
    onRemoved: (nzo_id: string) => void
}

export function HistoryRow({ slot, onIsSelectedChanged, onIsRemovingChanged, onRemoved }: HistoryRowProps) {
    // state
    const [isConfirmingRemoval, setIsConfirmingRemoval] = useState(false);

    // events
    const onRemove = useCallback(() => {
        setIsConfirmingRemoval(true);
    }, [setIsConfirmingRemoval]);

    const onCancelRemoval = useCallback(() => {
        setIsConfirmingRemoval(false);
    }, [setIsConfirmingRemoval]);

    const onConfirmRemoval = useCallback(async (deleteCompletedFiles?: boolean) => {
        setIsConfirmingRemoval(false);
        onIsRemovingChanged(slot.nzo_id, true);
        try {
            const url = '/api?mode=history&name=delete'
                + `&value=${encodeURIComponent(slot.nzo_id)}`
                + `&del_completed_files=${deleteCompletedFiles ? 1 : 0}`;
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
                isSelected={!!slot.isSelected}
                isRemoving={!!slot.isRemoving}
                name={slot.name}
                category={slot.category}
                status={slot.status}
                error={slot.fail_message}
                fileSizeBytes={slot.bytes}
                actions={
                    <>
                        <Link to={`/explore/content/${slot.category}/${slot.name}`}>
                            <ActionButton type="explore" disabled={!!slot.isRemoving} />
                        </Link>
                        <ActionButton type="delete" disabled={!!slot.isRemoving} onClick={onRemove} />
                    </>
                }
                onRowSelectionChanged={isSelected => onIsSelectedChanged(slot.nzo_id, isSelected)}
            />
            <ConfirmModal
                show={isConfirmingRemoval}
                title="Remove From History?"
                message={slot.nzb_name}
                checkboxMessage="Delete mounted files"
                onConfirm={onConfirmRemoval}
                onCancel={onCancelRemoval} />
        </>
    )
}
