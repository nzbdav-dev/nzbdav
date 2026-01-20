import styles from "./page-section.module.css";
import type { ReactNode } from "react";

export type PageTableProps = {
    title: ReactNode,
    subTitle?: ReactNode,
    badgeText?: string,
    children?: ReactNode,
}

export function PageSection({ title, subTitle, badgeText, children }: PageTableProps) {
    return (
        <div className={styles.container}>
            <div className={styles.header}>
                <div className={styles.titleRow}>
                    {title}
                    {badgeText &&
                        <div className={styles.badgeText}>
                            {badgeText}
                        </div>
                    }
                </div>
                {subTitle}
            </div>
            {children}
        </div>
    );
}