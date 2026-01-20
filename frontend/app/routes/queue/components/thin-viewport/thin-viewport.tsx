import type { ReactNode } from "react"
import { useId } from "react"
import styles from "./thin-viewport.module.css"

export type ThinViewportProps = {
    width: string,
    children: ReactNode,
}

export function ThinViewport({ width, children }: ThinViewportProps) {
    const id = useId();
    const uniqueId = `thin-viewport-${id.replace(/:/g, '-')}`;

    return (
        <>
            <style dangerouslySetInnerHTML={{
                __html: `
                    @media (min-width: ${width}) {
                        .${uniqueId} {
                            display: none !important;
                        }
                    }
                `
            }} />
            <div className={`${styles.container} ${uniqueId}`}>
                {children}
            </div>
        </>
    );
}
