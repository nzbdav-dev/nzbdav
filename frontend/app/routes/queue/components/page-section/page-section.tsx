import styles from "./page-section.module.css";
import type { ReactNode } from "react";

export type PageTableProps = {
    title: ReactNode,
    badgeText: string,
    children?: ReactNode,
}

export function PageSection({ title, badgeText, children }: PageTableProps) {
    return (
        <div className={styles.container}>
            <div className={styles.header}>
                {title}
                <div className={styles.badgeText}>
                    {badgeText}
                </div>
            </div>
            {children}
        </div>
    );
}