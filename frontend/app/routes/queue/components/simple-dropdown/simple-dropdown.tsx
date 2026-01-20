import { useCallback, useEffect, useRef, useState } from "react"
import styles from "./simple-dropdown.module.css"

export type SimpleDropdownProps = {
    options: string[],
    value: string,
    onChange: (value: string) => void,
}

export function SimpleDropdown({ options, value, onChange }: SimpleDropdownProps) {
    const [isOpen, setIsOpen] = useState(false);
    const dropdownRef = useRef<HTMLDivElement>(null);

    const toggleDropdown = useCallback(() => {
        setIsOpen(prev => !prev);
    }, []);

    const handleOptionClick = useCallback((option: string) => {
        onChange(option);
        setIsOpen(false);
    }, [onChange]);

    // Close dropdown when clicking outside
    useEffect(() => {
        const handleClickOutside = (event: MouseEvent) => {
            if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
                setIsOpen(false);
            }
        };

        if (isOpen) {
            document.addEventListener('mousedown', handleClickOutside);
        }

        return () => {
            document.removeEventListener('mousedown', handleClickOutside);
        };
    }, [isOpen]);

    return (
        <div className={styles.container} ref={dropdownRef}>
            <div className={styles.selected} onClick={toggleDropdown}>
                {value}
                <span className={styles.arrow}></span>
            </div>
            {isOpen && (
                <div className={styles.dropdown}>
                    {options.map(option => (
                        <div
                            key={option}
                            className={styles.option}
                            onClick={() => handleOptionClick(option)}
                        >
                            {option}
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
}
