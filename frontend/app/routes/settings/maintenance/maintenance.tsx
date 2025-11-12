import { Accordion } from "react-bootstrap";
import styles from "./maintenance.module.css"
import { RemoveUnlinkedFiles } from "./remove-unlinked-files/remove-unlinked-files";

type MaintenanceProps = {
    savedConfig: Record<string, string>
};

export function Maintenance({ savedConfig }: MaintenanceProps) {
    return (
        <div className={styles.container}>
            <Accordion className={styles.accordion} defaultActiveKey="remove-unlinked-files">
                <Accordion.Item className={styles.accordionItem} eventKey="remove-unlinked-files">
                    <Accordion.Header className={styles.accordionHeader}>
                        Remove Orphaned Files
                    </Accordion.Header>
                    <Accordion.Body className={styles.accordionBody}>
                        <RemoveUnlinkedFiles savedConfig={savedConfig} />
                    </Accordion.Body>
                </Accordion.Item>
            </Accordion>
        </div>
    );
}