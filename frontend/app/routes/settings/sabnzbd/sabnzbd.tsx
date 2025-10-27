import { Button, Form, InputGroup } from "react-bootstrap";
import styles from "./sabnzbd.module.css"
import { useCallback, type Dispatch, type SetStateAction } from "react";
import { className } from "~/utils/styling";
import { isPositiveInteger } from "../usenet/usenet";

type SabnzbdSettingsProps = {
    config: Record<string, string>
    setNewConfig: Dispatch<SetStateAction<Record<string, string>>>
};

export function SabnzbdSettings({ config, setNewConfig }: SabnzbdSettingsProps) {

    const onRefreshApiKey = useCallback(() => {
        setNewConfig({ ...config, "api.key": generateNewApiKey() })
    }, [setNewConfig, config]);

    return (
        <div className={styles.container}>
            <Form.Group>
                <Form.Label htmlFor="api-key-input">API Key</Form.Label>
                <InputGroup className={styles.input}>
                    <Form.Control
                        type="text"
                        id="api-key-input"
                        aria-describedby="api-key-help"
                        value={config["api.key"]}
                        readOnly />
                    <Button variant="primary" onClick={onRefreshApiKey}>
                        Refresh
                    </Button>
                </InputGroup>
                <Form.Text id="api-key-help" muted>
                    Use this API key when configuring your download client in Radarr or Sonarr.
                </Form.Text>
            </Form.Group>
            <hr />
            <Form.Group>
                <Form.Label htmlFor="categories-input">Categories</Form.Label>
                <Form.Control
                    {...className([styles.input, !isValidCategories(config["api.categories"]) && styles.error])}
                    type="text"
                    id="categories-input"
                    aria-describedby="categories-help"
                    value={config["api.categories"]}
                    placeholder="tv, movies, audio, software"
                    onChange={e => setNewConfig({ ...config, "api.categories": e.target.value })} />
                <Form.Text id="categories-help" muted>
                    Comma-separated categories. Only letters, numbers, and dashes are allowed.
                </Form.Text>
            </Form.Group>
            <hr />
            <Form.Group>
                <Form.Label htmlFor="mount-dir-input">Rclone Mount Directory</Form.Label>
                <Form.Control
                    className={styles.input}
                    type="text"
                    id="mount-dir-input"
                    aria-describedby="mount-dir-help"
                    placeholder="/mnt/nzbdav"
                    value={config["rclone.mount-dir"]}
                    onChange={e => setNewConfig({ ...config, "rclone.mount-dir": e.target.value })} />
                <Form.Text id="mount-dir-help" muted>
                    The location at which you've mounted (or will mount) the webdav root, through Rclone. This is used to tell Radarr / Sonarr where to look for completed "downloads."
                </Form.Text>
            </Form.Group>
            <hr />
            <Form.Group>
                <Form.Label htmlFor="max-queue-connections-input">Max Connections for Queue Processing</Form.Label>
                <Form.Control
                    {...className([styles.input, !isValidQueueConnections(config["api.max-queue-connections"]) && styles.error])}
                    type="text"
                    id="max-queue-connections-input"
                    aria-describedby="max-queue-connections-help"
                    placeholder="All"
                    value={config["api.max-queue-connections"]}
                    onChange={e => setNewConfig({ ...config, "api.max-queue-connections": e.target.value })} />
                <Form.Text id="max-queue-connections-help" muted>
                    Queue processing tasks will not use any more than this number of connections. Will default to your overall Max Connections if left empty.
                </Form.Text>
            </Form.Group>
            <hr />
            <Form.Group>
                <Form.Check
                    className={styles.input}
                    type="checkbox"
                    id="ensure-importable-video-checkbox"
                    aria-describedby="ensure-importable-video-help"
                    label={`Fail downloads for nzbs without video content`}
                    checked={config["api.ensure-importable-video"] === "true"}
                    onChange={e => setNewConfig({ ...config, "api.ensure-importable-video": "" + e.target.checked })} />
                <Form.Text id="ensure-importable-video-help" muted>
                    Whether to mark downloads as `failed` when no single video file is found inside the nzb. This will force Radarr / Sonarr to automatically look for a new nzb.
                </Form.Text>
            </Form.Group>
            <hr />
            <Form.Group>
                <Form.Check
                    className={styles.input}
                    type="checkbox"
                    id="ensure-article-existence-checkbox"
                    aria-describedby="ensure-article-existence-help"
                    label={`Perform article health check during downloads`}
                    checked={config["api.ensure-article-existence"] === "true"}
                    onChange={e => setNewConfig({ ...config, "api.ensure-article-existence": "" + e.target.checked })} />
                <Form.Text id="ensure-article-existence-help" muted>
                    Whether to check for the existence of all articles within an NZB during queue processing. This process may be slow.
                </Form.Text>
            </Form.Group>
            <hr />
            <Form.Group>
                <Form.Check
                    className={styles.input}
                    type="checkbox"
                    id="ignore-history-limit-checkbox"
                    aria-describedby="ignore-history-limit-help"
                    label={`Always send full History to Radarr/Sonarr`}
                    checked={config["api.ignore-history-limit"] === "true"}
                    onChange={e => setNewConfig({ ...config, "api.ignore-history-limit": "" + e.target.checked })} />
                <Form.Text id="ignore-history-limit-help" muted>
                    When enabled, this will ignore the History limit sent by radarr/sonarr and always reply with all History items.&nbsp;
                    <a href="https://github.com/Sonarr/Sonarr/issues/5452">See here</a>.
                </Form.Text>
            </Form.Group>
        </div>
    );
}

export function isSabnzbdSettingsUpdated(config: Record<string, string>, newConfig: Record<string, string>) {
    return config["api.key"] !== newConfig["api.key"]
        || config["api.categories"] !== newConfig["api.categories"]
        || config["rclone.mount-dir"] !== newConfig["rclone.mount-dir"]
        || config["api.max-queue-connections"] !== newConfig["api.max-queue-connections"]
        || config["api.ensure-importable-video"] !== newConfig["api.ensure-importable-video"]
        || config["api.ensure-article-existence"] !== newConfig["api.ensure-article-existence"]
        || config["api.ignore-history-limit"] !== newConfig["api.ignore-history-limit"]
}

export function isSabnzbdSettingsValid(newConfig: Record<string, string>) {
    return isValidCategories(newConfig["api.categories"])
        && isValidQueueConnections(newConfig["api.max-queue-connections"]);
}

export function generateNewApiKey(): string {
    return crypto.randomUUID().toString().replaceAll("-", "");
}

function isValidCategories(categories: string): boolean {
    if (categories === "") return true;
    var parts = categories.split(",");
    return parts.map(x => x.trim()).every(x => isAlphaNumericWithDashes(x));
}

function isAlphaNumericWithDashes(input: string): boolean {
    const regex = /^[A-Za-z0-9-]+$/;
    return regex.test(input);
}

function isValidQueueConnections(maxQueueConnections: string): boolean {
    return maxQueueConnections === "" || isPositiveInteger(maxQueueConnections);
}
