import { useCallback, useRef, useEffect, type ChangeEvent } from "react";
import { Form } from "react-bootstrap";
import styles from "./expanding-text-input.module.css";
import { classNames } from "~/utils/styling";

type ExpandingTextInputProps = {
    className?: string;
    value: string;
    onChange: (value: string) => void;
    placeholder?: string;
    id?: string;
    "aria-describedby"?: string;
    readOnly?: boolean;
};

export function ExpandingTextInput({
    className,
    value,
    onChange,
    placeholder,
    id,
    "aria-describedby": ariaDescribedBy,
    readOnly,
}: ExpandingTextInputProps) {
    const textareaRef = useRef<HTMLTextAreaElement>(null);

    const adjustHeight = useCallback(() => {
        const textarea = textareaRef.current;
        if (textarea) {
            textarea.style.height = '0';
            textarea.style.height = `${textarea.scrollHeight}px`;
        }
    }, []);

    // Adjust height when value changes, becoming visible, or container resizes
    useEffect(() => {
        const textarea = textareaRef.current;
        if (!textarea) return;

        adjustHeight();

        const intersectionObserver = new IntersectionObserver((entries) => {
            if (entries[0].isIntersecting) {
                adjustHeight();
            }
        });
        intersectionObserver.observe(textarea);

        const resizeObserver = new ResizeObserver(() => {
            adjustHeight();
        });
        resizeObserver.observe(textarea);

        return () => {
            intersectionObserver.disconnect();
            resizeObserver.disconnect();
        };
    }, [value, adjustHeight]);

    const handleChange = useCallback((e: ChangeEvent<HTMLTextAreaElement>) => {
        onChange(e.target.value);
    }, [onChange]);

    return (
        <Form.Control
            as="textarea"
            ref={textareaRef}
            className={classNames([styles.textarea, className])}
            value={value}
            onChange={handleChange}
            onInput={adjustHeight}
            placeholder={placeholder}
            id={id}
            aria-describedby={ariaDescribedBy}
            readOnly={readOnly}
            spellCheck={false}
        />
    );
}
