import type { QueueSlot } from "~/clients/backend-client.server"
import tableStyles from "../page-table/page-table.module.css"
import { Badge } from "react-bootstrap"
import { ActionButton } from "../action-button/action-button"
import { PageTable } from "../page-table/page-table"
import { Truncate } from "../truncate/truncate"
import { useCallback, useState } from "react"
import { ConfirmModal } from "../confirm-modal/confirm-modal"
import { StatusBadge } from "../status-badge/status-badge"

export type QueueTableProps = {
    queueSlots: QueueSlot[]
}

export function QueueTable({ queueSlots }: QueueTableProps) {
    return (
        <PageTable responsive striped>
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
                {queueSlots.map(slot =>
                    <QueueRow slot={slot} key={slot.nzo_id} />
                )}
            </tbody>
        </PageTable>
    );
}

type QueueRowProps = {
    slot: QueueSlot
}

export function QueueRow({ slot }: QueueRowProps) {
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

    const onConfirmDelete = useCallback(async () => {
        setIsConfirmingDelete(false);
        setIsDeleting(true);
        try {
            const url = '/api?mode=queue&name=delete'
                + `&value=${encodeURIComponent(slot.nzo_id)}`;
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
            <tr key={slot.nzo_id} className={className}>
                <td>
                    <Truncate>{slot.filename}</Truncate>
                    <div className={tableStyles.reappear}>
                        <StatusBadge status={slot.status} percentage={slot.percentage} />
                        <CategoryBadge category={slot.cat} />
                        <div>{formatFileSize(Number(slot.mb) * 1024 * 1024)}</div>
                    </div>
                </td>
                <td className={tableStyles.disappear}>
                    <CategoryBadge category={slot.cat} />
                </td>
                <td className={tableStyles.disappear}>
                    <StatusBadge status={slot.status} percentage={slot.percentage} />
                </td>
                <td className={tableStyles.disappear}>
                    {formatFileSize(Number(slot.mb) * 1024 * 1024)}
                </td>
                <td>
                    <ActionButton type="delete" disabled={isDeleting} onClick={onDelete} />
                </td>
            </tr>
            <ConfirmModal
                show={isConfirmingDelete}
                title="Remove From Queue?"
                message={slot.filename}
                onConfirm={onConfirmDelete}
                onCancel={onCancelDelete} />
        </>
    )
}

export function CategoryBadge({ category }: { category: string }) {
    const categoryLower = category?.toLowerCase();
    let variant = 'secondary';
    if (categoryLower === 'movies') variant = 'primary';
    if (categoryLower === 'tv') variant = 'info';
    return <Badge bg={variant} style={{ width: '85px' }}>{categoryLower}</Badge>
}


export function formatFileSize(bytes: number) {
    var suffix = "B";
    if (bytes >= 1024) { bytes /= 1024; suffix = "KB"; }
    if (bytes >= 1024) { bytes /= 1024; suffix = "MB"; }
    if (bytes >= 1024) { bytes /= 1024; suffix = "GB"; }
    if (bytes >= 1024) { bytes /= 1024; suffix = "TB"; }
    if (bytes >= 1024) { bytes /= 1024; suffix = "PB"; }
    return `${bytes.toFixed(2)} ${suffix}`;
}