import { Button, Form, Card, InputGroup, Spinner } from "react-bootstrap";
import styles from "./arrs.module.css"
import { type Dispatch, type SetStateAction, useState, useCallback, useEffect } from "react";

type ArrsSettingsProps = {
    config: Record<string, string>
    setNewConfig: Dispatch<SetStateAction<Record<string, string>>>
};

interface ConnectionDetails {
    Host: string;
    ApiKey: string;
}

interface QueueRule {
    Message: string;
    Action: number;
}

interface ArrConfig {
    RadarrInstances: ConnectionDetails[];
    SonarrInstances: ConnectionDetails[];
    QueueRules: QueueRule[];
}

const queueStatusMessages = [
    {
        display: "Found matching series via grab history, but release was matched to series by ID. Automatic import is not possible.",
        searchTerm: "Found matching series via grab history, but release was matched to series by ID. Automatic import is not possible."
    },
    {
        display: "Found matching movie via grab history, but release was matched to movie by ID. Manual Import required.",
        searchTerm: "Found matching movie via grab history, but release was matched to movie by ID. Manual Import required."
    },
    {
        display: "Episode was not found in the grabbed release",
        searchTerm: "was not found in the grabbed release"
    },
    {
        display: "Not an upgrade for existing episode file(s)",
        searchTerm: "Not an upgrade for existing episode file(s)"
    },
    {
        display: "Not an upgrade for existing movie file",
        searchTerm: "Not an upgrade for existing movie file"
    },
    {
        display: "No files found are eligible for import",
        searchTerm: "No files found are eligible for import"
    },
    {
        display: "Episode file already imported",
        searchTerm: "Episode file already imported"
    },
    {
        display: "No audio tracks detected",
        searchTerm: "No audio tracks detected"
    },
    {
        display: "Unable to determine if file is a sample",
        searchTerm: "Unable to determine if file is a sample"
    },
    {
        display: "Sample",
        searchTerm: "Sample"
    },
];

export function ArrsSettings({ config, setNewConfig }: ArrsSettingsProps) {
    const arrConfig = JSON.parse(config["arr.instances"]);

    const updateConfig = useCallback((newArrConfig: ArrConfig) => {
        setNewConfig({ ...config, "arr.instances": JSON.stringify(newArrConfig) });
    }, [config, setNewConfig]);

    const addRadarrInstance = useCallback(() => {
        updateConfig({
            ...arrConfig,
            RadarrInstances: [
                ...arrConfig.RadarrInstances,
                { Host: "", ApiKey: "" }
            ]
        });
    }, [arrConfig, updateConfig]);

    const removeRadarrInstance = useCallback((index: number) => {
        updateConfig({
            ...arrConfig,
            RadarrInstances: arrConfig.RadarrInstances
                .filter((_: any, i: number) => i !== index)
        });
    }, [arrConfig, updateConfig]);

    const updateRadarrInstance = useCallback((index: number, field: keyof ConnectionDetails, value: string) => {
        updateConfig({
            ...arrConfig,
            RadarrInstances: arrConfig.RadarrInstances
                .map((instance: any, i: number) =>
                    i === index ? { ...instance, [field]: value } : instance
                )
        });
    }, [arrConfig, updateConfig]);

    const addSonarrInstance = useCallback(() => {
        updateConfig({
            ...arrConfig,
            SonarrInstances: [
                ...arrConfig.SonarrInstances,
                { Host: "", ApiKey: "" }
            ]
        });
    }, [arrConfig, updateConfig]);

    const removeSonarrInstance = useCallback((index: number) => {
        updateConfig({
            ...arrConfig,
            SonarrInstances: arrConfig.SonarrInstances
                .filter((_: any, i: number) => i !== index)
        });
    }, [arrConfig, updateConfig]);

    const updateSonarrInstance = useCallback((index: number, field: keyof ConnectionDetails, value: string) => {
        updateConfig({
            ...arrConfig,
            SonarrInstances: arrConfig.SonarrInstances
                .map((instance: any, i: number) =>
                    i === index ? { ...instance, [field]: value } : instance
                )
        });
    }, [arrConfig, updateConfig]);

    const updateQueueAction = useCallback((searchTerm: string, action: number) => {
        // update the queue rule if it already exists
        var newQueueRules = (arrConfig.QueueRules || [])
            .filter((queueRule: QueueRule) => queueStatusMessages.map(x => x.searchTerm).includes(queueRule.Message))
            .map((queueRule: QueueRule) => queueRule.Message == searchTerm
                ? { Message: searchTerm, Action: action }
                : queueRule
            );

        // add the new queue rule if it doesn't already exist
        if (!newQueueRules.find((queueRule: QueueRule) => queueRule.Message == searchTerm))
            newQueueRules.push({ Message: searchTerm, Action: action });

        // update the config
        updateConfig({
            ...arrConfig,
            QueueRules: newQueueRules
        })
    }, [arrConfig, updateConfig])


    return (
        <div className={styles.container}>
            <div className={styles.section}>
                <div className={styles.sectionHeader}>
                    <div>Radarr Instances</div>
                    <Button variant="primary" size="sm" onClick={addRadarrInstance}>
                        Add
                    </Button>
                </div>
                {arrConfig.RadarrInstances.length === 0 ? (
                    <p className={styles.alertMessage}>No Radarr instances configured. Click on the "Add" button to get started.</p>
                ) : (
                    arrConfig.RadarrInstances.map((instance: any, index: number) =>
                        <InstanceForm
                            key={index}
                            instance={instance}
                            index={index}
                            type="radarr"
                            onUpdate={updateRadarrInstance}
                            onRemove={removeRadarrInstance}
                        />
                    )
                )}
            </div>
            <hr />
            <div className={styles.section}>
                <div className={styles.sectionHeader}>
                    <div>Sonarr Instances</div>
                    <Button variant="primary" size="sm" onClick={addSonarrInstance}>
                        Add
                    </Button>
                </div>
                {arrConfig.SonarrInstances.length === 0 ? (
                    <p className={styles.alertMessage}>No Sonarr instances configured. Click on the "Add" button to get started.</p>
                ) : (
                    arrConfig.SonarrInstances.map((instance: any, index: number) =>
                        <InstanceForm
                            key={index}
                            instance={instance}
                            index={index}
                            type="sonarr"
                            onUpdate={updateSonarrInstance}
                            onRemove={removeSonarrInstance}
                        />
                    )
                )}
            </div>
            <hr />
            <div className={styles.section}>
                <div className={styles.sectionHeader}>
                    <div>Automatic Queue Management</div>
                </div>
                <p className={styles.alertMessage}>
                    Configure what to do for items stuck in Radarr / Sonarr queues.
                    Different actions can be configured for different status messages.
                    Only `usenet` queue items will be acted upon.
                </p>
                <ul>
                    {queueStatusMessages.map((queueStatusMessage, index) =>
                        <li key={index} className={styles.listItem}>
                            <div className={styles.statusMessage}>{queueStatusMessage.display}</div>
                            <Form.Select
                                className={styles.input}
                                value={arrConfig.QueueRules.find((x: QueueRule) => x.Message == queueStatusMessage.searchTerm)?.Action ?? "0"}
                                onChange={e => updateQueueAction(queueStatusMessage.searchTerm, Number(e.target.value))}
                            >
                                <option value="0">Do Nothing</option>
                                <option value="1">Remove</option>
                                <option value="2">Remove and Blocklist</option>
                                <option value="3">Remove, Blocklist, and Search</option>
                            </Form.Select>
                        </li>
                    )}
                </ul>
            </div>
        </div>
    );
}

interface InstanceFormProps {
    instance: ConnectionDetails;
    index: number;
    type: 'radarr' | 'sonarr';
    onUpdate: (index: number, field: keyof ConnectionDetails, value: string) => void;
    onRemove: (index: number) => void;
}

function InstanceForm({ instance, index, type, onUpdate, onRemove }: InstanceFormProps) {
    const [connectionState, setConnectionState] = useState<'idle' | 'testing' | 'success' | 'error'>('idle');

    useEffect(() => {
        setConnectionState('idle');
    }, [instance.Host, instance.ApiKey]);

    const testConnection = useCallback(async (host: string, apiKey: string) => {
        if (!host.trim() || !apiKey.trim()) {
            return;
        }

        setConnectionState('testing');

        try {
            const formData = new FormData();
            formData.append('host', host);
            formData.append('apiKey', apiKey);

            const response = await fetch('/api/test-arr-connection', {
                method: 'POST',
                body: formData
            });

            const result = await response.json();

            if (result.status && result.connected) {
                setConnectionState('success');
            } else {
                setConnectionState('error');
            }
        } catch (error) {
            setConnectionState('error');
        }
    }, []);

    return (
        <Card className={styles.instanceCard}>
            <button
                className={styles.closeButton}
                onClick={() => onRemove(index)}
                aria-label="Remove instance"
            >
                ×
            </button>
            <Card.Body>
                <Form.Group>
                    <Form.Label>Host</Form.Label>
                    <InputGroup className={styles.input}>
                        <Form.Control
                            type="text"
                            placeholder={type === "radarr" ? "http://localhost:7878" : "http://localhost:8989"}
                            value={instance.Host}
                            onChange={e => onUpdate(index, 'Host', e.target.value)} />
                        {instance.Host.trim() && instance.ApiKey.trim() && (
                            <Button
                                variant={connectionState === 'success' ? 'success' :
                                    connectionState === 'error' ? 'danger' : 'secondary'}
                                onClick={() => testConnection(instance.Host, instance.ApiKey)}
                                disabled={connectionState === 'testing'}
                                className={styles.testButton}
                            >
                                {
                                    connectionState === 'testing' ? (
                                        <Spinner animation="border" size="sm" />
                                    ) : connectionState === 'success' ? (
                                        <span style={{ color: 'white' }}>✓</span>
                                    ) : connectionState === 'error' ? (
                                        <span style={{ color: 'white' }}>✗</span>
                                    ) : ('?')
                                }
                            </Button>
                        )}
                    </InputGroup>
                </Form.Group>
                <Form.Group>
                    <Form.Label>API Key</Form.Label>
                    <Form.Control
                        type="password"
                        className={styles.input}
                        value={instance.ApiKey}
                        onChange={e => onUpdate(index, 'ApiKey', e.target.value)} />
                </Form.Group>
            </Card.Body>
        </Card>
    );
}

export function isArrsSettingsUpdated(config: Record<string, string>, newConfig: Record<string, string>) {
    return config["arr.instances"] !== newConfig["arr.instances"];
}

export function isArrsSettingsValid(newConfig: Record<string, string>) {
    try {
        const arrConfig: ArrConfig = JSON.parse(newConfig["arr.instances"] || "{}");

        // Validate all Radarr instances
        for (const instance of arrConfig.RadarrInstances || []) {
            if (!isValidHost(instance.Host) || !isValidApiKey(instance.ApiKey)) {
                return false;
            }
        }

        // Validate all Sonarr instances
        for (const instance of arrConfig.SonarrInstances || []) {
            if (!isValidHost(instance.Host) || !isValidApiKey(instance.ApiKey)) {
                return false;
            }
        }

        return true;
    } catch {
        return false;
    }
}

function isValidHost(host: string): boolean {
    if (host.trim().length === 0) return false;
    try {
        new URL(host);
        return true;
    } catch {
        return false;
    }
}

function isValidApiKey(apiKey: string): boolean {
    return apiKey.trim().length > 0;
}