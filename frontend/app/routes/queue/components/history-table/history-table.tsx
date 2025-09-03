import type { HistorySlot } from "~/clients/backend-client.server"
import { CategoryBadge, formatFileSize } from "../queue-table/queue-table"
import { ActionButton } from "../action-button/action-button"
import { PageTable } from "../page-table/page-table"
import tableStyles from "../page-table/page-table.module.css"
import { Truncate } from "../truncate/truncate"
import { useCallback, useState } from "react"
import { ConfirmModal } from "../confirm-modal/confirm-modal"
import { Link } from "react-router"
import { StatusBadge } from "../status-badge/status-badge"

export type HistoryTableProps = {
    historySlots: HistorySlot[]
}

export function HistoryTable({ historySlots }: HistoryTableProps) {
    return (
        <PageTable responsive>
            <thead>
                <tr>
                    <th>Name</th>
                    <th className={tableStyles.disappear}>Category</th>
                    <th className={tableStyles.disappear}>Status</th>
                    <th className={tableStyles.disappear}>Size</th>
                    <th>Actions</th>
                </tr>
            </thead>
            <tbody>
                {historySlots.map(slot =>
                    <HistoryRow slot={slot} key={slot.nzo_id} />
                )}
            </tbody>
        </PageTable>
    );
}


type HistoryRowProps = {
    slot: HistorySlot
}

export function HistoryRow({ slot }: HistoryRowProps) {
    // state
    const [isConfirmingDelete, setIsConfirmingDelete] = useState(false);
    const [isDeleting, setIsDeleting] = useState(false);
    const [isDeleted, setIsDeleted] = useState(false);
    const className = isDeleting ? tableStyles.deleting : undefined;

    // events
    const onDelete = useCallback(() => {
        setIsConfirmingDelete(true);
    }, [setIsConfirmingDelete]);

    const onCancelDelete = useCallback(() => {
        setIsConfirmingDelete(false);
    }, [setIsConfirmingDelete]);

    const onConfirmDelete = useCallback(async (deleteCompletedFiles?: boolean) => {
        setIsConfirmingDelete(false);
        setIsDeleting(true);
        try {
            const url = '/api?mode=history&name=delete'
                + `&value=${encodeURIComponent(slot.nzo_id)}`
                + `&del_completed_files=${deleteCompletedFiles ? 1 : 0}`;
            const response = await fetch(url);
            if (response.ok) {
                const data = await response.json();
                if (data.status === true) {
                    setIsDeleted(true);
                    return;
                }
            }
        } catch { }
        setIsDeleting(false);
    }, [slot.nzo_id, setIsConfirmingDelete, setIsDeleting, setIsDeleted]);

    // view
    return isDeleted ? null : (
        <>
            <tr className={className}>
                <td>
                    <Truncate>{slot.nzb_name}</Truncate>
                    <div className={tableStyles.reappear}>
                        <StatusBadge status={slot.status} error={slot.fail_message} />
                        <CategoryBadge category={slot.category} />
                        <div>{formatFileSize(slot.bytes)}</div>
                    </div>
                </td>
                <td className={tableStyles.disappear}>
                    <CategoryBadge category={slot.category} />
                </td>
                <td className={tableStyles.disappear}>
                    <StatusBadge status={slot.status} error={slot.fail_message} />
                </td>
                <td className={tableStyles.disappear}>
                    {formatFileSize(slot.bytes)}
                </td>
                <td>
                    <Link to={`/explore/content/${slot.category}/${slot.name}`}>
                        <ActionButton type="explore" />
                    </Link>
                    <ActionButton type="delete" onClick={onDelete} />
                </td>
            </tr>
            <ConfirmModal
                show={isConfirmingDelete}
                title="Remove From History?"
                message={slot.nzb_name}
                checkboxMessage="Delete mounted files"
                onConfirm={onConfirmDelete}
                onCancel={onCancelDelete} />
        </>
    )
}
