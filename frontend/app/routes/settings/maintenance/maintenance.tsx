import { Accordion, Form } from "react-bootstrap";
import styles from "./maintenance.module.css"
import { RemoveUnlinkedFiles } from "./remove-unlinked-files/remove-unlinked-files";
import { ConvertStrmToSymlinks } from "./strm-to-symlinks/strm-to-symlinks";
import { MigrateDatabaseFilesToBlobstore } from "./migrate-database-files-to-blobstore/migrate-database-files-to-blobstore";
import type { Dispatch, SetStateAction } from "react";

type MaintenanceProps = {
    savedConfig: Record<string, string>,
    config: Record<string, string>,
    setNewConfig: Dispatch<SetStateAction<Record<string, string>>>,
};

export function Maintenance({ savedConfig, config, setNewConfig }: MaintenanceProps) {
    return (
        <div>
            <div className={styles.settingsContainer}>
                <Form.Group>
                    <Form.Check
                        className={styles.input}
                        type="checkbox"
                        id="db-startup-vacuum-enabled-checkbox"
                        aria-describedby="db-startup-vacuum-enabled-help"
                        label="Perform Database Vacuum on Start"
                        checked={config["db.is-startup-vacuum-enabled"] === "true"}
                        onChange={e => setNewConfig({ ...config, "db.is-startup-vacuum-enabled": "" + e.target.checked })} />
                    <Form.Text id="db-startup-vacuum-enabled-help" muted>
                        When enabled, nzbdav will run a SQLite VACUUM on the database at every startup. This reclaims unused disk space and can improve query performance over time, but may increase startup time for large databases.
                    </Form.Text>
                </Form.Group>
            </div>
            <div className={styles.tasksContainer}>
                <hr />
                <Accordion>
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
        </div>
    );
}

export function isMaintenanceSettingsUpdated(config: Record<string, string>, newConfig: Record<string, string>) {
    return config["db.is-startup-vacuum-enabled"] !== newConfig["db.is-startup-vacuum-enabled"];
}