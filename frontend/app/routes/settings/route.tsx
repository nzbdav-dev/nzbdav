import type { Route } from "./+types/route";
import styles from "./route.module.css"
import { Tabs, Tab, Button, Form } from "react-bootstrap"
import { backendClient } from "~/clients/backend-client.server";
import { isUsenetSettingsUpdated, UsenetSettings } from "./usenet/usenet";
import React, { useEffect } from "react";
import { isSabnzbdSettingsUpdated, isSabnzbdSettingsValid, SabnzbdSettings } from "./sabnzbd/sabnzbd";
import { isWebdavSettingsUpdated, isWebdavSettingsValid, WebdavSettings } from "./webdav/webdav";
import { isLibrarySettingsUpdated, LibrarySettings } from "./library/library";
import { isArrsSettingsUpdated, isArrsSettingsValid, ArrsSettings } from "./arrs/arrs";
import { Maintenance } from "./maintenance/maintenance";
import { isRepairsSettingsUpdated, isRepairsSettingsValid, RepairsSettings } from "./repairs/repairs";

const defaultConfig = {
    "api.key": "",
    "api.categories": "",
    "api.max-queue-connections": "",
    "api.ensure-importable-video": "true",
    "api.ensure-article-existence": "false",
    "api.ignore-history-limit": "true",
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
    "webdav.preview-par2-files": "false",
    "rclone.mount-dir": "",
    "media.library-dir": "",
    "arr.instances": "{\"RadarrInstances\":[],\"SonarrInstances\":[],\"QueueRules\":[]}",
    "repair.connections": "",
    "repair.delete-unlinked-files": "false",
}

const advancedTabs = ["library", "maintenance"];

export async function loader({ request }: Route.LoaderArgs) {
    // fetch the config items
    var configItems = await backendClient.getConfig(Object.keys(defaultConfig));

    // transform to a map
    const config: Record<string, string> = defaultConfig;
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
    const [showAdvanced, setShowAdvanced] = React.useState(false);
    const [activeTab, setActiveTab] = React.useState('usenet');

    // derived variables
    const iseUsenetUpdated = isUsenetSettingsUpdated(config, newConfig);
    const isSabnzbdUpdated = isSabnzbdSettingsUpdated(config, newConfig);
    const isWebdavUpdated = isWebdavSettingsUpdated(config, newConfig);
    const isLibraryUpdated = isLibrarySettingsUpdated(config, newConfig);
    const isArrsUpdated = isArrsSettingsUpdated(config, newConfig);
    const isRepairsUpdated = isRepairsSettingsUpdated(config, newConfig);
    const isUpdated = iseUsenetUpdated || isSabnzbdUpdated || isWebdavUpdated || isLibraryUpdated || isArrsUpdated || isRepairsUpdated;

    const usenetTitle = iseUsenetUpdated ? "✏️ Usenet" : "Usenet";
    const sabnzbdTitle = isSabnzbdUpdated ? "✏️ SABnzbd " : "SABnzbd";
    const webdavTitle = isWebdavUpdated ? "✏️ WebDAV" : "WebDAV";
    const arrsTitle = isArrsUpdated ? "✏️ Radarr/Sonarr" : "Radarr/Sonarr";
    const libraryTitle = isLibraryUpdated ? "✏️ Library" : "Library";
    const repairsTitle = isRepairsUpdated ? "✏️ Repairs" : "Repairs";

    const saveButtonLabel = isSaving ? "Saving..."
        : !isUpdated && isSaved ? "Saved ✅"
        : !isUpdated && !isSaved ? "There are no changes to save"
        : iseUsenetUpdated && !isUsenetSettingsReadyToSave ? "Must test the usenet connection to save"
        : isSabnzbdUpdated && !isSabnzbdSettingsValid(newConfig) ? "Invalid SABnzbd settings"
        : isWebdavUpdated && !isWebdavSettingsValid(newConfig) ? "Invalid WebDAV settings"
        : isArrsUpdated && !isArrsSettingsValid(newConfig) ? "Invalid Arrs settings"
        : isRepairsUpdated && !isRepairsSettingsValid(newConfig) ? "Invalid Repairs settings"
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
    const onSelectTab = React.useCallback((tab: string | null) => {
        if (tab === "*") {
            setShowAdvanced(true);
            setActiveTab("library")
        } else {
            setActiveTab(tab!);
        }
    }, [setShowAdvanced, setActiveTab]);

    const onClear = React.useCallback(() => {
        setNewConfig(config);
        setIsSaved(false);
    }, [config, setNewConfig]);

    const onUsenetSettingsReadyToSave = React.useCallback((isReadyToSave: boolean) => {
        setIsUsenetSettingsReadyToSave(isReadyToSave);
    }, [setIsUsenetSettingsReadyToSave]);

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
                onSelect={onSelectTab}
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
                <Tab eventKey="arrs" title={arrsTitle}>
                    <ArrsSettings config={newConfig} setNewConfig={setNewConfig} />
                </Tab>
                <Tab eventKey="repairs" title={repairsTitle}>
                    <RepairsSettings config={newConfig} setNewConfig={setNewConfig} />
                </Tab>
                {!showAdvanced &&
                    <Tab eventKey="*" title={"🚀"}>
                    </Tab>
                }
                {showAdvanced &&
                    <Tab eventKey="library" title={libraryTitle}>
                        <LibrarySettings savedConfig={config} config={newConfig} setNewConfig={setNewConfig} />
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
                onChange={(e) => setShowAdvanced(Boolean(e.target.checked))} />

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
    let configKeys = Object.keys(defaultConfig);
    for (const configKey of configKeys) {
        if (config[configKey] !== newConfig[configKey]) {
            changedConfig[configKey] = newConfig[configKey];
        }
    }
    return changedConfig;
}