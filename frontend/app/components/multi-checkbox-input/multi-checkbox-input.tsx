import { useCallback, useMemo } from "react";
import { Form } from "react-bootstrap";
import styles from "./multi-checkbox-input.module.css";

type MultiCheckboxInputProps = {
    options: string[];
    value: string;
    onChange: (value: string) => void;
};

export function MultiCheckboxInput({ options, value, onChange }: MultiCheckboxInputProps) {
    const selectedOptions = useMemo(() => {
        if (!value || value.trim() === "") return [];
        return value.split(",").map(c => c.trim()).filter(c => c.length > 0);
    }, [value]);

    const onOptionCheckboxChange = useCallback((option: string, checked: boolean) => {
        let newSelected: string[];
        if (checked) {
            newSelected = [...selectedOptions, option];
        } else {
            newSelected = selectedOptions.filter(o => o !== option);
        }
        onChange(newSelected.join(", "));
    }, [onChange, selectedOptions]);

    if (options.length === 0) {
        return null;
    }

    return (
        <div className={styles.container}>
            {options.map(option => (
                <Form.Check
                    key={option}
                    type="checkbox"
                    id={`multi-checkbox-${option}`}
                    label={option}
                    checked={selectedOptions.includes(option)}
                    onChange={e => onOptionCheckboxChange(option, e.target.checked)}
                />
            ))}
        </div>
    );
}
