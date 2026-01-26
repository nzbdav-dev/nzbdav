import styles from "./word-wrap.module.css";
import type { ReactNode } from "react";

export type WordWrapProps = {
    children: ReactNode
}

export function WordWrap({ children }: WordWrapProps) {
    return <div className={styles.wordWrap}>{children}</div>;
}
