import { Form } from "react-bootstrap";
import { useEffect, useRef, type ReactNode } from "react";
import styles from "./tri-checkbox.module.css";

export type TriCheckboxState = "all" | "some" | "none" | boolean
export type TriCheckboxProps = {
    state: TriCheckboxState,
    onChange?: (isChecked: boolean) => void,
    children: ReactNode
}

export function TriCheckbox({ state, onChange, children }: TriCheckboxProps) {
    const checkboxRef = useRef<HTMLInputElement>(null);
    useEffect(() => {
        if (checkboxRef && checkboxRef.current) {
            checkboxRef.current.indeterminate = (state === "some");
        }
    }, [checkboxRef, state])

    return (
        <div className={styles.container}>
            <div className={styles.checkbox}>
                <Form.Check
                    ref={checkboxRef}
                    checked={state === "all" || state === true}
                    onChange={(e) => onChange && onChange(e.target.checked)}
                />
            </div>
            <div>
                {children}
            </div>
        </div>

    )
}