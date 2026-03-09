import { Accordion } from "react-bootstrap";
import styles from "./maintenance.module.css"
import { RemoveUnlinkedFiles } from "./remove-unlinked-files/remove-unlinked-files";
import { ConvertStrmToSymlinks } from "./strm-to-symlinks/strm-to-symlinks";
import { MigrateDatabaseFilesToBlobstore } from "./migrate-database-files-to-blobstore/migrate-database-files-to-blobstore";

type MaintenanceProps = {
    savedConfig: Record<string, string>
};

export function Maintenance({ savedConfig }: MaintenanceProps) {
    return (
        <div className={styles.container}>
            <Accordion className={styles.accordion}>

                <Accordion.Item className={styles.accordionItem} eventKey="remove-unlinked-files">
                    <Accordion.Header className={styles.accordionHeader}>
                        Remove Orphaned Files
                    </Accordion.Header>
                    <Accordion.Body className={styles.accordionBody}>
                        <RemoveUnlinkedFiles savedConfig={savedConfig} />
                    </Accordion.Body>
                </Accordion.Item>


                <Accordion.Item className={styles.accordionItem} eventKey="strm-to-symlinks">
                    <Accordion.Header className={styles.accordionHeader}>
                        Convert Strm Files to Symlnks
                    </Accordion.Header>
                    <Accordion.Body className={styles.accordionBody}>
                        <ConvertStrmToSymlinks savedConfig={savedConfig} />
                    </Accordion.Body>
                </Accordion.Item>


                <Accordion.Item className={styles.accordionItem} eventKey="migrate-database-files-to-blobstore">
                    <Accordion.Header className={styles.accordionHeader}>
                        Migrate Large Database Blobs to Blobstore
                    </Accordion.Header>
                    <Accordion.Body className={styles.accordionBody}>
                        <MigrateDatabaseFilesToBlobstore savedConfig={savedConfig} />
                    </Accordion.Body>
                </Accordion.Item>

            </Accordion>
        </div>
    );
}