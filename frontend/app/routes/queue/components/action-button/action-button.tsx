import { Button } from "react-bootstrap";
import styles from "./action-button.module.css";
import type { ReactNode } from "react";

export type ActionButtonProps = {
    type: "delete" | "explore",
    text?: string,
    disabled?: boolean,
    onClick?: () => void,
}

export function ActionButton({ type, text, disabled, onClick }: ActionButtonProps) {
    const variant = type === "delete" ? "outline-danger" : "outline-warning";
    return (
        <Button
            className={styles["action-button"]}
            variant={variant} onClick={onClick}
            disabled={disabled}>
            {type === "delete" && <TrashIcon />}
            {type === "explore" && <DirectoryIcon />}
            {text && <div className={styles.text}>{text}</div>}
        </Button>
    )
}

function DirectoryIcon() {
    return (
        <svg className={styles["directory-icon"]} xmlns="http://www.w3.org/2000/svg" viewBox="0 0 22 24">
            <path d="M2 4.75C2 3.784 2.784 3 3.75 3h4.971c.58 0 1.12.286 1.447.765l1.404 2.063a.25.25 0 0 0 .207.11h6.224c.966 0 1.75.783 1.75 1.75v.117H5.408a.848.848 0 0 0 0 1.695h15.484a1 1 0 0 1 .995 1.102L21 19.25c-.106 1.05-.784 1.75-1.75 1.75H3.75A1.75 1.75 0 0 1 2 19.25z" />
        </svg>
    )
}

function TrashIcon() {
    return (
        <svg className={styles["trash-icon"]} xmlns="http://www.w3.org/2000/svg" viewBox="0 0 22 24">
            <path d="M16.313 4V2.144C16.313.96 15.353 0 14.169 0H7.831A2.14 2.14 0 0 0 5.69 2.189v-.002V4H0v2h.575c.196.023.372.099.515.214l-.002-.002c.119.157.203.346.239.552l.001.008l1.187 15.106c.094 1.84.094 2.118 2.25 2.118h12.462c2.16 0 2.16-.275 2.25-2.113l1.187-15.1c.036-.217.12-.409.242-.572l-.002.003a1 1 0 0 1 .508-.212h.58v-2h-5.687zM7 2.187c0-.6.487-.938 1.106-.938h5.734c.618 0 1.162.344 1.162.938V4h-8zM6.469 20l-.64-12h1.269l.656 12zm5.225 0H10.32V8h1.375zm3.85 0h-1.275l.656-12h1.269z" />
        </svg>
    )
}