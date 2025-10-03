import { Alert, Form } from "react-bootstrap";
import styles from "./integrity.module.css"
import { type Dispatch, type SetStateAction } from "react";

type IntegritySettingsProps = {
    config: Record<string, string>
    setNewConfig: Dispatch<SetStateAction<Record<string, string>>>
};

export function IntegritySettings({ config, setNewConfig }: IntegritySettingsProps) {
    // derived variables
    const isEnabled = config["integrity.enabled"] === "true";
    const isScheduledEnabled = config["integrity.scheduled_enabled"] === "true";
    
    // Check if any arr instances are configured
    const hasArrInstances = () => {
        for (let i = 0; i < 10; i++) {
            const radarrUrl = config[`radarr.${i}.url`];
            const radarrApiKey = config[`radarr.${i}.api_key`];
            const sonarrUrl = config[`sonarr.${i}.url`];
            const sonarrApiKey = config[`sonarr.${i}.api_key`];
            
            if ((radarrUrl && radarrApiKey) || (sonarrUrl && sonarrApiKey)) {
                return true;
            }
        }
        return false;
    };

    // view
    return (
        <div className={styles.container}>
            {!isEnabled && (
                <Alert variant="info">
                    <strong>Media Integrity Verification is disabled.</strong> Enable it below to automatically check your media files for corruption.
                </Alert>
            )}

            <Form.Group>
                <Form.Check
                    className={styles.input}
                    type="checkbox"
                    id="integrity-enabled-checkbox"
                    aria-describedby="integrity-enabled-help"
                    label="Enable Media Integrity Verification"
                    checked={isEnabled}
                    onChange={e => setNewConfig({ ...config, "integrity.enabled": "" + e.target.checked })} />
                <Form.Text id="integrity-enabled-help" muted>
                    Check media files for corruption using ffprobe. Files are checked when downloaded and when errors occur during playback.
                </Form.Text>
            </Form.Group>

            {isEnabled && (
                <>
                    <hr />
                    <Form.Group>
                        <Form.Label htmlFor="integrity-action-select">Action for Corrupt Files</Form.Label>
                        <Form.Select
                            className={styles.input}
                            id="integrity-action-select"
                            aria-describedby="integrity-action-help"
                            value={config["integrity.corrupt_file_action"]}
                            onChange={e => setNewConfig({ ...config, "integrity.corrupt_file_action": e.target.value })}>
                            <option value="log">Log Only</option>
                            <option value="delete">Delete Files</option>
                            <option value="deleteViaArr">Delete via Radarr/Sonarr</option>
                        </Form.Select>
                        <Form.Text id="integrity-action-help" muted>
                            What to do when corrupt files are detected. "Log Only" is safest, "Delete" permanently removes corrupt files, "Delete via Radarr/Sonarr" removes files through configured Radarr/Sonarr instances (configure in Radarr/Sonarr tab).
                        </Form.Text>
                    </Form.Group>
                </>
            )}

            {isEnabled && (
                <>
                    <hr />
                    <Form.Group>
                        <Form.Check
                            className={styles.input}
                            type="checkbox"
                            id="integrity-scheduled-checkbox"
                            aria-describedby="integrity-scheduled-help"
                            label="Enable Scheduled Background Checks"
                            checked={isScheduledEnabled}
                            onChange={e => setNewConfig({ ...config, "integrity.scheduled_enabled": "" + e.target.checked })} />
                        <Form.Text id="integrity-scheduled-help" muted>
                            Run integrity checks automatically in the background at configurable intervals.
                        </Form.Text>
                    </Form.Group>
                </>
            )}

            {isEnabled && isScheduledEnabled && (
                <>
                    <hr />
                    <Form.Group>
                        <Form.Label htmlFor="integrity-interval-input">Check Interval (hours)</Form.Label>
                        <Form.Control
                            className={styles.input}
                            type="number"
                            id="integrity-interval-input"
                            aria-describedby="integrity-interval-help"
                            min="1"
                            max="168"
                            placeholder="24"
                            value={config["integrity.interval_hours"]}
                            onChange={e => setNewConfig({ ...config, "integrity.interval_hours": e.target.value })} />
                        <Form.Text id="integrity-interval-help" muted>
                            How often the background integrity check runs (1-168 hours).
                        </Form.Text>
                    </Form.Group>
                    <hr />
                    <Form.Group>
                        <Form.Label htmlFor="integrity-recheck-input">Re-check Files After (days)</Form.Label>
                        <Form.Control
                            className={styles.input}
                            type="number"
                            id="integrity-recheck-input"
                            aria-describedby="integrity-recheck-help"
                            min="1"
                            max="365"
                            placeholder="7"
                            value={config["integrity.recheck_eligibility_days"]}
                            onChange={e => setNewConfig({ ...config, "integrity.recheck_eligibility_days": e.target.value })} />
                        <Form.Text id="integrity-recheck-help" muted>
                            How many days to wait before re-checking files that have already been verified (1-365 days).
                        </Form.Text>
                    </Form.Group>
                    <hr />
                    <Form.Group>
                        <Form.Label htmlFor="integrity-max-files-input">Max Files Per Run</Form.Label>
                        <Form.Control
                            className={styles.input}
                            type="number"
                            id="integrity-max-files-input"
                            aria-describedby="integrity-max-files-help"
                            min="1"
                            max="1000"
                            placeholder="100"
                            value={config["integrity.max_files_per_run"]}
                            onChange={e => setNewConfig({ ...config, "integrity.max_files_per_run": e.target.value })} />
                        <Form.Text id="integrity-max-files-help" muted>
                            Maximum number of files to check in a single run to avoid overwhelming the system (1-1000).
                        </Form.Text>
                    </Form.Group>
                </>
            )}

            {config["integrity.corrupt_file_action"] === "deleteViaArr" && (
                <>
                    <hr />
                    {!hasArrInstances() ? (
                        <Alert variant="warning">
                            <strong>Radarr/Sonarr Integration Required:</strong> You have selected "Delete via Radarr/Sonarr" but need to configure your instances first. 
                            Please go to the <strong>Radarr/Sonarr</strong> tab in Advanced Settings to set up your instances.
                        </Alert>
                    ) : (
                        <Alert variant="success">
                            <strong>Radarr/Sonarr Integration Ready:</strong> Your configured instances will be used to delete corrupt files. 
                            You can manage your instances in the <strong>Radarr/Sonarr</strong> tab in Advanced Settings.
                        </Alert>
                    )}
                    <hr />
                    <Form.Group>
                        <Form.Check
                            type="checkbox"
                            id="direct-deletion-fallback-checkbox"
                            label="Enable direct deletion fallback"
                            checked={config["integrity.direct_deletion_fallback"] === "true"}
                            onChange={e => setNewConfig({ ...config, "integrity.direct_deletion_fallback": e.target.checked ? "true" : "false" })}
                        />
                        <Form.Text muted>
                            When enabled, if Radarr/Sonarr cannot delete a corrupt file (e.g., file not found in their library), 
                            the system will delete the file directly from the filesystem. Leave disabled for safety.
                        </Form.Text>
                    </Form.Group>
                    
                    <hr />
                    
                    <Form.Group>
                        <Form.Check
                            type="checkbox"
                            id="auto-monitor-checkbox"
                            label="Auto-monitor corrupt files before deletion"
                            checked={config["integrity.auto_monitor"] === "true"}
                            onChange={e => setNewConfig({ ...config, "integrity.auto_monitor": e.target.checked ? "true" : "false" })}
                        />
                        <Form.Text muted>
                            When enabled, corrupt files will be automatically monitored in Radarr/Sonarr before deletion, 
                            triggering automatic re-download. Only applies when using delete actions with Radarr/Sonarr integration.
                        </Form.Text>
                    </Form.Group>

                    <Form.Group>
                        <Form.Check
                            type="checkbox"
                            id="unmonitor-validated-files-checkbox"
                            label="Unmonitor successfully validated files"
                            checked={config["integrity.unmonitor_validated_files"] === "true"}
                            onChange={e => setNewConfig({ ...config, "integrity.unmonitor_validated_files": e.target.checked ? "true" : "false" })}
                        />
                        <Form.Text muted>
                            When enabled, scheduled integrity checks will automatically unmonitor files that pass validation 
                            in Radarr/Sonarr. This prevents unnecessary monitoring of files that have been verified as working.
                        </Form.Text>
                    </Form.Group>
                </>
            )}

            <hr />

            <Form.Group>
                <Form.Label htmlFor="nzb-segment-sample-input">NZB segment sample percentage</Form.Label>
                <Form.Control
                    type="number"
                    id="nzb-segment-sample-input"
                    value={config["integrity.nzb_segment_sample_percentage"] || "25"}
                    min="1"
                    max="100"
                    onChange={e => {
                        const value = Math.max(1, Math.min(100, parseInt(e.target.value) || 25));
                        setNewConfig({ ...config, "integrity.nzb_segment_sample_percentage": value.toString() });
                    }}
                />
                <Form.Text muted>
                    Percentage of NZB segments to randomly sample during integrity checks (1-100%). 
                    Lower values are faster but less thorough. Higher values are more accurate but slower.
                </Form.Text>
            </Form.Group>

            <Form.Group>
                <Form.Label htmlFor="nzb-segment-threshold-input">NZB segment threshold percentage</Form.Label>
                <Form.Control
                    type="number"
                    id="nzb-segment-threshold-input"
                    value={config["integrity.nzb_segment_threshold_percentage"] || "92"}
                    min="80"
                    max="100"
                    onChange={e => {
                        const value = Math.max(80, Math.min(100, parseInt(e.target.value) || 92));
                        setNewConfig({ ...config, "integrity.nzb_segment_threshold_percentage": value.toString() });
                    }}
                />
                <Form.Text muted>
                    Minimum percentage of NZB segments that must be available for a file to be considered valid (80-100%). 
                    Lower values are more permissive but may allow files with missing data through validation.
                </Form.Text>
            </Form.Group>

            <Form.Group>
                <Form.Check
                    type="checkbox"
                    id="mp4-deep-scan-checkbox"
                    label="Enable MP4 deep scan"
                    checked={config["integrity.mp4_deep_scan"] === "true"}
                    onChange={e => setNewConfig({ ...config, "integrity.mp4_deep_scan": e.target.checked ? "true" : "false" })}
                />
                <Form.Text muted>
                    When enabled, MP4 files use a slower but more thorough validation method that can detect moov atom issues. 
                    <strong>Warning:</strong> This may require downloading the entire file during validation, which can be slow and use significant bandwidth.
                    Disable for faster validation if you don't have MP4 corruption issues.
                </Form.Text>
            </Form.Group>

        </div>
    );
}

export function isIntegritySettingsUpdated(config: Record<string, string>, newConfig: Record<string, string>) {
    return config["integrity.enabled"] !== newConfig["integrity.enabled"]
        || config["integrity.scheduled_enabled"] !== newConfig["integrity.scheduled_enabled"]
        || config["integrity.interval_hours"] !== newConfig["integrity.interval_hours"]
        || config["integrity.recheck_eligibility_days"] !== newConfig["integrity.recheck_eligibility_days"]
        || config["integrity.max_files_per_run"] !== newConfig["integrity.max_files_per_run"]
        || config["integrity.corrupt_file_action"] !== newConfig["integrity.corrupt_file_action"]
        || config["integrity.direct_deletion_fallback"] !== newConfig["integrity.direct_deletion_fallback"]
        || config["integrity.auto_monitor"] !== newConfig["integrity.auto_monitor"]
        || config["integrity.unmonitor_validated_files"] !== newConfig["integrity.unmonitor_validated_files"]
        || config["integrity.mp4_deep_scan"] !== newConfig["integrity.mp4_deep_scan"]
        || config["integrity.nzb_segment_sample_percentage"] !== newConfig["integrity.nzb_segment_sample_percentage"]
        || config["integrity.nzb_segment_threshold_percentage"] !== newConfig["integrity.nzb_segment_threshold_percentage"];
}
