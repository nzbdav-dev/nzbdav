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
                <Form.Label htmlFor="manual-category-input">Manual Upload Category</Form.Label>
                <Form.Control
                    className={styles.input}
                    type="text"
                    id="manual-category-input"
                    aria-describedby="manual-category-help"
                    value={config["api.manual-category"]}
                    placeholder="uncategorized"
                    onChange={e => setNewConfig({ ...config, "api.manual-category": e.target.value })} />
                <Form.Text id="manual-category-help" muted>
                    The category to use for manual uploads through the Queue page on the UI.
                </Form.Text>
            </Form.Group>
            <hr />
            <Form.Group>
                <Form.Label htmlFor="import-strategy-input">Import Strategy</Form.Label>
                <Form.Select
                    className={styles.input}
                    value={config["api.import-strategy"]}
                    onChange={e => setNewConfig({ ...config, "api.import-strategy": e.target.value })}
                >
                    <option value="symlinks">Symlinks — Plex</option>
                    <option value="strm">STRM Files — Emby/Jellyfin</option>
                </Form.Select>
                <Form.Text id="import-strategy-help" muted>
                    If you need to be able to stream from Plex, you will need to configure rclone and should select the `Symlinks` option here. If you only need to stream through Emby or Jellyfin, then you can skip rclone altogether and select the `STRM Files` option.
                </Form.Text>
            </Form.Group>
            {/* <hr /> */}
            {config["api.import-strategy"] === 'symlinks' &&
                <Form.Group className={styles.subGroup}>
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
            }
            {config["api.import-strategy"] === 'strm' && <>
                <Form.Group  className={styles.subGroup}>
                    <Form.Label htmlFor="completed-downloads-dir-input">Completed Downloads Dir</Form.Label>
                    <Form.Control
                        className={styles.input}
                        type="text"
                        id="completed-downloads-dir-input"
                        aria-describedby="completed-downloads-dir-help"
                        placeholder="/data/completed-downloads"
                        value={config["api.completed-downloads-dir"]}
                        onChange={e => setNewConfig({ ...config, "api.completed-downloads-dir": e.target.value })} />
                    <Form.Text id="completed-downloads-dir-help" muted>
                        This is used to tell Radarr / Sonarr where to look for completed "downloads." Make sure this path is also visible to your Radarr / Sonarr containers. The "downloads" placed in this folder will all be *.strm files that point to nzbdav for streaming.
                    </Form.Text>
                </Form.Group>
                <Form.Group  className={styles.subGroup}>
                    <Form.Label htmlFor="base-url-input">Base URL</Form.Label>
                    <Form.Control
                        className={styles.input}
                        type="text"
                        id="base-url-input"
                        aria-describedby="base-url-help"
                        placeholder="http://localhost:3000"
                        value={config["general.base-url"]}
                        onChange={e => setNewConfig({ ...config, "general.base-url": e.target.value })} />
                    <Form.Text id="base-url-help" muted>
                        What is the base URL at which you access nzbdav? Make sure that Emby/Jellyfin can access this url. This is the URL they will connect to for streaming. All *.strm files will point to this URL.
                    </Form.Text>
                </Form.Group>
            </>}
            <hr />
            <Form.Group>
                <Form.Label htmlFor="ignored-file-extensions-input">Ignored File Extensions</Form.Label>
                <Form.Control
                    className={styles.input}
                    type="text"
                    id="ignored-file-extensions-input"
                    aria-describedby="ignored-file-extensions-help"
                    placeholder=".nfo, .par2, .sfv"
                    value={config["api.download-extension-blacklist"]}
                    onChange={e => setNewConfig({ ...config, "api.download-extension-blacklist": e.target.value })} />
                <Form.Text id="ignored-file-extensions-help" muted>
                    Files with these extensions will be ignored and not mounted onto the webdav when processing an nzb.
                </Form.Text>
            </Form.Group>
            <hr />
            <Form.Group>
                <Form.Label htmlFor="duplicate-nzb-behavior-input">Behavior for Duplicate NZBs</Form.Label>
                <Form.Select
                    className={styles.input}
                    aria-describedby="duplicate-nzb-behavior-help"
                    value={config["api.duplicate-nzb-behavior"]}
                    onChange={e => setNewConfig({ ...config, "api.duplicate-nzb-behavior": e.target.value })}
                >
                    <option value="increment">Download again with suffix (2)</option>
                    <option value="mark-failed">Mark the download as failed</option>
                </Form.Select>
                <Form.Text id="duplicate-nzb-behavior-help" muted>
                    When an NZB is added, a new folder is created on the webdav. What should be done when the download folder for an NZB already exists?
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
        || config["api.manual-category"] !== newConfig["api.manual-category"]
        || config["rclone.mount-dir"] !== newConfig["rclone.mount-dir"]
        || config["api.ensure-importable-video"] !== newConfig["api.ensure-importable-video"]
        || config["api.ensure-article-existence"] !== newConfig["api.ensure-article-existence"]
        || config["api.ignore-history-limit"] !== newConfig["api.ignore-history-limit"]
        || config["api.duplicate-nzb-behavior"] !== newConfig["api.duplicate-nzb-behavior"]
        || config["api.download-extension-blacklist"] !== newConfig["api.download-extension-blacklist"]
        || config["api.import-strategy"] !== newConfig["api.import-strategy"]
        || config["api.completed-downloads-dir"] !== newConfig["api.completed-downloads-dir"]
        || config["general.base-url"] !== newConfig["general.base-url"]
}

export function isSabnzbdSettingsValid(newConfig: Record<string, string>) {
    return isValidCategories(newConfig["api.categories"]);
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
