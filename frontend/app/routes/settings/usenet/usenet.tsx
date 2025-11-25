import styles from "./usenet.module.css"
import { type Dispatch, type SetStateAction, useState, useCallback, useEffect, useMemo } from "react";
import { Button } from "react-bootstrap";
import { receiveMessage } from "~/utils/websocket-util";

const usenetConnectionsTopic = {'cxs': 'state'};

type UsenetSettingsProps = {
    config: Record<string, string>
    setNewConfig: Dispatch<SetStateAction<Record<string, string>>>
};

enum ProviderType {
    Disabled = 0,
    Pooled = 1,
    BackupAndStats = 2,
    BackupOnly = 3,
}

type ConnectionDetails = {
    Type: ProviderType;
    Host: string;
    Port: number;
    UseSsl: boolean;
    User: string;
    Pass: string;
    MaxConnections: number;
};

type ConnectionCounts = {
    live: number;
    active: number;
    max: number;
}

type UsenetProviderConfig = {
    Providers: ConnectionDetails[];
};

const PROVIDER_TYPE_LABELS: Record<ProviderType, string> = {
    [ProviderType.Disabled]: "Disabled",
    [ProviderType.Pooled]: "Pool Connections",
    [ProviderType.BackupAndStats]: "Backup & Health Checks",
    [ProviderType.BackupOnly]: "Backup Only",
};

function parseProviderConfig(jsonString: string): UsenetProviderConfig {
    try {
        if (!jsonString || jsonString.trim() === "") {
            return { Providers: [] };
        }
        return JSON.parse(jsonString);
    } catch {
        return { Providers: [] };
    }
}

function serializeProviderConfig(config: UsenetProviderConfig): string {
    return JSON.stringify(config);
}

export function UsenetSettings({ config, setNewConfig }: UsenetSettingsProps) {
    // state
    const [showModal, setShowModal] = useState(false);
    const [editingIndex, setEditingIndex] = useState<number | null>(null);
    const [connections, setConnections] = useState<{[index: number]: ConnectionCounts}>({});
    const providerConfig = useMemo(() => parseProviderConfig(config["usenet.providers"]), [config]);

    // handlers
    const handleAddProvider = useCallback(() => {
        setEditingIndex(null);
        setShowModal(true);
    }, []);

    const handleEditProvider = useCallback((index: number) => {
        setEditingIndex(index);
        setShowModal(true);
    }, []);

    const handleDeleteProvider = useCallback((index: number) => {
        const newProviderConfig = { ...providerConfig };
        newProviderConfig.Providers = providerConfig.Providers.filter((_, i) => i !== index);
        setNewConfig({ ...config, "usenet.providers": serializeProviderConfig(newProviderConfig) });
    }, [config, providerConfig, setNewConfig]);

    const handleCloseModal = useCallback(() => {
        setShowModal(false);
        setEditingIndex(null);
    }, []);

    const handleSaveProvider = useCallback((provider: ConnectionDetails) => {
        const newProviderConfig = { ...providerConfig };
        if (editingIndex !== null) {
            newProviderConfig.Providers[editingIndex] = provider;
        } else {
            newProviderConfig.Providers.push(provider);
        }
        setNewConfig({ ...config, "usenet.providers": serializeProviderConfig(newProviderConfig) });
        handleCloseModal();
    }, [config, providerConfig, editingIndex, setNewConfig, handleCloseModal]);

    const handleConnectionsMessage = useCallback((message: string) => {
        const parts = (message || "0|0|0|0|1|0").split("|");
        const [index, live, idle, _0, _1, _2] = parts.map((x: any) => Number(x));
        if (showModal) return;
        if (index >= providerConfig.Providers.length) return;
        setConnections(prev => ({...prev, [index]: {
            active: live - idle,
            live: live,
            max: providerConfig.Providers[index]?.MaxConnections || 1
        }}));
    }, [setConnections]);

    // effects
    useEffect(() => {
        let ws: WebSocket;
        let disposed = false;
        function connect() {
            ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
            ws.onmessage = receiveMessage((_, message) => handleConnectionsMessage(message));
            ws.onopen = () => ws.send(JSON.stringify(usenetConnectionsTopic));
            ws.onerror = () => { ws.close() };
            ws.onclose = onClose;
            return () => { disposed = true; ws.close(); }
        }
        function onClose(e: CloseEvent) {
            !disposed && setTimeout(() => connect(), 1000);
            setConnections({});
        }
        return connect();
    }, [setConnections, handleConnectionsMessage]);

    // view
    return (
        <div className={styles.container}>
            <div className={styles.section}>
                <div className={styles.sectionHeader}>
                    <div>Usenet Providers</div>
                    <Button variant="primary" size="sm" onClick={handleAddProvider}>
                        Add
                    </Button>
                </div>
                {providerConfig.Providers.length === 0 ? (
                    <p className={styles.alertMessage}>
                        No Usenet providers configured.
                        Click on the "Add" button to get started.
                    </p>
                ) : (
                    <div className={styles["providers-grid"]}>
                        {providerConfig.Providers.map((provider, index) => (
                            <div key={index} className={styles["provider-card"]}>
                                <div className={styles["provider-card-inner"]}>
                                    <div className={styles["provider-header"]}>
                                        <div className={styles["provider-header-content"]}>
                                            <div className={styles["provider-host"]}>
                                                {provider.Host}
                                            </div>
                                            <div className={styles["provider-port"]}>
                                                Port {provider.Port}
                                            </div>
                                        </div>
                                        <div className={styles["provider-header-actions"]}>
                                            <button
                                                className={styles["header-action-button"]}
                                                onClick={() => handleEditProvider(index)}
                                                title="Edit Provider"
                                            >
                                                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                                                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                                                </svg>
                                            </button>
                                            <button
                                                className={`${styles["header-action-button"]} ${styles["delete"]}`}
                                                onClick={() => handleDeleteProvider(index)}
                                                title="Delete Provider"
                                            >
                                                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                                    <polyline points="3 6 5 6 21 6" />
                                                    <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
                                                </svg>
                                            </button>
                                        </div>
                                    </div>

                                    <div className={styles["provider-details"]}>
                                        <div className={styles["provider-detail-row"]}>

                                            <div className={styles["provider-detail-item"]}>
                                                <div className={styles["provider-detail-icon"]}>
                                                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                                        <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
                                                        <circle cx="12" cy="7" r="4" />
                                                    </svg>
                                                </div>
                                                <div className={styles["provider-detail-content"]}>
                                                    <span className={styles["provider-detail-label"]}>Username</span>
                                                    <span className={styles["provider-detail-value"]}>{provider.User}</span>
                                                </div>
                                            </div>

                                            <div className={styles["provider-detail-item"]}>
                                                {connections[index] && (
                                                    <div className={styles["connection-bar"]}>
                                                        <div
                                                            className={styles["connection-bar-live"]}
                                                            style={{ width: `${100 * (connections[index].live / connections[index].max)}%` }}
                                                        />
                                                        <div
                                                            className={styles["connection-bar-active"]}
                                                            style={{ width: `${100 * (connections[index].active / connections[index].max)}%` }}
                                                        />
                                                    </div>
                                                )}
                                                <div className={styles["provider-detail-icon"]}>
                                                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                                        <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z" />
                                                    </svg>
                                                </div>
                                                <div className={styles["provider-detail-content"]}>
                                                    <span className={styles["provider-detail-label"]}>Max Connections</span>
                                                    <span className={styles["provider-detail-value"]}>{provider.MaxConnections}</span>
                                                </div>
                                            </div>

                                            <div className={styles["provider-detail-item"]}>
                                                <div className={styles["provider-detail-icon"]}>
                                                    {provider.UseSsl ? (
                                                        // Closed lock icon
                                                        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                                            <rect x="5" y="11" width="14" height="11" rx="2" ry="2" />
                                                            <path d="M7 11V7a5 5 0 0 1 10 0v4" />
                                                            <circle cx="12" cy="16" r="1" fill="currentColor" />
                                                        </svg>
                                                    ) : (
                                                        // Open lock icon
                                                        <svg width="13" height="13" viewBox="0 -2 24 26" fill="none" stroke="currentColor" strokeWidth="2">
                                                            <rect x="5" y="11" width="14" height="11" rx="2" ry="2" />
                                                            <path d="M7 11V4a5 5 0 0 1 9.9 1" />
                                                            <circle cx="12" cy="16" r="1" fill="currentColor" />
                                                        </svg>
                                                    )}
                                                </div>
                                                <div className={styles["provider-detail-content"]}>
                                                    <span className={styles["provider-detail-label"]}>Security</span>
                                                    <span className={styles["provider-detail-value"]}>
                                                        {provider.UseSsl ? "SSL Enabled" : "No SSL"}
                                                    </span>
                                                </div>
                                            </div>

                                            <div className={styles["provider-detail-item"]}>
                                                <div className={styles["provider-detail-icon"]}>
                                                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.3">
                                                        <text x="12" y="9" fontSize="10" fill="currentColor" textAnchor="middle" fontWeight="600">1</text>
                                                        <text x="6" y="21" fontSize="10" fill="currentColor" textAnchor="middle" fontWeight="600">2</text>
                                                        <text x="18" y="21" fontSize="10" fill="currentColor" textAnchor="middle" fontWeight="600">3</text>
                                                    </svg>
                                                </div>
                                                <div className={styles["provider-detail-content"]}>
                                                    <span className={styles["provider-detail-label"]}>Behavior</span>
                                                    <span className={styles["provider-detail-value"]}>{PROVIDER_TYPE_LABELS[provider.Type]}</span>
                                                </div>
                                            </div>

                                        </div>
                                    </div>
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </div>

            <ProviderModal
                show={showModal}
                provider={editingIndex !== null ? providerConfig.Providers[editingIndex] : null}
                onClose={handleCloseModal}
                onSave={handleSaveProvider}
            />
        </div>
    );
}

type ProviderModalProps = {
    show: boolean;
    provider: ConnectionDetails | null;
    onClose: () => void;
    onSave: (provider: ConnectionDetails) => void;
};

function ProviderModal({ show, provider, onClose, onSave }: ProviderModalProps) {
    const [host, setHost] = useState(provider?.Host || "");
    const [port, setPort] = useState(provider?.Port?.toString() || "");
    const [useSsl, setUseSsl] = useState(provider?.UseSsl ?? true);
    const [user, setUser] = useState(provider?.User || "");
    const [pass, setPass] = useState(provider?.Pass || "");
    const [maxConnections, setMaxConnections] = useState(provider?.MaxConnections?.toString() || "");
    const [type, setType] = useState<ProviderType>(provider?.Type ?? ProviderType.Pooled);
    const [isTestingConnection, setIsTestingConnection] = useState(false);
    const [connectionTested, setConnectionTested] = useState(false);
    const [testError, setTestError] = useState<string | null>(null);

    // Reset form when modal opens or provider changes
    useEffect(() => {
        if (show) {
            setHost(provider?.Host || "");
            setPort(provider?.Port?.toString() || "");
            setUseSsl(provider?.UseSsl ?? true);
            setUser(provider?.User || "");
            setPass(provider?.Pass || "");
            setMaxConnections(provider?.MaxConnections?.toString() || "");
            setType(provider?.Type ?? ProviderType.Pooled);
            setConnectionTested(false);
            setTestError(null);
        }
    }, [show, provider]);

    // Handle Escape key to close modal
    useEffect(() => {
        const handleEscape = (e: KeyboardEvent) => {
            if (e.key === 'Escape' && show) {
                onClose();
            }
        };

        if (show) {
            document.addEventListener('keydown', handleEscape);
            return () => document.removeEventListener('keydown', handleEscape);
        }
    }, [show, onClose]);

    const handleTestConnection = useCallback(async () => {
        setIsTestingConnection(true);
        setTestError(null);

        try {
            const formData = new FormData();
            formData.append('host', host);
            formData.append('port', port);
            formData.append('use-ssl', useSsl.toString());
            formData.append('user', user);
            formData.append('pass', pass);

            const response = await fetch('/api/test-usenet-connection', {
                method: 'POST',
                body: formData,
            });

            if (response.ok) {
                const data = await response.json();
                if (data.connected) {
                    setConnectionTested(true);
                    setTestError(null);
                } else {
                    setTestError("Connection test failed");
                }
            } else {
                setTestError("Failed to test connection");
            }
        } catch (error) {
            setTestError("Network error: " + (error instanceof Error ? error.message : "Unknown error"));
        } finally {
            setIsTestingConnection(false);
        }
    }, [host, port, useSsl, user, pass]);

    const handleSave = useCallback(() => {
        onSave({
            Type: type,
            Host: host,
            Port: parseInt(port, 10),
            UseSsl: useSsl,
            User: user,
            Pass: pass,
            MaxConnections: parseInt(maxConnections, 10),
        });
    }, [type, host, port, useSsl, user, pass, maxConnections, onSave]);

    const handleOverlayClick = useCallback((e: React.MouseEvent) => {
        if (e.target === e.currentTarget) {
            onClose();
        }
    }, [onClose]);

    const isFormValid = host.trim() !== ""
        && isPositiveInteger(port)
        && user.trim() !== ""
        && pass.trim() !== ""
        && isPositiveInteger(maxConnections);

    const canSave = isFormValid && connectionTested;

    if (!show) return null;

    return (
        <div className={styles["modal-overlay"]} onClick={handleOverlayClick}>
            <div className={styles["modal-container"]}>
                <div className={styles["modal-header"]}>
                    <h2 className={styles["modal-title"]}>
                        {provider ? "Edit Provider" : "Add Provider"}
                    </h2>
                    <button className={styles["modal-close"]} onClick={onClose} aria-label="Close">
                        ×
                    </button>
                </div>

                <div className={styles["modal-body"]}>
                    <div className={styles["form-grid"]}>
                        <div className={styles["form-group"]}>
                            <label htmlFor="provider-host" className={styles["form-label"]}>
                                Host
                            </label>
                            <input
                                type="text"
                                id="provider-host"
                                className={styles["form-input"]}
                                placeholder="news.provider.com"
                                value={host}
                                onChange={(e) => {
                                    setHost(e.target.value);
                                    setConnectionTested(false);
                                }}
                            />
                        </div>

                        <div className={styles["form-group"]}>
                            <label htmlFor="provider-port" className={styles["form-label"]}>
                                Port
                            </label>
                            <input
                                type="text"
                                id="provider-port"
                                className={`${styles["form-input"]} ${!isPositiveInteger(port) && port !== "" ? styles.error : ""}`}
                                placeholder="563"
                                value={port}
                                onChange={(e) => {
                                    setPort(e.target.value);
                                    setConnectionTested(false);
                                }}
                            />
                        </div>

                        <div className={styles["form-group"]}>
                            <label htmlFor="provider-user" className={styles["form-label"]}>
                                Username
                            </label>
                            <input
                                type="text"
                                id="provider-user"
                                className={styles["form-input"]}
                                placeholder="username"
                                value={user}
                                onChange={(e) => {
                                    setUser(e.target.value);
                                    setConnectionTested(false);
                                }}
                            />
                        </div>

                        <div className={styles["form-group"]}>
                            <label htmlFor="provider-pass" className={styles["form-label"]}>
                                Password
                            </label>
                            <input
                                type="password"
                                id="provider-pass"
                                className={styles["form-input"]}
                                placeholder="password"
                                value={pass}
                                onChange={(e) => {
                                    setPass(e.target.value);
                                    setConnectionTested(false);
                                }}
                            />
                        </div>

                        <div className={styles["form-group"]}>
                            <label htmlFor="provider-max-connections" className={styles["form-label"]}>
                                Max Connections
                            </label>
                            <input
                                type="text"
                                id="provider-max-connections"
                                className={`${styles["form-input"]} ${!isPositiveInteger(maxConnections) && maxConnections !== "" ? styles.error : ""}`}
                                placeholder="20"
                                value={maxConnections}
                                onChange={(e) => setMaxConnections(e.target.value)}
                            />
                        </div>

                        <div className={styles["form-group"]}>
                            <label htmlFor="provider-type" className={styles["form-label"]}>
                                Type
                            </label>
                            <select
                                id="provider-type"
                                className={styles["form-select"]}
                                value={type}
                                onChange={(e) => setType(parseInt(e.target.value, 10) as ProviderType)}
                            >
                                <option value={ProviderType.Disabled}>Disabled</option>
                                <option value={ProviderType.Pooled}>Pool Connections</option>
                                <option value={ProviderType.BackupOnly}>Backup Only</option>
                            </select>
                        </div>

                        <div className={`${styles["form-group"]} ${styles["full-width"]}`}>
                            <div className={styles["form-checkbox-wrapper"]}>
                                <input
                                    type="checkbox"
                                    id="provider-ssl"
                                    className={styles["form-checkbox"]}
                                    checked={useSsl}
                                    onChange={(e) => {
                                        setUseSsl(e.target.checked);
                                        setConnectionTested(false);
                                    }}
                                />
                                <label htmlFor="provider-ssl" className={styles["form-checkbox-label"]}>
                                    Use SSL
                                </label>
                            </div>
                        </div>
                    </div>

                    {testError && (
                        <div className={`${styles.alert} ${styles["alert-danger"]}`} style={{ marginTop: '16px' }}>
                            {testError}
                        </div>
                    )}

                    {connectionTested && (
                        <div className={`${styles.alert} ${styles["alert-success"]}`} style={{ marginTop: '16px' }}>
                            Connection test successful!
                        </div>
                    )}
                </div>

                <div className={styles["modal-footer"]}>
                    <div className={styles["modal-footer-left"]}></div>
                    <div className={styles["modal-footer-right"]}>
                        <Button variant="secondary" onClick={onClose}>
                            Cancel
                        </Button>
                        {!connectionTested ? (
                            <Button
                                variant="primary"
                                onClick={handleTestConnection}
                                disabled={!isFormValid || isTestingConnection}
                            >
                                {isTestingConnection ? "Testing..." : "Test Connection"}
                            </Button>
                        ) : (
                            <Button variant="primary" onClick={handleSave} disabled={!canSave}>
                                Save Provider
                            </Button>
                        )}
                    </div>
                </div>
            </div>
        </div>
    );
}

export function isUsenetSettingsUpdated(config: Record<string, string>, newConfig: Record<string, string>) {
    return config["usenet.providers"] !== newConfig["usenet.providers"]
}

export function isPositiveInteger(value: string) {
    const num = Number(value);
    return Number.isInteger(num) && num > 0 && value.trim() === num.toString();
}