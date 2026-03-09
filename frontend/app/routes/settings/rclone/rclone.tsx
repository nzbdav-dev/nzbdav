import { Form } from "react-bootstrap";
import styles from "./rclone.module.css"
import { type Dispatch, type SetStateAction } from "react";

type RcloneSettingsProps = {
    config: Record<string, string>
    setNewConfig: Dispatch<SetStateAction<Record<string, string>>>
};

export function RcloneSettings({ config, setNewConfig }: RcloneSettingsProps) {
    return (
        <div className={styles.container}>
            <Form.Group>
                <Form.Check
                    className={styles.input}
                    type="checkbox"
                    id="rclone-rc-enabled-checkbox"
                    aria-describedby="rclone-rc-enabled-help"
                    label={`Enable Rclone RC Server Notifications`}
                    checked={config["rclone.rc-enabled"] === "true"}
                    onChange={e => setNewConfig({ ...config, "rclone.rc-enabled": "" + e.target.checked })} />
                <Form.Text id="rclone-rc-enabled-help" muted>
                    When enabled, nzbdav will automatically notify your rclone mount via the RC API whenever files are added or removed on the webdav. This allows setting a high dir-cache-time setting on Rclone.
                </Form.Text>
            </Form.Group>
            <hr />
            <Form.Group>
                <Form.Label htmlFor="rclone-host-input">RC Host</Form.Label>
                <Form.Control
                    className={styles.input}
                    type="text"
                    id="rclone-host-input"
                    aria-describedby="rclone-host-help"
                    placeholder="http://localhost:5572"
                    value={config["rclone.host"]}
                    onChange={e => setNewConfig({ ...config, "rclone.host": e.target.value })} />
                <Form.Text id="rclone-host-help" muted>
                    The host address of the rclone RC API.
                </Form.Text>
            </Form.Group>
            <hr />
            <Form.Group>
                <Form.Label htmlFor="rclone-user-input">RC User</Form.Label>
                <Form.Control
                    className={styles.input}
                    type="text"
                    id="rclone-user-input"
                    aria-describedby="rclone-user-help"
                    value={config["rclone.user"]}
                    onChange={e => setNewConfig({ ...config, "rclone.user": e.target.value })} />
                <Form.Text id="rclone-user-help" muted>
                    The username for rclone RC API authentication.
                </Form.Text>
            </Form.Group>
            <hr />
            <Form.Group>
                <Form.Label htmlFor="rclone-pass-input">RC Password</Form.Label>
                <Form.Control
                    className={styles.input}
                    type="password"
                    id="rclone-pass-input"
                    aria-describedby="rclone-pass-help"
                    value={config["rclone.pass"]}
                    onChange={e => setNewConfig({ ...config, "rclone.pass": e.target.value })} />
                <Form.Text id="rclone-pass-help" muted>
                    The password for rclone RC API authentication.
                </Form.Text>
            </Form.Group>
        </div>
    );
}

export function isRcloneSettingsUpdated(config: Record<string, string>, newConfig: Record<string, string>) {
    return config["rclone.rc-enabled"] !== newConfig["rclone.rc-enabled"]
        || config["rclone.host"] !== newConfig["rclone.host"]
        || config["rclone.user"] !== newConfig["rclone.user"]
        || config["rclone.pass"] !== newConfig["rclone.pass"];
}
