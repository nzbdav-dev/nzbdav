import { Button, Form } from "react-bootstrap";
import styles from "./usenet.module.css"
import { useCallback, useEffect, useState, type Dispatch, type SetStateAction } from "react";

type UsenetSettingsProps = {
    config: Record<string, string>
    setNewConfig: Dispatch<SetStateAction<Record<string, string>>>
    onReadyToSave: (isReadyToSave: boolean) => void
};

export function UsenetSettings({ config, setNewConfig, onReadyToSave }: UsenetSettingsProps) {
    const [isFetching, setIsFetching] = useState(false);
    const [isConnectionSuccessful, setIsConnectionSuccessful] = useState(false);
    const [testedConfig, setTestedConfig] = useState({});
    const isChangedSinceLastTest = isUsenetSettingsUpdated(config, testedConfig);

    const TestButtonLabel = isFetching ? "Testing Connection..."
        : !config["usenet.host"] ? "`Host` is required"
        : !config["usenet.port"] ? "`Port` is required"
        : !isPositiveInteger(config["usenet.port"]) ? "`Port` is invalid"
        : !config["usenet.user"] ? "`User` is required"
        : !config["usenet.pass"] ? "`Pass` is required"
        : !config["usenet.connections"] ? "`Max Connections` is required"
        : !config["usenet.connections-per-stream"] ? "`Connections Per Stream` is required"
        : !isPositiveInteger(config["usenet.connections"]) ? "`Max Connections` is invalid"
        : !config["usenet.connections-per-stream"] ? "`Connections Per Stream` is required"
        : !isPositiveInteger(config["usenet.connections-per-stream"]) ? "`Connections Per Stream` is invalid"
        : Number(config["usenet.connections-per-stream"]) > Number(config["usenet.connections"]) ? "`Connections Per Stream` is invalid"
        : !isChangedSinceLastTest && isConnectionSuccessful ? "Connected ✅"
        : !isChangedSinceLastTest && !isConnectionSuccessful ? "Test Connection ❌"
        : "Test Connection";
    const testButtonVariant = isFetching ? "secondary"
        : TestButtonLabel === "Connected ✅" ? "success"
        : TestButtonLabel.includes("Test Connection") ? "primary"
        : "danger";
    const IsTestButtonEnabled = TestButtonLabel == "Test Connection"
                             || TestButtonLabel == "Test Connection ❌";

    const isReadyToSave = isConnectionSuccessful && !isChangedSinceLastTest;
    useEffect(() => {
        onReadyToSave && onReadyToSave(isReadyToSave);
    }, [isReadyToSave])

    const onTestButtonClicked = useCallback(async () => {
        setIsFetching(true);
        const response = await fetch("/api/test-usenet-connection", {
            method: "POST",
            body: (() => {
                const form = new FormData();
                form.append("host", config["usenet.host"]);
                form.append("port", config["usenet.port"]);
                form.append("use-ssl", config["usenet.use-ssl"] || "false");
                form.append("user", config["usenet.user"]);
                form.append("pass", config["usenet.pass"]);
                return form;
            })()
        });
        const isConnectionSuccessful = response.ok && ((await response.json())?.connected === true);
        setIsFetching(false);
        setTestedConfig(config);
        setIsConnectionSuccessful(isConnectionSuccessful);
    }, [config, setIsFetching, setIsConnectionSuccessful]);

    return (
        <div className={styles.container}>

            <Form.Group className={styles["form-group"]}>
                <Form.Label>Host</Form.Label>
                <Form.Control
                    type="text"
                    className={styles.input}
                    value={config["usenet.host"] || ""}
                    onChange={e => setNewConfig({ ...config, "usenet.host": e.target.value })} />
            </Form.Group>

            <Form.Group className={styles["form-group"]}>
                <Form.Label>Port</Form.Label>
                <Form.Control
                    type="text"
                    className={styles.input}
                    value={config["usenet.port"] || ""}
                    onChange={e => setNewConfig({ ...config, "usenet.port": e.target.value })} />
            </Form.Group>

            <div className={styles["justify-right"]}>
                <Form.Check
                    type="checkbox"
                    label={`Use SSL`}
                    checked={config["usenet.use-ssl"] === "true"}
                    onChange={e => setNewConfig({ ...config, "usenet.use-ssl": "" + e.target.checked })} />
            </div>

            <br />

            <Form.Group className={styles["form-group"]}>
                <Form.Label>User</Form.Label>
                <Form.Control
                    type="text"
                    className={styles.input}
                    value={config["usenet.user"] || ""}
                    onChange={e => setNewConfig({ ...config, "usenet.user": e.target.value })} />
            </Form.Group>

            <Form.Group className={styles["form-group"]}>
                <Form.Label>Pass</Form.Label>
                <Form.Control
                    className={styles.input}
                    type="password"
                    value={config["usenet.pass"] || ""}
                    onChange={e => setNewConfig({ ...config, "usenet.pass": e.target.value })} />
            </Form.Group>

            <Form.Group className={styles["form-group"]}>
                <Form.Label>Max Connections</Form.Label>
                <Form.Control
                    className={styles.input}
                    type="text"
                    placeholder="50"
                    value={config["usenet.connections"] || ""}
                    onChange={e => setNewConfig({ ...config, "usenet.connections": e.target.value })} />
            </Form.Group>

            <Form.Group className={styles["form-group"]}>
                <Form.Label>Connections Per Stream</Form.Label>
                <Form.Control
                    className={styles.input}
                    type="text"
                    placeholder="5"
                    value={config["usenet.connections-per-stream"] || ""}
                    onChange={e => setNewConfig({ ...config, "usenet.connections-per-stream": e.target.value })} />
            </Form.Group>

            <div className={styles["justify-right"]}>
                <Button
                    className={styles["test-connection-button"]}
                    variant={testButtonVariant}
                    disabled={!IsTestButtonEnabled}
                    onClick={() => onTestButtonClicked()}>
                    {TestButtonLabel}
                </Button>
            </div>
        </div>
    );
}

export function isUsenetSettingsUpdated(config: Record<string, string>, newConfig: Record<string, string>) {
    return config["usenet.host"] !== newConfig["usenet.host"]
        || config["usenet.port"] !== newConfig["usenet.port"]
        || config["usenet.use-ssl"] !== newConfig["usenet.use-ssl"]
        || config["usenet.user"] !== newConfig["usenet.user"]
        || config["usenet.pass"] !== newConfig["usenet.pass"]
        || config["usenet.connections"] !== newConfig["usenet.connections"]
        || config["usenet.connections-per-stream"] !== newConfig["usenet.connections-per-stream"]
}

export function isPositiveInteger(value: string) {
    const num = Number(value);
    return Number.isInteger(num) && num > 0 && value.trim() === num.toString();
}