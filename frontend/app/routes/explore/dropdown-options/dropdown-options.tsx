import { useEffect, useRef, type ReactNode } from "react";
import styles from "./dropdown-options.module.css";
import { classNames } from "~/utils/styling";

export type DropdownOptionsProps = {
    isOpen?: boolean;
    options: (DropdownOption | undefined)[];
    onClose?: () => void;
};

export type DropdownOption = {
    option: ReactNode;
    linkTo?: string,
    onSelect?: () => void;
}

export function DropdownOptions({ isOpen = true, options, onClose }: DropdownOptionsProps) {
    const ref = useRef<HTMLUListElement>(null);

    useEffect(() => {
        if (!isOpen) return;

        function handleClick(e: MouseEvent) {
            if (ref.current && !ref.current.contains(e.target as Node)) {
                e.preventDefault();
                onClose?.();
            }
        }

        document.addEventListener("click", handleClick);
        return () => document.removeEventListener("click", handleClick);
    }, [isOpen, onClose]);

    return !isOpen ? null : (
        <ul ref={ref} className={classNames([styles.dropdown])}>
            {options.filter(x => !!x).map((option, index) => (
                <li key={index} className={styles.option}>
                    {option.linkTo && <>
                        <a href={option.linkTo}>
                            <button
                                type="button"
                                className={styles.optionButton}
                                onClick={() => option.onSelect?.()}
                            >
                                {option.option}
                            </button>
                        </a>
                    </>}
                    {!option.linkTo &&
                        <button
                            type="button"
                            className={styles.optionButton}
                            onClick={() => option.onSelect?.()}
                        >
                            {option.option}
                        </button>
                    }
                </li>
            ))}
        </ul>
    );
}
