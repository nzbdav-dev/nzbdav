import type { Route } from "./+types/route";
import styles from "./route.module.css"
import { Tabs, Tab, Button, Form } from "react-bootstrap"
import { backendClient } from "~/clients/backend-client.server";
import { isUsenetSettingsUpdated, UsenetSettings } from "./usenet/usenet";
import React, { useEffect } from "react";
import { isSabnzbdSettingsUpdated, isSabnzbdSettingsValid, SabnzbdSettings } from "./sabnzbd/sabnzbd";
import { isWebdavSettingsUpdated, isWebdavSettingsValid, WebdavSettings } from "./webdav/webdav";
import { isLibrarySettingsUpdated, LibrarySettings } from "./library/library";
import { isIntegritySettingsUpdated, IntegritySettings } from "./integrity/integrity";
import { isArrSettingsUpdated, ArrSettings } from "./arr/arr";
import { Maintenance } from "./maintenance/maintenance";

const defaultConfig = {
    "api.key": "",
    "api.categories": "",
    "api.max-queue-connections": "",
    "api.ensure-importable-video": "true",
    "usenet.host": "",
    "usenet.port": "",
    "usenet.use-ssl": "false",
    "usenet.connections": "",
    "usenet.connections-per-stream": "",
    "usenet.user": "",
    "usenet.pass": "",
    "webdav.user": "",
    "webdav.pass": "",
    "webdav.show-hidden-files": "false",
    "webdav.enforce-readonly": "true",
    "rclone.mount-dir": "",
    "media.library-dir": "",
    "integrity.enabled": "false",
    "integrity.scheduled_enabled": "false",
    "integrity.interval_hours": "24",
    "integrity.recheck_eligibility_days": "7",
    "integrity.max_files_per_run": "100",
    "integrity.corrupt_file_action": "log",
    "integrity.direct_deletion_fallback": "false",
    "integrity.auto_monitor": "false",
    "integrity.unmonitor_validated_files": "false",
    "integrity.mp4_deep_scan": "false",
    "integrity.nzb_segment_sample_percentage": "25",
    "integrity.nzb_segment_threshold_percentage": "92",
}

const advancedTabs = ["library", "integrity", "arr", "maintenance"];

export async function loader({ request }: Route.LoaderArgs) {
    // Build config keys including arr instances
    const arrKeys: string[] = [];
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
    
    const allConfigKeys = [...Object.keys(defaultConfig), ...arrKeys];
    
    // fetch the config items
    var configItems = await backendClient.getConfig(allConfigKeys);

    // transform to a map
    const config: Record<string, string> = { ...defaultConfig };
    
    // Initialize arr keys with empty strings
    arrKeys.forEach(key => {
        config[key] = "";
    });
    
    for (const item of configItems) {
        config[item.configName] = item.configValue;
    }
    return { config: config }
}

export default function Settings(props: Route.ComponentProps) {
    return (
        <Body config={props.loaderData.config} />
    );
}

type BodyProps = {
    config: Record<string, string>
};

function Body(props: BodyProps) {
    // stateful variables
    const [config, setConfig] = React.useState(props.config);
    const [newConfig, setNewConfig] = React.useState(config);
    const [isUsenetSettingsReadyToSave, setIsUsenetSettingsReadyToSave] = React.useState(false);
    const [isSaving, setIsSaving] = React.useState(false);
    const [isSaved, setIsSaved] = React.useState(false);
    const [showAdvanced, setShowAdvanced] = React.useState(() => {
        // Initialize from localStorage if available, default to false
        if (typeof window !== 'undefined') {
            const stored = localStorage.getItem('nzbdav-show-advanced-settings');
            return stored === 'true';
        }
        return false;
    });
    const [activeTab, setActiveTab] = React.useState('usenet');


    // derived variables
    const iseUsenetUpdated = isUsenetSettingsUpdated(config, newConfig);
    const isSabnzbdUpdated = isSabnzbdSettingsUpdated(config, newConfig);
    const isWebdavUpdated = isWebdavSettingsUpdated(config, newConfig);
    const isLibraryUpdated = isLibrarySettingsUpdated(config, newConfig);
    const isIntegrityUpdated = isIntegritySettingsUpdated(config, newConfig);
    const isArrUpdated = isArrSettingsUpdated(config, newConfig);
    const isUpdated = iseUsenetUpdated || isSabnzbdUpdated || isWebdavUpdated || isLibraryUpdated || isIntegrityUpdated || isArrUpdated;

    const usenetTitle = iseUsenetUpdated ? "Usenet ✏️" : "Usenet";
    const sabnzbdTitle = isSabnzbdUpdated ? "SABnzbd ✏️" : "SABnzbd";
    const webdavTitle = isWebdavUpdated ? "WebDAV ✏️" : "WebDAV";
    const libraryTitle = isLibraryUpdated ? "Library ✏️" : "Library";
    const integrityTitle = isIntegrityUpdated ? "Integrity ✏️" : "Integrity";
    const arrTitle = isArrUpdated ? "Radarr/Sonarr ✏️" : "Radarr/Sonarr";

    const saveButtonLabel = isSaving ? "Saving..."
        : !isUpdated && isSaved ? "Saved ✅"
        : !isUpdated && !isSaved ? "There are no changes to save"
        : iseUsenetUpdated && !isUsenetSettingsReadyToSave ? "Must test the usenet connection to save"
        : isSabnzbdUpdated && !isSabnzbdSettingsValid(newConfig) ? "Invalid SABnzbd settings"
        : isWebdavUpdated && !isWebdavSettingsValid(newConfig) ? "Invalid WebDAV settings"
        : "Save";
    const saveButtonVariant = saveButtonLabel === "Save" ? "primary"
        : saveButtonLabel === "Saved ✅" ? "success"
        : "secondary";
    const isSaveButtonDisabled = saveButtonLabel !== "Save";

    // effects
    useEffect(() => {
        if (!showAdvanced && advancedTabs.includes(activeTab)) {
            setActiveTab("usenet");
        }
    }, [showAdvanced, activeTab, setActiveTab])

    // events
    const onClear = React.useCallback(() => {
        setNewConfig(config);
        setIsSaved(false);
    }, [config, setNewConfig]);

    const onUsenetSettingsReadyToSave = React.useCallback((isReadyToSave: boolean) => {
        setIsUsenetSettingsReadyToSave(isReadyToSave);
    }, [setIsUsenetSettingsReadyToSave]);

    const onShowAdvancedChange = React.useCallback((checked: boolean) => {
        setShowAdvanced(checked);
        // Persist to localStorage
        if (typeof window !== 'undefined') {
            localStorage.setItem('nzbdav-show-advanced-settings', checked.toString());
        }
    }, [setShowAdvanced]);

    const onSave = React.useCallback(async () => {
        setIsSaving(true);
        setIsSaved(false);
        const response = await fetch("/settings/update", {
            method: "POST",
            body: (() => {
                const form = new FormData();
                const changedConfig = getChangedConfig(config, newConfig);
                form.append("config", JSON.stringify(changedConfig));
                return form;
            })()
        });
        if (response.ok) {
            setConfig(newConfig);
        }
        setIsSaving(false);
        setIsSaved(true);
    }, [config, newConfig, setIsSaving, setIsSaved, setConfig]);

    return (
        <div className={styles.container}>
            <Tabs
                activeKey={activeTab}
                onSelect={(k) => setActiveTab(k!)}
                className={styles.tabs}
            >
                <Tab eventKey="usenet" title={usenetTitle}>
                    <UsenetSettings config={newConfig} setNewConfig={setNewConfig} onReadyToSave={onUsenetSettingsReadyToSave} />
                </Tab>
                <Tab eventKey="sabnzbd" title={sabnzbdTitle}>
                    <SabnzbdSettings config={newConfig} setNewConfig={setNewConfig} />
                </Tab>
                <Tab eventKey="webdav" title={webdavTitle}>
                    <WebdavSettings config={newConfig} setNewConfig={setNewConfig} />
                </Tab>
                {showAdvanced &&
                    <Tab eventKey="library" title={libraryTitle}>
                        <LibrarySettings savedConfig={config} config={newConfig} setNewConfig={setNewConfig} />
                    </Tab>
                }
                {showAdvanced &&
                    <Tab eventKey="integrity" title={integrityTitle}>
                        <IntegritySettings config={newConfig} setNewConfig={setNewConfig} />
                    </Tab>
                }
                {showAdvanced &&
                    <Tab eventKey="arr" title={arrTitle}>
                        <ArrSettings config={newConfig} setNewConfig={setNewConfig} />
                    </Tab>
                }
                {showAdvanced &&
                    <Tab eventKey="maintenance" title="Maintenance">
                        <Maintenance savedConfig={config} />
                    </Tab>
                }
            </Tabs>
            <hr />

            <Form.Check
                style={{ marginBottom: '20px' }}
                type="checkbox"
                id="advanced-settings-checkbox"
                label="Show Advanced Settings"
                checked={showAdvanced}
                onChange={(e) => onShowAdvancedChange(Boolean(e.target.checked))}
            />

            {isUpdated && <Button
                className={styles.button}
                variant="secondary"
                disabled={!isUpdated}
                onClick={() => onClear()}>
                Clear
            </Button>}
            <Button
                className={styles.button}
                variant={saveButtonVariant}
                disabled={isSaveButtonDisabled}
                onClick={onSave}>
                {saveButtonLabel}
            </Button>
        </div>
    );
}

function getChangedConfig(
    config: Record<string, string>,
    newConfig: Record<string, string>
): Record<string, string> {
    let changedConfig: Record<string, string> = {};
    
    // Check all keys from newConfig (includes dynamic arr keys)
    const allConfigKeys = Object.keys(newConfig);
    for (const configKey of allConfigKeys) {
        if (config[configKey] !== newConfig[configKey]) {
            changedConfig[configKey] = newConfig[configKey];
        }
    }
    return changedConfig;
}