import styles from "./pagination.module.css";
import { Pagination as BSPagination, Form, Button } from "react-bootstrap";
import { useEffect, useState } from "react";

type Props = {
    page: number;
    pageSize: number;
    total: number;
    pageSizeOptions?: number[];
    onPageChange: (page: number) => void;
    onPageSizeChange: (size: number) => void;
};

export function Pagination({ page, pageSize, total, pageSizeOptions = [25, 50, 100], onPageChange, onPageSizeChange }: Props) {
    const totalPages = Math.max(1, Math.ceil(total / pageSize));
    const canPrev = page > 1;
    const canNext = page < totalPages;
    const [pendingPage, setPendingPage] = useState(page);

    const handlePageChange = (next: number) => {
        if (next < 1 || next > totalPages) return;
        onPageChange(next);
    };

    // keep pendingPage in sync with external page
    useEffect(() => {
        setPendingPage(page);
    }, [page]);

    const applyPendingPage = () => {
        if (!pendingPage || isNaN(pendingPage)) return;
        handlePageChange(pendingPage);
    };

    return (
        <div className={styles.container}>
            <div className={styles.left}>
                <div className={styles.pageGroup}>
                    <BSPagination size="sm" className={styles.pagination}>
                        <BSPagination.Prev disabled={!canPrev} onClick={() => handlePageChange(page - 1)} />
                    </BSPagination>
                    <div className={styles.pageInputWrapper}>
                        <Form.Control
                            size="sm"
                            type="number"
                            min={1}
                            max={totalPages}
                            value={pendingPage}
                            onChange={e => setPendingPage(Number(e.target.value))}
                            className={styles.pageInput}
                        />
                        <span className={styles.ofText}>/ {totalPages}</span>
                        <Button size="sm" variant="secondary" className={styles.goButton} onClick={applyPendingPage}>
                            Go
                        </Button>
                    </div>
                    <BSPagination size="sm" className={styles.pagination}>
                        <BSPagination.Next disabled={!canNext} onClick={() => handlePageChange(page + 1)} />
                    </BSPagination>
                </div>
                <Form.Select
                    size="sm"
                    className={styles.pageSize}
                    value={pageSize}
                    onChange={e => onPageSizeChange(Number(e.target.value))}
                >
                    {pageSizeOptions.map(opt => (
                        <option key={opt} value={opt}>{opt} / page</option>
                    ))}
                </Form.Select>
                <div className={styles.count}>
                    Showing {(page - 1) * pageSize + 1}-{Math.min(page * pageSize, total)} of {total}
                </div>
            </div>
        </div>
    );
}
