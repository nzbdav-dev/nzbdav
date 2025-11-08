import { Alert, Form } from "react-bootstrap";
import styles from "./repairs.module.css"
import { type Dispatch, type SetStateAction } from "react";
import { className } from "~/utils/styling";

type RepairsSettingsProps = {
    config: Record<string, string>
    setNewConfig: Dispatch<SetStateAction<Record<string, string>>>
};

export function RepairsSettings({ config, setNewConfig }: RepairsSettingsProps) {
    const libraryDirConfig = config["media.library-dir"];
    const arrConfig = JSON.parse(config["arr.instances"]);
    const areArrInstancesConfigured =
        arrConfig.RadarrInstances.length > 0 ||
        arrConfig.SonarrInstances.length > 0;
    const canEnableRepairs = !!libraryDirConfig && areArrInstancesConfigured;
    var helpText = canEnableRepairs
        ? "When enabled, usenet items will be continuously monitored for health. Unhealthy items will be removed. If an unhealthy item is part of your Radarr/Sonarr library, a new search will be triggered to find a replacement."
        : "When enabled, usenet items will be continuously monitored for health. Unhealthy items will be removed and replaced. This setting can only be enabled once your Library-Directory and Radarr/Sonarr instances are configured.";

    return (
        <div className={styles.container}>
            <Form.Group>
                <Form.Check
                    className={styles.input}
                    type="checkbox"
                    id="enable-repairs-checkbox"
                    aria-describedby="enable-repairs-help"
                    label={`Enable Background Repairs`}
                    checked={canEnableRepairs && config["repair.enable"] === "true"}
                    disabled={!canEnableRepairs}
                    onChange={e => setNewConfig({ ...config, "repair.enable": "" + e.target.checked })} />
                <Form.Text id="enable-repairs-help" muted>
                    {helpText}
                </Form.Text>
            </Form.Group>
            <hr />
            <Form.Group>
                <Form.Label htmlFor="library-dir-input">Library Directory</Form.Label>
                <Form.Control
                    className={styles.input}
                    type="text"
                    id="library-dir-input"
                    aria-describedby="library-dir-help"
                    value={config["media.library-dir"]}
                    onChange={e => setNewConfig({ ...config, "media.library-dir": e.target.value })} />
                <Form.Text id="library-dir-help" muted>
                    The path to your organized media library that contains all your imported symlinks or *.strm files.
                    Make sure this path is visible to your NzbDAV container.
                </Form.Text>
            </Form.Group>
            <hr />
            <Form.Group>
                <Form.Label htmlFor="repairs-connections-input">Max Connections for Health Checks</Form.Label>
                <Form.Control
                    {...className([styles.input, !isValidRepairsConnections(config["repair.connections"]) && styles.error])}
                    type="text"
                    id="repairs-connections-input"
                    aria-describedby="repairs-connections-help"
                    placeholder={"All"}
                    value={config["repair.connections"] || ""}
                    onChange={e => setNewConfig({ ...config, "repair.connections": e.target.value })} />
                <Form.Text id="repairs-connections-help" muted>
                    The background health-check job will not use any more than this number of connections. Will default to your overall Max Connections if left empty.
                </Form.Text>
            </Form.Group>
        </div>
    );
}

export function isRepairsSettingsUpdated(config: Record<string, string>, newConfig: Record<string, string>) {
    return config["repair.enable"] !== newConfig["repair.enable"]
        || config["repair.connections"] !== newConfig["repair.connections"]
        || config["media.library-dir"] !== newConfig["media.library-dir"];
}

export function isRepairsSettingsValid(newConfig: Record<string, string>) {
    return isValidRepairsConnections(newConfig["repair.connections"]);
}

function isValidRepairsConnections(repairsConnections: string): boolean {
    return repairsConnections === "" || isNonNegativeInteger(repairsConnections);
}

function isNonNegativeInteger(value: string) {
    const num = Number(value);
    return Number.isInteger(num) && num >= 0 && value.trim() === num.toString();
}
