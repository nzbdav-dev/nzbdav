import { Alert, Button, Card, Form } from "react-bootstrap";
import styles from "./arr.module.css";
import { useState, type Dispatch, type SetStateAction } from "react";

type ArrInstance = {
    id: string;
    name: string;
    url: string;
    apiKey: string;
    type: 'radarr' | 'sonarr';
    isNew: boolean;
    testing: boolean;
    testResult?: 'success' | 'error' | null;
    testMessage?: string;
};

type ArrSettingsProps = {
    config: Record<string, string>;
    setNewConfig: Dispatch<SetStateAction<Record<string, string>>>;
};

export function ArrSettings({ config, setNewConfig }: ArrSettingsProps) {
    const [instances, setInstances] = useState<ArrInstance[]>(() => {
        const existingInstances: ArrInstance[] = [];
        
        // Load existing Radarr instances
        for (let i = 0; i < 10; i++) {
            const name = config[`radarr.${i}.name`];
            const url = config[`radarr.${i}.url`];
            const apiKey = config[`radarr.${i}.api_key`];
            
            if (name || url || apiKey) {
                existingInstances.push({
                    id: `radarr-${i}`,
                    name: name || '',
                    url: url || '',
                    apiKey: apiKey || '',
                    type: 'radarr',
                    isNew: false,
                    testing: false,
                    testResult: null
                });
            }
        }
        
        // Load existing Sonarr instances
        for (let i = 0; i < 10; i++) {
            const name = config[`sonarr.${i}.name`];
            const url = config[`sonarr.${i}.url`];
            const apiKey = config[`sonarr.${i}.api_key`];
            
            if (name || url || apiKey) {
                existingInstances.push({
                    id: `sonarr-${i}`,
                    name: name || '',
                    url: url || '',
                    apiKey: apiKey || '',
                    type: 'sonarr',
                    isNew: false,
                    testing: false,
                    testResult: null
                });
            }
        }
        
        return existingInstances;
    });

    const addInstance = (type: 'radarr' | 'sonarr') => {
        const existingIndices = instances
            .filter(inst => inst.type === type)
            .map(inst => parseInt(inst.id.split('-')[1]))
            .filter(idx => !isNaN(idx));
        
        const nextIndex = existingIndices.length > 0 ? Math.max(...existingIndices) + 1 : 0;
        
        const newInstance: ArrInstance = {
            id: `${type}-${nextIndex}`,
            name: '',
            url: '',
            apiKey: '',
            type,
            isNew: true,
            testing: false,
            testResult: null
        };
        
        setInstances([...instances, newInstance]);
    };

    const updateInstance = (id: string, field: keyof ArrInstance, value: string) => {
        setInstances(instances.map(inst => 
            inst.id === id ? { ...inst, [field]: value } : inst
        ));
        
        // Update config - use current newConfig state instead of original config
        const instance = instances.find(inst => inst.id === id);
        if (instance) {
            const index = parseInt(id.split('-')[1]);
            const configKey = `${instance.type}.${index}.${field === 'apiKey' ? 'api_key' : field}`;
            setNewConfig(prevConfig => ({
                ...prevConfig,
                [configKey]: value
            }));
        }
    };

    const removeInstance = (id: string) => {
        const instance = instances.find(inst => inst.id === id);
        if (instance) {
            const index = parseInt(id.split('-')[1]);
            
            setNewConfig(prevConfig => {
                const updatedConfig = { ...prevConfig };
                
                // Clear config values
                delete updatedConfig[`${instance.type}.${index}.name`];
                delete updatedConfig[`${instance.type}.${index}.url`];
                delete updatedConfig[`${instance.type}.${index}.api_key`];
                
                return updatedConfig;
            });
        }
        
        setInstances(instances.filter(inst => inst.id !== id));
    };

    const testConnection = async (id: string) => {
        const instance = instances.find(inst => inst.id === id);
        if (!instance || !instance.url || !instance.apiKey) return;

        setInstances(instances.map(inst => 
            inst.id === id ? { ...inst, testing: true, testResult: null } : inst
        ));

        try {
            // Test connection via backend API to avoid CORS issues
            const response = await fetch("/api/test-arr-connection", {
                method: "POST",
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    appType: instance.type,
                    url: instance.url,
                    apiKey: instance.apiKey,
                    name: instance.name || `${instance.type}-test`
                })
            });

            const data = await response.json();

            if (response.ok && data.success) {
                setInstances(instances.map(inst => 
                    inst.id === id ? {
                        ...inst,
                        testing: false,
                        testResult: 'success',
                        testMessage: data.message
                    } : inst
                ));
            } else {
                setInstances(instances.map(inst => 
                    inst.id === id ? {
                        ...inst,
                        testing: false,
                        testResult: 'error',
                        testMessage: data.message || `Connection failed: ${response.status} ${response.statusText}`
                    } : inst
                ));
            }
        } catch (error) {
            setInstances(instances.map(inst => 
                inst.id === id ? {
                    ...inst,
                    testing: false,
                    testResult: 'error',
                    testMessage: `Connection failed: ${error instanceof Error ? error.message : 'Unknown error'}`
                } : inst
            ));
        }
    };

    const radarrInstances = instances.filter(inst => inst.type === 'radarr');
    const sonarrInstances = instances.filter(inst => inst.type === 'sonarr');
    
    // Check if library directory is configured
    const libraryDir = config["media.library-dir"];
    const hasLibraryDir = libraryDir && libraryDir.trim() !== "";

    return (
        <div className={styles.container}>
            {!hasLibraryDir && (
                <Alert variant="danger">
                    <strong>Library Directory Required:</strong> You must configure the Library Directory in the Library tab before setting up Radarr/Sonarr integration. 
                    The integrity checker needs to know where your media files are located to work with Radarr and Sonarr.
                </Alert>
            )}
            
            <Alert variant="info">
                Configure your Radarr and Sonarr instances here. These will be used for automatic deletion of corrupt files when integrity checking is enabled with "Delete via Radarr/Sonarr" action.
                {hasLibraryDir && (
                    <><br/><br/><strong>Library Directory:</strong> {libraryDir}</>
                )}
            </Alert>

            {/* Radarr Section */}
            <div className={styles.section}>
                <div className={styles.sectionHeader}>
                    <h5>Radarr Instances</h5>
                    <Button 
                        variant="primary" 
                        size="sm"
                        onClick={() => addInstance('radarr')}
                        className={styles.addButton}>
                        + Add Radarr Instance
                    </Button>
                </div>

                {radarrInstances.length === 0 && (
                    <div className={styles.emptyState}>
                        No Radarr instances configured. Click "Add Radarr Instance" to get started.
                    </div>
                )}

                {radarrInstances.map((instance) => (
                    <Card key={instance.id} className={styles.instanceCard}>
                        <Card.Body>
                            <div className={styles.instanceHeader}>
                                <h6>Radarr Instance</h6>
                                <Button 
                                    variant="outline-danger" 
                                    size="sm"
                                    onClick={() => removeInstance(instance.id)}>
                                    Remove
                                </Button>
                            </div>

                            <Form.Group>
                                <Form.Label>Instance Name</Form.Label>
                                <Form.Control
                                    type="text"
                                    placeholder="Main Radarr"
                                    value={instance.name}
                                    onChange={e => updateInstance(instance.id, 'name', e.target.value)} />
                            </Form.Group>

                            <Form.Group>
                                <Form.Label>Base URL</Form.Label>
                                <Form.Control
                                    type="url"
                                    placeholder="http://localhost:7878"
                                    value={instance.url}
                                    onChange={e => updateInstance(instance.id, 'url', e.target.value)} />
                            </Form.Group>

                            <Form.Group>
                                <Form.Label>API Key</Form.Label>
                                <Form.Control
                                    type="text"
                                    placeholder="Your Radarr API key"
                                    value={instance.apiKey}
                                    onChange={e => updateInstance(instance.id, 'apiKey', e.target.value)} />
                                <Form.Text muted>
                                    Find this in Radarr under Settings → General → Security
                                </Form.Text>
                            </Form.Group>

                            <div className={styles.testSection}>
                                <Button 
                                    variant="secondary"
                                    size="sm"
                                    disabled={!instance.url || !instance.apiKey || instance.testing}
                                    onClick={() => testConnection(instance.id)}>
                                    {instance.testing ? 'Testing...' : 'Test Connection'}
                                </Button>

                                {instance.testResult && (
                                    <Alert 
                                        variant={instance.testResult === 'success' ? 'success' : 'danger'}
                                        className={styles.testResult}>
                                        {instance.testMessage}
                                    </Alert>
                                )}
                            </div>
                        </Card.Body>
                    </Card>
                ))}
            </div>

            {/* Sonarr Section */}
            <div className={styles.section}>
                <div className={styles.sectionHeader}>
                    <h5>Sonarr Instances</h5>
                    <Button 
                        variant="primary" 
                        size="sm"
                        onClick={() => addInstance('sonarr')}
                        className={styles.addButton}>
                        + Add Sonarr Instance
                    </Button>
                </div>

                {sonarrInstances.length === 0 && (
                    <div className={styles.emptyState}>
                        No Sonarr instances configured. Click "Add Sonarr Instance" to get started.
                    </div>
                )}

                {sonarrInstances.map((instance) => (
                    <Card key={instance.id} className={styles.instanceCard}>
                        <Card.Body>
                            <div className={styles.instanceHeader}>
                                <h6>Sonarr Instance</h6>
                                <Button 
                                    variant="outline-danger" 
                                    size="sm"
                                    onClick={() => removeInstance(instance.id)}>
                                    Remove
                                </Button>
                            </div>

                            <Form.Group>
                                <Form.Label>Instance Name</Form.Label>
                                <Form.Control
                                    type="text"
                                    placeholder="Main Sonarr"
                                    value={instance.name}
                                    onChange={e => updateInstance(instance.id, 'name', e.target.value)} />
                            </Form.Group>

                            <Form.Group>
                                <Form.Label>Base URL</Form.Label>
                                <Form.Control
                                    type="url"
                                    placeholder="http://localhost:8989"
                                    value={instance.url}
                                    onChange={e => updateInstance(instance.id, 'url', e.target.value)} />
                            </Form.Group>

                            <Form.Group>
                                <Form.Label>API Key</Form.Label>
                                <Form.Control
                                    type="text"
                                    placeholder="Your Sonarr API key"
                                    value={instance.apiKey}
                                    onChange={e => updateInstance(instance.id, 'apiKey', e.target.value)} />
                                <Form.Text muted>
                                    Find this in Sonarr under Settings → General → Security
                                </Form.Text>
                            </Form.Group>

                            <div className={styles.testSection}>
                                <Button 
                                    variant="secondary"
                                    size="sm"
                                    disabled={!instance.url || !instance.apiKey || instance.testing}
                                    onClick={() => testConnection(instance.id)}>
                                    {instance.testing ? 'Testing...' : 'Test Connection'}
                                </Button>

                                {instance.testResult && (
                                    <Alert 
                                        variant={instance.testResult === 'success' ? 'success' : 'danger'}
                                        className={styles.testResult}>
                                        {instance.testMessage}
                                    </Alert>
                                )}
                            </div>
                        </Card.Body>
                    </Card>
                ))}
            </div>
        </div>
    );
}

export function isArrSettingsUpdated(config: Record<string, string>, newConfig: Record<string, string>) {
    // Check all possible radarr and sonarr configuration keys
    const arrKeys = [];
    for (let i = 0; i < 10; i++) {
        arrKeys.push(
            `radarr.${i}.name`,
            `radarr.${i}.url`,
            `radarr.${i}.api_key`,
            `sonarr.${i}.name`,
            `sonarr.${i}.url`,
            `sonarr.${i}.api_key`
        );
    }
    
    return arrKeys.some(key => config[key] !== newConfig[key]);
}
