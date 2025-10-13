import { Badge, Table } from "react-bootstrap";
import styles from "./page-table.module.css";
import type { ReactNode } from "react";
import { TriCheckbox, type TriCheckboxState } from "../tri-checkbox/tri-checkbox";
import { Truncate } from "../truncate/truncate";
import { StatusBadge } from "../status-badge/status-badge";
import { formatFileSize } from "~/utils/file-size";

export type PageTableProps = {
    striped?: boolean,
    children?: ReactNode,
    headerCheckboxState: TriCheckboxState,
    onHeaderCheckboxChange: (isChecked: boolean) => void
}

export function PageTable({ striped, children, headerCheckboxState, onHeaderCheckboxChange: onTitleCheckboxChange }: PageTableProps) {
    return (
        <div className={styles.container}>
            <Table className={styles["page-table"]} responsive striped={striped}>
                <thead>
                    <tr>
                        <th>
                            <TriCheckbox state={headerCheckboxState} onChange={onTitleCheckboxChange}>
                                Name
                            </TriCheckbox>
                        </th>
                        <th className={styles.desktop}>Category</th>
                        <th className={styles.desktop}>Status</th>
                        <th className={styles.desktop}>Size</th>
                        <th>Actions</th>
                    </tr>
                </thead>
                <tbody>
                    {children}
                </tbody>
            </Table>
        </div>
    )
}

export type PageRowProps = {
    isSelected: boolean,
    isRemoving: boolean,
    name: string,
    category: string,
    status: string,
    percentage?: string,
    error?: string,
    fileSizeBytes: number,
    actions: ReactNode,
    onRowSelectionChanged: (isSelected: boolean) => void
}
export function PageRow(props: PageRowProps) {
    return (
        <tr className={props.isRemoving ? styles.removing : undefined}>
            <td>
                <TriCheckbox state={props.isSelected} onChange={props.onRowSelectionChanged}>
                    <Truncate>{props.name}</Truncate>
                    <div className={styles.mobile}>
                        <div className={styles.badges}>
                            <StatusBadge status={props.status} percentage={props.percentage} error={props.error} />
                            <CategoryBadge category={props.category} />
                        </div>
                        <div>{formatFileSize(props.fileSizeBytes)}</div>
                    </div>
                </TriCheckbox>
            </td>
            <td className={styles.desktop}>
                <CategoryBadge category={props.category} />
            </td>
            <td className={styles.desktop}>
                <StatusBadge status={props.status} percentage={props.percentage} error={props.error} />
            </td>
            <td className={styles.desktop}>
                {formatFileSize(props.fileSizeBytes)}
            </td>
            <td>
                <div className={styles.actions}>
                    {props.actions}
                </div>
            </td>
        </tr>
    );
}

export function CategoryBadge({ category }: { category: string }) {
    const categoryLower = category?.toLowerCase();
    let variant = 'secondary';
    if (categoryLower === 'movies') variant = 'primary';
    if (categoryLower === 'tv') variant = 'info';
    return <Badge bg={variant} style={{ width: '85px' }}>{categoryLower}</Badge>
}