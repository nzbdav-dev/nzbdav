import { useCallback, useRef, useState, useEffect, type ChangeEvent, type KeyboardEvent } from "react";
import styles from "./tag-input.module.css";
import { classNames } from "~/utils/styling";

const delimiter = ",";
type TagInputProps = {
    className?: string;
    value: string;
    onChange: (value: string) => void;
    placeholder?: string;
    id?: string;
    "aria-describedby"?: string;
};

export function TagInput({
    className,
    value,
    onChange,
    placeholder,
    id,
    "aria-describedby": ariaDescribedBy,
}: TagInputProps) {
    const [inputValue, setInputValue] = useState("");
    const [inputWidth, setInputWidth] = useState<number | undefined>(undefined);
    const inputRef = useRef<HTMLInputElement>(null);
    const measureRef = useRef<HTMLSpanElement>(null);

    const tags = value
        .split(delimiter)
        .map(t => t.trim())
        .filter(t => t.length > 0);

    const displayText = inputValue || (tags.length === 0 ? placeholder : "") || "";

    // Measure text width and update input width
    useEffect(() => {
        if (measureRef.current) {
            const width = measureRef.current.offsetWidth + 4; // Add small buffer
            setInputWidth(width);
        }
    }, [displayText]);

    const updateTags = useCallback((newTags: string[]) => {
        onChange(newTags.join(`${delimiter} `));
    }, [onChange, delimiter]);

    const addTag = useCallback((tag: string) => {
        const trimmed = tag.trim();
        if (trimmed && !tags.includes(trimmed)) {
            updateTags([...tags, trimmed]);
        }
        setInputValue("");
    }, [tags, updateTags]);

    const removeTag = useCallback((index: number) => {
        updateTags(tags.filter((_, i) => i !== index));
    }, [tags, updateTags]);

    const handleInputChange = useCallback((e: ChangeEvent<HTMLInputElement>) => {
        const newValue = e.target.value;

        // Find the first occurrence of comma or space
        const commaIndex = newValue.indexOf(",");
        const spaceIndex = newValue.indexOf(" ");
        const delimiterIndex = commaIndex === -1 ? spaceIndex
            : spaceIndex === -1 ? commaIndex
                : Math.min(commaIndex, spaceIndex);

        if (delimiterIndex !== -1) {
            const beforeDelimiter = newValue.slice(0, delimiterIndex).trim();
            const afterDelimiter = newValue.slice(delimiterIndex + 1);

            if (beforeDelimiter) {
                const newTags = tags.includes(beforeDelimiter) ? tags : [...tags, beforeDelimiter];
                updateTags(newTags);
            }
            setInputValue(afterDelimiter);
        } else {
            setInputValue(newValue);
        }
    }, [tags, updateTags]);

    const handleKeyDown = useCallback((e: KeyboardEvent<HTMLInputElement>) => {
        if (e.key === "Enter") {
            e.preventDefault();
            if (inputValue.trim()) {
                addTag(inputValue);
            }
        } else if (e.key === "Backspace" && inputValue === "" && tags.length > 0) {
            removeTag(tags.length - 1);
        }
    }, [inputValue, tags, addTag, removeTag]);

    const handleContainerClick = useCallback(() => {
        inputRef.current?.focus();
    }, []);

    return (
        <div className={classNames([styles.container, className])} onClick={handleContainerClick}>
            {tags.map((tag, index) => (
                <span key={index} className={styles.tag}>
                    {tag}
                    <button
                        type="button"
                        className={styles.removeButton}
                        onClick={(e) => {
                            e.stopPropagation();
                            removeTag(index);
                        }}
                        aria-label={`Remove ${tag}`}
                    >
                        &times;
                    </button>
                </span>
            ))}
            <span ref={measureRef} className={styles.measure}>
                {displayText}
            </span>
            <input
                ref={inputRef}
                type="text"
                className={styles.input}
                style={{ width: inputWidth }}
                value={inputValue}
                onChange={handleInputChange}
                onKeyDown={handleKeyDown}
                onBlur={() => {
                    if (inputValue.trim()) {
                        addTag(inputValue);
                    }
                }}
                placeholder={tags.length === 0 ? placeholder : undefined}
                id={id}
                aria-describedby={ariaDescribedBy}
            />
        </div>
    );
}
