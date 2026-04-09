import { Accordion, Form, InputGroup } from "react-bootstrap";
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
                <hr />
                <Form.Group>
                    <Form.Check
                        className={styles.input}
                        type="checkbox"
                        id="remove-orphaned-schedule-enabled-checkbox"
                        aria-describedby="remove-orphaned-schedule-help"
                        label={'Schedule "Remove Orphaned Files" Task Daily'}
                        checked={isScheduledOrphanTaskEnabled(config)}
                        onChange={e => setNewConfig({ ...config, "maintenance.remove-orphaned-schedule-enabled": "" + e.target.checked })} />
                    <InputGroup className={styles.input} style={{ marginTop: '15px' }}>
                        <Form.Select
                            disabled={!isScheduledOrphanTaskEnabled(config)}
                            value={getScheduledTime(config).hour}
                            onChange={e => setNewConfig({
                                ...config,
                                "maintenance.remove-orphaned-schedule-time": buildScheduledTime(
                                    parseInt(e.target.value),
                                    getScheduledTime(config).minute,
                                    getScheduledTime(config).period
                                )
                            })}>
                            {Array.from({ length: 12 }, (_, i) => i + 1).map(h => (
                                <option key={h} value={h}>{h}</option>
                            ))}
                        </Form.Select>
                        <Form.Select
                            disabled={!isScheduledOrphanTaskEnabled(config)}
                            value={getScheduledTime(config).minute}
                            onChange={e => setNewConfig({
                                ...config,
                                "maintenance.remove-orphaned-schedule-time": buildScheduledTime(
                                    getScheduledTime(config).hour,
                                    parseInt(e.target.value),
                                    getScheduledTime(config).period
                                )
                            })}>
                            <option value={0}>00</option>
                            <option value={15}>15</option>
                            <option value={30}>30</option>
                            <option value={45}>45</option>
                        </Form.Select>
                        <Form.Select
                            disabled={!isScheduledOrphanTaskEnabled(config)}
                            value={getScheduledTime(config).period}
                            onChange={e => setNewConfig({
                                ...config,
                                "maintenance.remove-orphaned-schedule-time": buildScheduledTime(
                                    getScheduledTime(config).hour,
                                    getScheduledTime(config).minute,
                                    e.target.value as "am" | "pm"
                                )
                            })}>
                            <option value="am">am</option>
                            <option value="pm">pm</option>
                        </Form.Select>
                    </InputGroup>
                    <Form.Text id="remove-orphaned-schedule-help" muted>
                        When enabled, the "Remove Orphaned Files" task will run every day at the specified time.
                        You may need to set the TZ env variable to ensure the correct timezone.
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
    return config["db.is-startup-vacuum-enabled"] !== newConfig["db.is-startup-vacuum-enabled"]
        || config["maintenance.remove-orphaned-schedule-enabled"] !== newConfig["maintenance.remove-orphaned-schedule-enabled"]
        || config["maintenance.remove-orphaned-schedule-time"] !== newConfig["maintenance.remove-orphaned-schedule-time"];
}

function isScheduledOrphanTaskEnabled(config: Record<string, string>) {
    return config["maintenance.remove-orphaned-schedule-enabled"] === "true";
}

function getScheduledTime(config: Record<string, string>): { hour: number, minute: number, period: "am" | "pm" } {
    const totalMinutes = parseInt(config["maintenance.remove-orphaned-schedule-time"] || "0");
    const hour24 = Math.floor(totalMinutes / 60);
    return {
        hour: hour24 % 12 || 12, // 0→12 (midnight), 1→1, ..., 12→12 (noon), 13→1, ...
        minute: totalMinutes % 60,
        period: Math.floor(totalMinutes / 60) >= 12 ? "pm" : "am"
    };
}

function buildScheduledTime(hour: number, minute: number, period: "am" | "pm"): string {
    const hour24 = (hour % 12) + (period === "pm" ? 12 : 0);
    return "" + (hour24 * 60 + minute);
}