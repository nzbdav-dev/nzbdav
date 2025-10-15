import type { Route } from "./+types/route";
import styles from "./route.module.css"

export default function Health(props: Route.ComponentProps) {
    return (
        <div className={styles.container}>
            {/* Health content placeholder */}
        </div>
    );
}