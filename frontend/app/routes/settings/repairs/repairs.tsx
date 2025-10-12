import { Form } from "react-bootstrap";
import styles from "./repairs.module.css"
import { type Dispatch, type SetStateAction } from "react";
import { className } from "~/utils/styling";
import { isPositiveInteger } from "../usenet/usenet";

type RepairsSettingsProps = {
    config: Record<string, string>
    setNewConfig: Dispatch<SetStateAction<Record<string, string>>>
};

export function RepairsSettings({ config, setNewConfig }: RepairsSettingsProps) {
    return (
        <div className={styles.container}>
            <Form.Group>
                <Form.Label htmlFor="repairs-connections-input">Max Connections for Health Checks</Form.Label>
                <Form.Control
                    {...className([styles.input, !isValidRepairsConnections(config["repair.connections"]) && styles.error])}
                    type="text"
                    id="repairs-connections-input"
                    aria-describedby="repairs-connections-help"
                    placeholder="10"
                    value={config["repair.connections"] || ""}
                    onChange={e => setNewConfig({ ...config, "repair.connections": e.target.value })} />
                <Form.Text id="repairs-connections-help" muted>
                    The background health-check job will not use any more than this number of connections.
                </Form.Text>
            </Form.Group>
            <hr />
            <Form.Group>
                <Form.Check
                    className={styles.input}
                    type="checkbox"
                    id="delete-unlinked-files-checkbox"
                    aria-describedby="delete-unlinked-files-help"
                    label="Delete Unlinked Files When Unhealthy"
                    checked={config["repair.delete-unlinked-files"] === "true"}
                    onChange={e => setNewConfig({ ...config, "repair.delete-unlinked-files": "" + e.target.checked })} />
                <Form.Text id="delete-unlinked-files-help" muted>
                    When an unhealthy file is identified that cannot be linked back to a Radarr/Sonarr item, should it be deleted?
                </Form.Text>
            </Form.Group>
        </div>
    );
}

export function isRepairsSettingsUpdated(config: Record<string, string>, newConfig: Record<string, string>) {
    return config["repair.connections"] !== newConfig["repair.connections"]
        || config["repair.delete-unlinked-files"] !== newConfig["repair.delete-unlinked-files"];
}

export function isRepairsSettingsValid(newConfig: Record<string, string>) {
    return isValidRepairsConnections(newConfig["repair.connections"]);
}

function isValidRepairsConnections(repairsConnections: string): boolean {
    return repairsConnections === "" || isPositiveInteger(repairsConnections);
}