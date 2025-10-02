import { Form } from "react-bootstrap";
import styles from "./library.module.css"
import { type Dispatch, type SetStateAction } from "react";

type LibrarySettingsProps = {
    savedConfig: Record<string, string>
    config: Record<string, string>
    setNewConfig: Dispatch<SetStateAction<Record<string, string>>>
};

export function LibrarySettings({ savedConfig, config, setNewConfig }: LibrarySettingsProps) {
    return (
        <div className={styles.container}>
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
                    The path to your organized media library that contains all your imported symlinks.
                    Make sure this path is visible to your NzbDAV container.
                </Form.Text>
            </Form.Group>
        </div>
    );
}

export function isLibrarySettingsUpdated(config: Record<string, string>, newConfig: Record<string, string>) {
    return config["media.library-dir"] !== newConfig["media.library-dir"]
}