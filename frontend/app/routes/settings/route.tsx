import { Button } from "~/components/ui/button";
import { Alert } from "~/components/ui/feedback";
import { SettingsPanel, ManagedEnvProvider, omitManagedConfigKeys, pinManagedConfigKeys, type ManagedEnvMap } from "~/components/ui";
import type { Route } from "./+types/route";
import { backendClient } from "~/clients/backend-client.server";
import { isUsenetSettingsUpdated, UsenetSettings } from "./usenet/usenet";
import { isSabnzbdSettingsUpdated, isSabnzbdSettingsValid, SabnzbdSettings } from "./sabnzbd/sabnzbd";
import { isWebdavSettingsUpdated, isWebdavSettingsValid, WebdavSettings } from "./webdav/webdav";
import { isArrsSettingsUpdated, isArrsSettingsValid, ArrsSettings } from "./arrs/arrs";
import { isIndexersSettingsUpdated, isIndexersSettingsValid, IndexersSettings } from "./indexers/indexers";
import { isProfilesSettingsUpdated, isProfilesSettingsValid, ProfilesSettings } from "./profiles/profiles";
import { isMaintenanceSettingsUpdated, Maintenance } from "./maintenance/maintenance";
import { isBackupSettingsUpdated, BackupSettings } from "./backup/backup";
import { AltmountMigration } from "./altmount-migration/altmount-migration";
import { isRepairsSettingsUpdated, RepairsSettings } from "./repairs/repairs";
import { isWatchdogSettingsUpdated, WatchdogSettings } from "./watchdog/watchdog";
import { isPreflightSettingsUpdated, PreflightSettings } from "./preflight/preflight";
import { isWatchtowerSettingsUpdated, WatchtowerSettings } from "./watchtower/watchtower";
import { isWardenSettingsUpdated, WardenSettings } from "./warden/warden";
import { isRcloneSettingsUpdated, RcloneSettings } from "./rclone/rclone";
import { SupportSettings } from "./support/support";
import { useCallback, useMemo, useState, type Dispatch, type SetStateAction } from "react";
import { useBlocker, useSearchParams } from "react-router";
import { ConfirmModal } from "~/components/confirm-modal/confirm-modal";
import { getAppVersion } from "~/utils/version.server";
import { parseSettingsTab, getSettingsTabItem } from "./settings-tabs";
import { Icon } from "~/components/ui";

const defaultConfig = {
    "general.base-url": "",
    "api.key": "",
    "api.categories": "",
    "api.manual-category": "uncategorized",
    "api.ensure-importable-video": "true",
    "api.skip-non-video-on-missing-articles": "true",
    "api.ensure-article-existence-categories": "",
    "api.ignore-history-limit": "true",
    "api.download-file-blocklist": "*.nfo, *.par2, *.sfv, *sample.mkv, *unpack.mkv, *.unpack.mp4",
    "api.duplicate-nzb-behavior": "increment",
    "api.import-strategy": "symlinks",
    "api.completed-downloads-dir": "",
    "api.user-agent": "",
    "api.addurl-trusted-hosts": "",
    "api.search-user-agent": "",
    "usenet.providers": "",
    "usenet.max-download-connections": "0",
    "usenet.max-download-connections-per-stream": "false",
    "usenet.max-download-connections-per-stream-preset": "high",
    "usenet.max-queue-connections": "",
    "queue.worker-count": "1",
    "usenet.streaming-priority": "80",
    "usenet.streaming-segment-timeout-seconds": "8",
    "usenet.streaming-read-timeout-seconds": "30",
    "usenet.streaming-segment-retries": "3",
    "usenet.article-buffer-size": "40",
    "usenet.in-flight-article-budget-mb": "512",
    "usenet.idle-connection-timeout-seconds": "60",
    "usenet.pipelined-body-requests": "true",
    "usenet.segment-cache.enabled": "false",
    "usenet.segment-cache.path": "/config/segment-cache",
    "usenet.segment-cache.max-gb": "10",
    "usenet.pipelining.enabled": "false",
    "usenet.pipelining.depth": "8",
    "usenet.cascade.enabled": "false",
    "usenet.cascade.retry-primary-on-miss": "true",
    "usenet.article-miss-cache-ttl-seconds": "300",
    "usenet.article-miss-cache-max-entries": "10000",
    "webdav.user": "admin",
    "webdav.pass": "",
    "webdav.show-hidden-files": "false",
    "webdav.enforce-readonly": "true",
    "webdav.preview-par2-files": "false",
    "webdav.windows-safe-paths": "true",
    "rclone.rc-enabled": "false",
    "rclone.host": "",
    "rclone.user": "",
    "rclone.pass": "",
    "rclone.mount-dir": "",
    "media.library-dir": "",
    "arr.instances": "{\"RadarrInstances\":[],\"SonarrInstances\":[],\"QueueRules\":[]}",
    "indexers.instances": "{\"Indexers\":[]}",
    "profiles.instances": "{\"Profiles\":[]}",
    "play.watchdog-enabled": "true",
    "play.total-budget-seconds": "30",
    "play.hedge-delay-seconds": "3",
    "play.max-candidates": "3",
    "play.max-attempts": "10",
    "play.verify-mode": "none",
    "play.candidate-negative-cache-minutes": "5",
    "play.resolution-cache-ttl-hours": "168",
    "play.prefer-subtitles": "true",
    "grab.stall-failover-enabled": "true",
    "grab.stall-failover-window-seconds": "2",
    "grab.stall-failover-ceiling-seconds": "5",
    "search.exclude-patterns": "",
    "search.exclude-sync-urls": "",
    "search.exclude-sync-refresh-minutes": "720",
    "variants.mode": "off",
    "variants.tolerance-pct": "25",
    "variants.max-per-group": "3",
    "variants.replay-strategy": "closest-to-click",
    "variants.fallback-on-failure": "true",
    "variants.eviction-strategy": "lru",
    "variants.eviction-active-grace-seconds": "60",
    "preflight.mode": "off",
    "preflight.max-attempts": "20",
    "preflight.ttl-seconds": "120",
    "preflight.indexer-max-wait-seconds": "5",
    "repair.enable": "false",
    "repair.healthcheck-concurrency": "50",
    "repair.healthcheck-depth": "standard",
    "repair.healthcheck-aging": "false",
    "repair.auto-remove-after-failures": "0",
    "repair.auto-remove-unlinked-only": "true",
    "db.is-startup-vacuum-enabled": "false",
    "database.history-retention-days": "90",
    "database.healthcheck-retention-days": "30",
    "maintenance.remove-orphaned-schedule-enabled": "false",
    "maintenance.remove-orphaned-schedule-time": "0",
    "backup.schedule-enabled": "false",
    "backup.schedule-time": "0",
    "backup.retention-count": "5",
    "api.nzb-backup-enabled": "false",
    "api.nzb-backup-location": "",
    "api.nzb-backup-retention-days": "30",
    "watchtower.enabled": "false",
    "watchtower.profile-token": "",
    "watchtower.ranking": "watchdog",
    "watchtower.size-floor-bytes": "524288000",
    "watchtower.size-ceiling-bytes": "0",
    "watchtower.shortlist-depth": "2",
    "watchtower.grab-cap-per-resolve": "3",
    "watchtower.active-set-cap": "100",
    "watchtower.daily-resolve-budget": "60",
    "watchtower.auto-throughput": "false",
    "watchtower.sync-interval-seconds": "3600",
    "watchtower.series-scope": "latest-season",
    "watchtower.season-bundles": "true",
    "watchtower.series-max-episodes": "50",
    "watchtower.series-cap-keep": "newest",
    "watchtower.series-recent-count": "3",
    "watchtower.season-bundle-fallback": "false",
    "watchtower.season-bundle-fallback-scope": "latest-season",
    "watchtower.season-bundle-fallback-recent-count": "2",
    "watchtower.season-bundle-fallback-max-episodes": "50",
    "watchtower.min-grabs": "0",
    "watchtower.verify-sample-count": "3",
    "watchtower.verify-timeout-seconds": "10",
    "watchtower.keepfresh-base-seconds": "21600",
    "watchtower.keepfresh-max-seconds": "604800",
    "watchtower.unavailable-retry-seconds": "21600",
    "watchtower.verbose-logging": "false",
    "warden.hide-dead": "true",
    "warden.quorum": "2",
    "warden.backbone-scope": "true",
}

export async function loader({ request }: Route.LoaderArgs) {
    const configItems = await backendClient.getConfig(Object.keys(defaultConfig));

    const config: Record<string, string> = { ...defaultConfig };
    const managedEnv: ManagedEnvMap = {};
    for (const item of configItems) {
        config[item.configName] = item.configValue;
        if (item.environmentVariableName) {
            managedEnv[item.configName] = item.environmentVariableName;
        }
    }

    return {
        config: config,
        managedEnv,
        appVersion: (await getAppVersion()) ?? "unknown",
    }
}

export default function Settings(props: Route.ComponentProps) {
    return (
        <Body {...props.loaderData} />
    );
}

type BodyProps = {
    config: Record<string, string>,
    managedEnv: ManagedEnvMap,
    appVersion: string,
};

function Body(props: BodyProps) {
    const [searchParams] = useSearchParams();
    const activeTab = parseSettingsTab(searchParams.get("tab"));
    const activeTabItem = getSettingsTabItem(activeTab);
    const [config, setConfig] = useState(props.config);
    const [newConfig, setNewConfigState] = useState(config);
    const managedEnv = props.managedEnv;
    const setNewConfig: Dispatch<SetStateAction<Record<string, string>>> = useCallback((updater) => {
        setNewConfigState((prev) => {
            const next = typeof updater === "function" ? updater(prev) : updater;
            return pinManagedConfigKeys(next, config, managedEnv);
        });
    }, [config, managedEnv]);
    const [isSaving, setIsSaving] = useState(false);
    const [isSaved, setIsSaved] = useState(false);
    const [saveError, setSaveError] = useState<string | null>(null);
    const managedCount = useMemo(() => Object.keys(managedEnv).length, [managedEnv]);

    const iseUsenetUpdated = isUsenetSettingsUpdated(config, newConfig);
    const isSabnzbdUpdated = isSabnzbdSettingsUpdated(config, newConfig);
    const isWebdavUpdated = isWebdavSettingsUpdated(config, newConfig);
    const isArrsUpdated = isArrsSettingsUpdated(config, newConfig);
    const isIndexersUpdated = isIndexersSettingsUpdated(config, newConfig);
    const isProfilesUpdated = isProfilesSettingsUpdated(config, newConfig);
    const isRepairsUpdated = isRepairsSettingsUpdated(config, newConfig);
    const isWatchdogUpdated = isWatchdogSettingsUpdated(config, newConfig);
    const isPreflightUpdated = isPreflightSettingsUpdated(config, newConfig);
    const isRcloneUpdated = isRcloneSettingsUpdated(config, newConfig);
    const isMaintenanceUpdated = isMaintenanceSettingsUpdated(config, newConfig);
    const isBackupUpdated = isBackupSettingsUpdated(config, newConfig);
    const isWatchtowerUpdated = isWatchtowerSettingsUpdated(config, newConfig);
    const isWardenUpdated = isWardenSettingsUpdated(config, newConfig);
    const isUpdated = iseUsenetUpdated || isSabnzbdUpdated || isWebdavUpdated || isArrsUpdated || isIndexersUpdated || isProfilesUpdated || isRepairsUpdated || isWatchdogUpdated || isPreflightUpdated || isRcloneUpdated || isMaintenanceUpdated || isBackupUpdated || isWatchtowerUpdated || isWardenUpdated;
    const navigationBlocker = useNavigationBlocker(isUpdated);

    const saveButtonLabel = isSaving ? "Saving..."
        : !isUpdated && isSaved ? "Saved"
        : !isUpdated && !isSaved ? "There are no changes to save"
        : isSabnzbdUpdated && !isSabnzbdSettingsValid(newConfig) ? "Invalid SABnzbd settings"
        : isWebdavUpdated && !isWebdavSettingsValid(newConfig) ? "Invalid WebDAV settings"
        : isArrsUpdated && !isArrsSettingsValid(newConfig) ? "Invalid Arrs settings"
        : isIndexersUpdated && !isIndexersSettingsValid(newConfig) ? "Invalid Indexers settings"
        : isProfilesUpdated && !isProfilesSettingsValid(newConfig) ? "Invalid Search Profiles settings"
        : "Save";
    const saveButtonVariant = saveButtonLabel === "Save" ? "primary"
        : saveButtonLabel === "Saved" ? "success"
        : "secondary";
    const isSaveButtonDisabled = saveButtonLabel !== "Save";

    const onClear = useCallback(() => {
        setNewConfigState(config);
        setIsSaved(false);
        setSaveError(null);
    }, [config]);

    const postConfigUpdate = useCallback(async (changedConfig: Record<string, string>) => {
        const response = await fetch("/settings/update", {
            method: "POST",
            body: (() => {
                const form = new FormData();
                form.append("config", JSON.stringify(changedConfig));
                return form;
            })()
        });
        if (!response.ok) {
            throw new Error(`Settings update failed with status ${response.status}`);
        }
    }, []);

    const onSave = useCallback(async () => {
        setIsSaving(true);
        setIsSaved(false);
        setSaveError(null);
        try {
            const changedConfig = omitManagedConfigKeys(
                getChangedConfig(config, newConfig),
                managedEnv,
            );
            if (Object.keys(changedConfig).length === 0) {
                // Managed keys are pinned client-side; nothing left to persist.
                setConfig(newConfig);
                setIsSaved(true);
                return;
            }
            await postConfigUpdate(changedConfig);
            setConfig(newConfig);
            setIsSaved(true);
        } catch {
            setSaveError("Could not save settings. Check the server logs and try again.");
        } finally {
            setIsSaving(false);
        }
    }, [config, newConfig, managedEnv, postConfigUpdate]);

    const persistConfigPatch = useCallback(async (patch: Record<string, string>) => {
        const nextNew = pinManagedConfigKeys(
            { ...newConfig, ...patch },
            config,
            managedEnv,
        );
        setNewConfigState(nextNew);
        setSaveError(null);

        const changedFromPatch: Record<string, string> = {};
        for (const key of Object.keys(patch)) {
            if (config[key] !== nextNew[key]) {
                changedFromPatch[key] = nextNew[key];
            }
        }
        const changedConfig = omitManagedConfigKeys(changedFromPatch, managedEnv);

        if (Object.keys(changedConfig).length > 0) {
            await postConfigUpdate(changedConfig);
        }

        const nextSaved = { ...config };
        for (const key of Object.keys(patch)) {
            nextSaved[key] = nextNew[key];
        }
        setConfig(nextSaved);

        const remaining = omitManagedConfigKeys(
            getChangedConfig(nextSaved, nextNew),
            managedEnv,
        );
        setIsSaved(Object.keys(remaining).length === 0);
    }, [config, newConfig, managedEnv, postConfigUpdate]);

    return (
        <ManagedEnvProvider value={managedEnv}>
        <div className="flex min-h-full flex-col gap-6 px-4 py-4 md:px-8">
            <SettingsPanel>
                <header className="mb-6 flex items-center gap-3 border-b border-base-content/10 pb-4">
                    <Icon name={activeTabItem.icon} className="!text-[28px] text-base-content/70" />
                    <h1 className="text-2xl font-semibold tracking-tight text-base-content">
                        {activeTabItem.label}
                    </h1>
                </header>
                {managedCount > 0 && (
                    <Alert variant="warning" className="mb-6 text-sm">
                        <span>
                            {managedCount === 1 ? "1 setting is" : `${managedCount} settings are`}{" "}
                            managed by <code className="font-mono text-xs">NZBDAV_CONFIG__...</code>{" "}
                            environment variables and shown read-only. Change the container environment
                            and restart to update them.
                        </span>
                    </Alert>
                )}
                {activeTab === "usenet" && (
                    <UsenetSettings
                        config={newConfig}
                        setNewConfig={setNewConfig}
                        persistConfigPatch={persistConfigPatch}
                    />
                )}
                {activeTab === "indexers" && <IndexersSettings config={newConfig} setNewConfig={setNewConfig} savedConfig={config} />}
                {activeTab === "profiles" && <ProfilesSettings config={newConfig} setNewConfig={setNewConfig} />}
                {activeTab === "watchdog" && <WatchdogSettings config={newConfig} setNewConfig={setNewConfig} />}
                {activeTab === "preflight" && <PreflightSettings config={newConfig} setNewConfig={setNewConfig} />}
                {activeTab === "watchtower" && <WatchtowerSettings config={newConfig} setNewConfig={setNewConfig} />}
                {activeTab === "warden" && <WardenSettings config={newConfig} setNewConfig={setNewConfig} />}
                {activeTab === "sabnzbd" && <SabnzbdSettings config={newConfig} setNewConfig={setNewConfig} appVersion={props.appVersion} />}
                {activeTab === "webdav" && <WebdavSettings config={newConfig} setNewConfig={setNewConfig} />}
                {activeTab === "arrs" && <ArrsSettings config={newConfig} setNewConfig={setNewConfig} />}
                {activeTab === "repairs" && <RepairsSettings config={newConfig} setNewConfig={setNewConfig} />}
                {activeTab === "rclone" && <RcloneSettings config={newConfig} setNewConfig={setNewConfig} />}
                {activeTab === "maintenance" && <Maintenance savedConfig={config} config={newConfig} setNewConfig={setNewConfig} />}
                {activeTab === "backup" && <BackupSettings config={newConfig} setNewConfig={setNewConfig} />}
                {activeTab === "support" && <SupportSettings />}
                {activeTab === "altmount-migration" && <AltmountMigration />}
            </SettingsPanel>

            {saveError && (
                <Alert variant="danger" className="sticky bottom-20 z-10 text-sm">
                    {saveError}
                </Alert>
            )}
            {((activeTab !== "support" && activeTab !== "altmount-migration") || isUpdated) && <div className="sticky bottom-0 z-10 -mx-4 flex flex-wrap justify-end gap-2 border-t border-base-content/10 bg-base-300/95 px-4 py-3 backdrop-blur md:-mx-8 md:px-8">
                {isUpdated && <Button
                    className="min-w-28"
                    variant="outline"
                    disabled={!isUpdated}
                    onClick={onClear}>
                    <Icon name="undo" className="!text-[18px]" />
                    Clear
                </Button>}
                <Button
                    className="min-w-28"
                    variant={saveButtonVariant}
                    disabled={isSaveButtonDisabled}
                    onClick={onSave}>
                    <Icon name={isSaving ? "progress_activity" : saveButtonLabel === "Saved" ? "check" : "save"} className={`!text-[18px] ${isSaving ? "animate-spin" : ""}`} />
                    {saveButtonLabel}
                </Button>
            </div>}
            <ConfirmModal
                show={navigationBlocker.showConfirmation}
                title="Unsaved Changes"
                message={<>You have unsaved changes.<br/>Are you sure you want to leave this page?</>}
                cancelText="Stay"
                confirmText="Leave"
                onCancel={navigationBlocker.onCancelNavigation}
                onConfirm={navigationBlocker.onConfirmNavigation}
            />
        </div>
        </ManagedEnvProvider>
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

function useNavigationBlocker(isConfigUpdated: boolean) {
    const blocker = useBlocker(({ currentLocation, nextLocation }) => {
        if (!isConfigUpdated) return false;
        const stayingInSettings =
            currentLocation.pathname.startsWith("/settings")
            && nextLocation.pathname.startsWith("/settings");
        return !stayingInSettings;
    });

    const onConfirmNavigation = useCallback(() => {
        if (blocker.state === "blocked") {
            blocker.proceed();
        }
    }, [blocker]);

    const onCancelNavigation = useCallback(() => {
        if (blocker.state === "blocked") {
            blocker.reset();
        }
    }, [blocker]);

    return {
        showConfirmation: blocker.state === "blocked",
        onConfirmNavigation,
        onCancelNavigation
    }
}
