import { OverlayTrigger, Tooltip } from "react-bootstrap";
import styles from "./status-badge.module.css";
import type React from "react";
import { className } from "~/utils/styling";

export type StatusBadgeProps = {
    status: string,
    percentage?: string,
    error?: string,
}


export function StatusBadge({ status, percentage, error }: StatusBadgeProps) {
    const statusLower = status?.toLowerCase();
    const percentNum = statusLower === "downloading" ? Number(percentage) : 0;

    // determine badge color
    let color = "grey";
    if (statusLower === "completed") color = "rgba(var(--bs-success-rgb)";
    if (statusLower === "failed") color = "rgba(var(--bs-danger-rgb)";
    if (statusLower === "downloading" || percentNum > 0) color = `#333`;

    // determine badge text
    let badgeText = statusLower;
    if (statusLower === "downloading" || percentNum > 0) badgeText = `${percentNum % 100}%`;

    // determine class name
    if (error?.startsWith("Article with message-id")) error = "Missing articles";
    const badgeClass = statusLower === "failed" ? styles["failure-badge"] : "";
    const overlay = statusLower == "failed"
        ? <Tooltip>{error}</Tooltip>
        : <></>;

    return (
        <OverlayTrigger placement="top" overlay={overlay} trigger="click">
            <div className={styles.container}>
                <Badge className={badgeClass} color={color} percentNum={percentNum}>{badgeText}</Badge>
            </div>
        </OverlayTrigger>
    );
}

type BadgeProps = {
    className?: string,
    color: string,
    percentNum: number,
    children?: React.ReactNode
}

function Badge(props: BadgeProps) {
    const isHealthCheck = props.percentNum > 100;

    const progressClassName = isHealthCheck
        ? styles.progress + " " + styles["healthcheck-progress"]
        : styles.progress;

    const style = (props.percentNum >= 0)
        ? { width: `${props.percentNum % 100}%` }
        : undefined;

    return (
        <div {...className([styles.badge, props.className])} style={{ backgroundColor: props.color }}>
            <div className={progressClassName} style={style} />
            <div className={styles["badge-text"]}>{props.children}</div>
        </div>
    );
}