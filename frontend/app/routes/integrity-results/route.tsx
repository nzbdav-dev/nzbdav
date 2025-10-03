import type { Route } from "./+types/route";
import styles from "./route.module.css";
import { useState, useEffect, useCallback, useMemo } from "react";
import { Card, Table, Badge, Alert, Button, Collapse, Modal, Form } from "react-bootstrap";
import { receiveMessage } from "../../utils/websocket-util";
import { type IntegrityResultsData, type IntegrityJobRun, type IntegrityFileResult, type IntegrityCheckRunParameters } from "~/types/integrity";

const integrityProgressTopic = { 'icp': 'state' };


// Modal for configuring integrity check parameters
function IntegrityCheckModal({ 
    show, 
    onHide, 
    onRun 
}: { 
    show: boolean; 
    onHide: () => void; 
    onRun: (params: IntegrityCheckRunParameters) => void; 
}) {
    const [parameters, setParameters] = useState<IntegrityCheckRunParameters | null>(null);
    const [loading, setLoading] = useState(false);

    // Load default parameters when modal opens
    useEffect(() => {
        if (show && !parameters) {
            setLoading(true);
            fetch("/api/media-integrity/parameters")
                .then(response => response.json())
                .then(data => {
                    setParameters(data);
                    setLoading(false);
                })
                .catch(error => {
                    console.error("Failed to load default parameters:", error);
                    setLoading(false);
                });
        }
    }, [show, parameters]);

    const handleRun = () => {
        if (parameters) {
            onRun(parameters);
            onHide();
        }
    };


    if (!parameters && loading) {
        return (
            <Modal show={show} onHide={onHide} size="lg">
                <Modal.Header closeButton>
                    <Modal.Title>Start Media Integrity Check</Modal.Title>
                </Modal.Header>
                <Modal.Body>
                    <div className="text-center">Loading default parameters...</div>
                </Modal.Body>
            </Modal>
        );
    }

    if (!parameters) {
        return null;
    }

    return (
        <Modal show={show} onHide={onHide} size="lg">
            <Modal.Header closeButton>
                <Modal.Title>Start Media Integrity Check</Modal.Title>
            </Modal.Header>
            <Modal.Body>
                <Form>
                    <Form.Group className="mb-3">
                        <Form.Label>Scan Directory</Form.Label>
                        <Form.Control
                            type="text"
                            value={parameters.scanDirectory || ""}
                            onChange={e => setParameters({...parameters, scanDirectory: e.target.value})}
                            placeholder="Leave empty to scan internal files"
                        />
                        <Form.Text className="text-muted">
                            Directory to scan for media files. Uses library directory by default.
                        </Form.Text>
                    </Form.Group>

                    <Form.Group className="mb-3">
                        <Form.Label>Maximum Files to Check</Form.Label>
                        <Form.Control
                            type="number"
                            min="1"
                            max="10000"
                            value={parameters.maxFilesToCheck}
                            onChange={e => setParameters({...parameters, maxFilesToCheck: parseInt(e.target.value) || 100})}
                        />
                        <Form.Text className="text-muted">
                            Limit the number of files to check in this run.
                        </Form.Text>
                    </Form.Group>

                    <Form.Group className="mb-3">
                        <Form.Label>Corrupt File Action</Form.Label>
                        <Form.Select
                            value={parameters.corruptFileAction}
                            onChange={e => setParameters({...parameters, corruptFileAction: e.target.value as "log" | "delete" | "deleteViaArr"})}
                        >
                            <option value="log">Log only</option>
                            <option value="delete">Delete corrupt files</option>
                            <option value="deleteViaArr">Delete via Radarr/Sonarr</option>
                        </Form.Select>
                    </Form.Group>

                    <Form.Group className="mb-3">
                        <Form.Check
                            type="checkbox"
                            id="modal-mp4-deep-scan"
                            label="Enable MP4 deep scan"
                            checked={parameters.mp4DeepScan}
                            onChange={e => setParameters({...parameters, mp4DeepScan: e.target.checked})}
                        />
                        <Form.Text className="text-muted">
                            Use slower but more thorough validation for MP4 files. May require downloading the entire file during validation.
                        </Form.Text>
                    </Form.Group>

                    <Form.Group className="mb-3">
                        <Form.Check
                            type="checkbox"
                            id="modal-auto-monitor"
                            label="Auto-monitor corrupt files"
                            checked={parameters.autoMonitor}
                            onChange={e => setParameters({...parameters, autoMonitor: e.target.checked})}
                        />
                        <Form.Text className="text-muted">
                            Automatically monitor corrupt files in Radarr/Sonarr for re-download.
                        </Form.Text>
                    </Form.Group>

                    <Form.Group className="mb-3">
                        <Form.Check
                            type="checkbox"
                            id="modal-unmonitor-validated"
                            label="Unmonitor successfully validated files"
                            checked={parameters.unmonitorValidatedFiles}
                            onChange={e => setParameters({...parameters, unmonitorValidatedFiles: e.target.checked})}
                        />
                        <Form.Text className="text-muted">
                            Automatically unmonitor files that pass validation in Radarr/Sonarr.
                        </Form.Text>
                    </Form.Group>

                    <Form.Group className="mb-3">
                        <Form.Label>NZB Segment Sample Percentage</Form.Label>
                        <Form.Control
                            type="number"
                            min="1"
                            max="100"
                            value={parameters.nzbSegmentSamplePercentage}
                            onChange={e => setParameters({...parameters, nzbSegmentSamplePercentage: parseInt(e.target.value) || 25})}
                        />
                        <Form.Text className="text-muted">
                            Percentage of NZB segments to randomly sample during integrity checks (1-100%). Lower values are faster but less thorough.
                        </Form.Text>
                    </Form.Group>

                    <Form.Group className="mb-3">
                        <Form.Label>NZB Segment Threshold Percentage</Form.Label>
                        <Form.Control
                            type="number"
                            min="80"
                            max="100"
                            value={parameters.nzbSegmentThresholdPercentage}
                            onChange={e => setParameters({...parameters, nzbSegmentThresholdPercentage: parseInt(e.target.value) || 92})}
                        />
                        <Form.Text className="text-muted">
                            Minimum percentage of sampled NZB segments that must be available for a file to be considered valid (80-100%).
                        </Form.Text>
                    </Form.Group>

                    <Form.Group className="mb-3">
                        <Form.Check
                            type="checkbox"
                            id="modal-direct-deletion-fallback"
                            label="Enable direct deletion fallback"
                            checked={parameters.directDeletionFallback}
                            onChange={e => setParameters({...parameters, directDeletionFallback: e.target.checked})}
                        />
                        <Form.Text className="text-muted">
                            Delete files directly if Radarr/Sonarr integration fails.
                        </Form.Text>
                    </Form.Group>
                </Form>
            </Modal.Body>
            <Modal.Footer>
                <Button variant="secondary" onClick={onHide}>
                    Cancel
                </Button>
                <Button variant="primary" onClick={handleRun}>
                    Start Integrity Check
                </Button>
            </Modal.Footer>
        </Modal>
    );
}

// Integrity Check Button Component
function IntegrityCheckButton({ setLiveData }: { setLiveData: React.Dispatch<React.SetStateAction<any>> }) {
    const [isFetching, setIsFetching] = useState(false);
    const [progress, setProgress] = useState<string | null>(null);
    const [connected, setConnected] = useState(false);
    const [showModal, setShowModal] = useState(false);

    const isFinished = progress?.startsWith("complete") || progress?.startsWith("failed") || progress?.startsWith("cancelled") || progress === "no_files";
    const isRunning = !isFinished && (isFetching || progress !== null);
    const isRunButtonEnabled = connected && !isRunning;
    const runButtonVariant = isRunButtonEnabled ? 'primary' : 'secondary';
    const runButtonLabel = isRunning ? "⌛ Checking..." : '🔍 Check Media Integrity Now';


    // WebSocket connection effect
    useEffect(() => {
        let ws: WebSocket;
        let disposed = false;
        function connect() {
            ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
            ws.onmessage = receiveMessage((_, message) => setProgress(message));
            ws.onopen = () => { 
                setConnected(true); 
                ws.send(JSON.stringify(integrityProgressTopic)); 
            }
            ws.onclose = () => { 
                setConnected(false);
                if (!disposed) setTimeout(() => connect(), 1000); 
                setProgress(null) 
            };
            ws.onerror = () => { ws.close() };
        }
        connect();
        return () => { disposed = true; ws.close(); }
    }, [setProgress, setConnected]);

    // Trigger integrity check with custom parameters
    const onRunCheckWithParameters = useCallback(async (parameters: IntegrityCheckRunParameters) => {
        setIsFetching(true);
        try {
            const response = await fetch("/api/media-integrity/run", {
                method: "POST",
                headers: {
                    'Content-Type': 'application/json;charset=UTF-8'
                },
                body: JSON.stringify(parameters)
            });
            
            if (response.ok) {
                const result = await response.json();
                console.debug("Integrity check response:", result);
                
                // If the check started and we got run details, add it to the state immediately
                if (result.started && result.runDetails) {
                    console.debug("Creating new job run from RunDetails:", result.runDetails);
                    
                    const newJobRun = {
                        date: result.runDetails.startTime,
                        runId: result.runDetails.runId,
                        startTime: result.runDetails.startTime,
                        endTime: result.runDetails.endTime,
                        totalFiles: result.runDetails.totalFiles,
                        corruptFiles: result.runDetails.corruptFiles,
                        validFiles: result.runDetails.validFiles,
                        files: result.runDetails.files || [],
                        parameters: result.runDetails.parameters
                    };
                    
                    console.debug("New job run object:", newJobRun);
                    
                    // Add the new run to the beginning of the list
                    setLiveData((prevData: any) => {
                        console.debug("Current liveData before adding new run:", prevData);
                        
                        if (!prevData) {
                            console.debug("No prevData, returning early");
                            return prevData;
                        }
                        
                        const updatedData = {
                            ...prevData,
                            jobRuns: [newJobRun, ...prevData.jobRuns]
                        };
                        
                        console.debug("Updated liveData with new run:", updatedData);
                        return updatedData;
                    });
                    
                    console.debug("Added new run to state:", newJobRun);
                } else {
                    console.debug("Check already running or no run details provided. Started:", result.started, "RunDetails:", result.runDetails);
                }
            } else {
                console.error('Failed to trigger integrity check:', response.statusText);
            }
        } catch (error) {
            console.error('Error triggering integrity check:', error);
        }
        setIsFetching(false);
    }, [setIsFetching, setLiveData]);


    return (
        <div className="d-flex align-items-center gap-3">
            {!connected && (
                <div className="text-muted small">
                    <em>Connecting to status updates...</em>
                </div>
            )}
            <Button variant={runButtonVariant} onClick={() => setShowModal(true)} disabled={!isRunButtonEnabled}>
                {runButtonLabel}
            </Button>
            
            <IntegrityCheckModal
                show={showModal}
                onHide={() => setShowModal(false)}
                onRun={onRunCheckWithParameters}
            />
        </div>
    );
}

// Helper function to format UTC timestamps for local display  
function formatTimestampForDisplay(dateString: string): string {
    // Backend sends UTC timestamps with "O" format (ISO 8601), convert to local time for display
    const date = new Date(dateString);
    return date.toLocaleString();
}

// Helper function to calculate and format duration
function formatDuration(startTime?: string, endTime?: string): string {
    if (!startTime || !endTime) {
        return "Unknown";
    }
    
    const start = new Date(startTime);
    const end = new Date(endTime);
    
    // Check if dates are valid
    if (isNaN(start.getTime()) || isNaN(end.getTime())) {
        return "Invalid";
    }
    
    const durationMs = end.getTime() - start.getTime();
    
    if (durationMs < 0) {
        return "Invalid";
    }
    
    const seconds = Math.floor(durationMs / 1000);
    const minutes = Math.floor(seconds / 60);
    const hours = Math.floor(minutes / 60);
    
    if (hours > 0) {
        return `${hours}h ${minutes % 60}m ${seconds % 60}s`;
    } else if (minutes > 0) {
        return `${minutes}m ${seconds % 60}s`;
    } else {
        return `${seconds}s`;
    }
}


export async function loader() {
    try {
        const { backendClient } = await import("~/clients/backend-client.server");
        const data = await backendClient.getIntegrityResults();
        return { data, error: null };
    } catch (error) {
        console.error("Failed to load integrity results:", error);
        return { data: null, error: `Failed to load integrity results: ${error instanceof Error ? error.message : String(error)}` };
    }
}

export default function IntegrityResults(props: Route.ComponentProps) {
    const { data, error } = props.loaderData;
    const [liveData, setLiveData] = useState(data);
    const [isCheckRunning, setIsCheckRunning] = useState(false);
    const [lastProgressUpdate, setLastProgressUpdate] = useState<string | null>(null);
    const [isCancelling, setIsCancelling] = useState(false);
    const [currentRunId, setCurrentRunId] = useState<string | null>(null);
    const [noFilesAlert, setNoFilesAlert] = useState<string | null>(null);
    
    // Search and filter state
    const [searchQuery, setSearchQuery] = useState("");
    const [debouncedSearchQuery, setDebouncedSearchQuery] = useState("");
    const [timeFilter, setTimeFilter] = useState("< 24 hours");

    // Function to refresh integrity data
    const refreshIntegrityData = useCallback(async () => {
        try {
            const response = await fetch("/api/integrity-results", {
                method: "GET",
                headers: {
                    "Content-Type": "application/json;charset=UTF-8"
                }
            });
            
            if (response.ok) {
                const freshData = await response.json();
                setLiveData(freshData);
            }
        } catch (error) {
            console.error("Failed to refresh integrity data:", error);
        }
    }, []);

    // Function to fetch single run status and update live data
    const refreshSingleRunData = useCallback(async (runId: string) => {
        try {
            const response = await fetch(`/api/media-integrity/run/${runId}/status`, {
                method: "GET",
                headers: {
                    "Content-Type": "application/json;charset=UTF-8"
                }
            });

            if (response.ok) {
                const runStatus = await response.json();
                
                // Update only the specific run's data in live data
                setLiveData((prevData: any) => {
                    if (!prevData) {
                        console.debug("refreshSingleRunData: No prevData available");
                        return prevData;
                    }
                    
                    const targetRun = prevData.jobRuns.find((run: any) => run.runId === runId);
                    if (!targetRun) {
                        console.debug(`refreshSingleRunData: Run ${runId} not found in existing runs. Creating new run.`);
                        // Create a new job run for scheduled runs that weren't initiated from this page
                        const newJobRun = {
                            date: runStatus.startTime || new Date().toISOString(),
                            runId: runId,
                            startTime: runStatus.startTime,
                            endTime: runStatus.endTime,
                            totalFiles: runStatus.totalFiles,
                            corruptFiles: runStatus.corruptFiles,
                            validFiles: runStatus.validFiles,
                            files: runStatus.files || [],
                            parameters: runStatus.parameters,
                            isRunning: runStatus.isRunning,
                            currentFile: runStatus.currentFile,
                            progressPercentage: runStatus.progressPercentage
                        };
                        
                        return {
                            ...prevData,
                            jobRuns: [newJobRun, ...prevData.jobRuns]
                        };
                    }
                    
                    console.debug(`refreshSingleRunData: Updating run ${runId} with new data:`, {
                        validFiles: runStatus.validFiles,
                        corruptFiles: runStatus.corruptFiles,
                        totalFiles: runStatus.totalFiles,
                        isRunning: runStatus.isRunning
                    });
                    
                    const updatedJobRuns = prevData.jobRuns.map((run: any) => {
                        if (run.runId === runId) {
                            return {
                                ...run,
                                validFiles: runStatus.validFiles,
                                corruptFiles: runStatus.corruptFiles,
                                totalFiles: runStatus.totalFiles,
                                files: runStatus.files || [],
                                startTime: runStatus.startTime,
                                endTime: runStatus.endTime,
                                parameters: runStatus.parameters,
                                isRunning: runStatus.isRunning,
                                currentFile: runStatus.currentFile,
                                progressPercentage: runStatus.progressPercentage
                            };
                        }
                        return run;
                    });
                    
                    return {
                        ...prevData,
                        jobRuns: updatedJobRuns
                    };
                });
            }
        } catch (error) {
            console.error("Error refreshing single run data:", error);
        }
    }, []);

    // Function to cancel integrity check
    const cancelIntegrityCheck = useCallback(async () => {
        setIsCancelling(true);
        try {
            const response = await fetch("/api/media-integrity/cancel", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json;charset=UTF-8"
                }
            });
            
            if (response.ok) {
                const result = await response.json();
                console.log("Cancel result:", result);
            } else {
                console.error("Failed to cancel integrity check:", response.statusText);
            }
        } catch (error) {
            console.error("Error cancelling integrity check:", error);
        } finally {
            setIsCancelling(false);
        }
    }, []);

    // WebSocket for real-time updates
    useEffect(() => {
        let ws: WebSocket;
        let disposed = false;
        let refreshInterval: NodeJS.Timeout | null = null;
        
        function connect() {
            ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
            
            ws.onmessage = receiveMessage((_, message) => {
                // Parse message and run ID (format: "message:runId")
                const parts = message ? message.split(':') : [];
                const messageText = parts[0] || message || '';
                const runId = parts.length > 1 ? parts[parts.length - 1] : null; // Take last part as run ID
                
                setLastProgressUpdate(messageText);
                
                // Handle "no files eligible" message
                if (messageText === "no_files") {
                    setNoFilesAlert("No files are currently eligible for integrity checking. All files have been checked recently according to your settings.");
                    // Auto-hide the alert after 10 seconds
                    setTimeout(() => setNoFilesAlert(null), 10000);
                    setIsCheckRunning(false);
                    setCurrentRunId(null);
                    return;
                }
                
                // Determine if check is running based on progress message
                const isRunning = !!messageText && 
                    !messageText.startsWith("complete") && 
                    !messageText.startsWith("failed") && 
                    !messageText.startsWith("cancelled") &&
                    messageText !== "no_files";
                    
                setIsCheckRunning(isRunning);
                
                // Update current run ID when check starts or continues
                if (runId && isRunning) {
                    setCurrentRunId(runId);
                    console.debug("Active run ID set to:", runId);
                    
                    // Refresh single run data to get updated progress during active runs
                    console.debug("WebSocket progress: Calling refreshSingleRunData for:", runId);
                    refreshSingleRunData(runId);
                }
                
                // Reset states when check completes
                if (!isRunning) {
                    setIsCancelling(false);
                    setCurrentRunId(null);
                    console.debug("Check completed, clearing run ID");
                }
                
                // Note: "starting" message no longer needed since POST response now includes run details
                
                // When check completes, do final refresh to get the completed state
                if (messageText && (messageText.startsWith("complete") || messageText.startsWith("failed") || messageText.startsWith("cancelled"))) {
                    // Final refresh to update the UI with completion status
                    // This ensures the run shows as completed even if smart polling has stopped
                    setTimeout(() => {
                        refreshIntegrityData();
                    }, 1000); // Shorter delay since completion is typically quick
                }
            });
            
            ws.onopen = () => { 
                ws.send(JSON.stringify(integrityProgressTopic)); 
            };
            
            ws.onclose = () => { 
                if (!disposed) setTimeout(() => connect(), 1000); 
            };
            
            ws.onerror = () => { ws.close() };
        }
        
        connect();
        return () => { 
            disposed = true; 
            ws.close(); 
            if (refreshInterval) {
                clearInterval(refreshInterval);
            }
        };
    }, [refreshIntegrityData, refreshSingleRunData]);

    // Update live data when props data changes
    useEffect(() => {
        setLiveData(data);
    }, [data]);

    // Debounce search query
    useEffect(() => {
        const timer = setTimeout(() => {
            setDebouncedSearchQuery(searchQuery);
        }, 300); // 300ms delay

        return () => clearTimeout(timer);
    }, [searchQuery]);

    // Helper function to get time filter cutoff
    const getTimeFilterCutoff = useCallback(() => {
        const now = new Date();
        switch (timeFilter) {
            case "< 2 hours":
                return new Date(now.getTime() - 2 * 60 * 60 * 1000);
            case "< 24 hours":
                return new Date(now.getTime() - 24 * 60 * 60 * 1000);
            case "< 3 days":
                return new Date(now.getTime() - 3 * 24 * 60 * 60 * 1000);
            case "< 7 days":
                return new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);
            default:
                return new Date(now.getTime() - 24 * 60 * 60 * 1000);
        }
    }, [timeFilter]);

    // Filter and search logic
    const filteredData = useMemo(() => {
        if (!liveData || !liveData.jobRuns) return liveData;

        const cutoffTime = getTimeFilterCutoff();
        
        // Filter job runs by time
        const timeFilteredRuns = liveData.jobRuns.filter((run: IntegrityJobRun) => {
            const runTime = run.startTime ? new Date(run.startTime) : new Date(run.date);
            return runTime >= cutoffTime;
        });

        // If no search query, return time-filtered runs
        if (!debouncedSearchQuery.trim()) {
            return {
                ...liveData,
                jobRuns: timeFilteredRuns
            };
        }

        // Apply search filter to files within each run
        const searchLower = debouncedSearchQuery.toLowerCase();
        const searchFilteredRuns = timeFilteredRuns.map((run: IntegrityJobRun) => {
            const filteredFiles = run.files.filter((file: IntegrityFileResult) => 
                file.fileName.toLowerCase().includes(searchLower) ||
                file.filePath.toLowerCase().includes(searchLower)
            );
            
            return {
                ...run,
                files: filteredFiles,
                totalFiles: filteredFiles.length,
                validFiles: filteredFiles.filter((f: IntegrityFileResult) => f.status === 'valid').length,
                corruptFiles: filteredFiles.filter((f: IntegrityFileResult) => f.status === 'corrupt').length
            };
        }).filter((run: IntegrityJobRun) => run.files.length > 0); // Only show runs that have matching files

        return {
            ...liveData,
            jobRuns: searchFilteredRuns
        };
    }, [liveData, debouncedSearchQuery, timeFilter, getTimeFilterCutoff]);

    if (error) {
        return (
            <div className="container mt-4">
                <Alert variant="danger">
                    <Alert.Heading>Error Loading Integrity Results</Alert.Heading>
                    <p>{error}</p>
                </Alert>
            </div>
        );
    }

    return (
        <div className="container mt-4">
            <div className="d-flex justify-content-between align-items-center mb-4">
                <h1>Media Integrity Results</h1>
                <IntegrityCheckButton setLiveData={setLiveData} />
            </div>
            
            {/* No Files Alert */}
            {noFilesAlert && (
                <Alert variant="info" dismissible onClose={() => setNoFilesAlert(null)}>
                    <Alert.Heading>No Files Eligible for Checking</Alert.Heading>
                    <p>{noFilesAlert}</p>
                </Alert>
            )}
            
            {/* Search and Filter Controls - Always show */}
            <div className="row mb-4">
                <div className="col-md-8">
                    <div className="input-group">
                        <span className="input-group-text">
                            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                                <path d="M11.742 10.344a6.5 6.5 0 1 0-1.397 1.398h-.001l3.85 3.85a1 1 0 0 0 1.415-1.414l-3.85-3.85zm-5.442 1.5a5.5 5.5 0 1 1 0-11 5.5 5.5 0 0 1 0 11"/>
                            </svg>
                        </span>
                        <input
                            type="text"
                            className="form-control"
                            placeholder="Search by file name or path..."
                            value={searchQuery}
                            onChange={(e) => setSearchQuery(e.target.value)}
                            onKeyDown={(e) => {
                                if (e.key === 'Escape') {
                                    setSearchQuery("");
                                }
                            }}
                        />
                        {searchQuery !== debouncedSearchQuery && (
                            <span className="input-group-text">
                                <div className="spinner-border spinner-border-sm" role="status" style={{ width: "12px", height: "12px" }}>
                                    <span className="visually-hidden">Searching...</span>
                                </div>
                            </span>
                        )}
                        {searchQuery && (
                            <button
                                className="btn btn-outline-secondary"
                                type="button"
                                onClick={() => setSearchQuery("")}
                                title="Clear search"
                            >
                                ✕
                            </button>
                        )}
                    </div>
                </div>
                <div className="col-md-4">
                    <select
                        className="form-select"
                        value={timeFilter}
                        onChange={(e) => setTimeFilter(e.target.value)}
                    >
                        <option value="< 2 hours">Last 2 hours</option>
                        <option value="< 24 hours">Last 24 hours</option>
                        <option value="< 3 days">Last 3 days</option>
                        <option value="< 7 days">Last 7 days</option>
                    </select>
                </div>
            </div>
            
            {!liveData || !liveData.jobRuns ? (
                <Alert variant="info">
                    <Alert.Heading>No Integrity Check Data</Alert.Heading>
                    <p>No integrity checks have been run yet. Enable integrity checking in Settings to start monitoring your media files.</p>
                </Alert>
            ) : filteredData?.jobRuns.length === 0 ? (
                <Alert variant="info">
                    <Alert.Heading>No Results Found</Alert.Heading>
                    <p>
                        {debouncedSearchQuery ? 
                            `No files matching "${debouncedSearchQuery}" found in the selected time range.` :
                            `No integrity checks found in the selected time range (${timeFilter}).`
                        }
                    </p>
                    {(debouncedSearchQuery || timeFilter !== "< 24 hours") && (
                        <div className="mt-2">
                            <button 
                                className="btn btn-outline-primary btn-sm me-2"
                                onClick={() => setSearchQuery("")}
                            >
                                Clear Search
                            </button>
                            <button 
                                className="btn btn-outline-primary btn-sm"
                                onClick={() => setTimeFilter("< 7 days")}
                            >
                                Expand Time Range
                            </button>
                        </div>
                    )}
                </Alert>
            ) : (
                <div className="mb-4">
                    <div className="d-flex justify-content-between align-items-center mb-3">
                        <h3>Integrity Check Results by Execution</h3>
                        {(debouncedSearchQuery || filteredData?.jobRuns.length !== liveData?.jobRuns.length) && (
                            <small className="text-muted">
                                Showing {filteredData?.jobRuns.length} of {liveData?.jobRuns.length} executions
                                {debouncedSearchQuery && ` matching "${debouncedSearchQuery}"`}
                            </small>
                        )}
                    </div>
                    <JobRunsList 
                        jobRuns={filteredData.jobRuns} 
                        isCheckRunning={isCheckRunning} 
                        cancelIntegrityCheck={cancelIntegrityCheck}
                        isCancelling={isCancelling}
                        currentRunId={currentRunId}
                        setLiveData={setLiveData}
                    />
                </div>
            )}
        </div>
    );
}

// Component to display run parameters
function RunParametersDisplay({ parameters }: { parameters?: IntegrityCheckRunParameters }) {
    const [isExpanded, setIsExpanded] = useState(false);
    
    if (!parameters) {
        return null;
    }

    return (
        <div className="mt-2">
            <div 
                className="d-flex align-items-center text-muted small"
                style={{ cursor: 'pointer' }}
                onClick={(e) => {
                    e.stopPropagation(); // Prevent card expansion
                    setIsExpanded(!isExpanded);
                }}
            >
                <strong>Run Parameters</strong>
                <svg 
                    xmlns="http://www.w3.org/2000/svg" 
                    width="12" 
                    height="12" 
                    fill="currentColor" 
                    className={`bi bi-chevron-down ms-1 ${isExpanded ? 'rotate-180' : ''}`}
                    style={{ transition: 'transform 0.2s ease' }}
                    viewBox="0 0 16 16"
                >
                    <path fillRule="evenodd" d="M1.646 4.646a.5.5 0 0 1 .708 0L8 10.293l5.646-5.647a.5.5 0 0 1 .708.708l-6 6a.5.5 0 0 1-.708 0l-6-6a.5.5 0 0 1 0-.708"/>
                </svg>
            </div>
            
            <Collapse in={isExpanded}>
                <div className="ms-2 mt-1">
                    <small className="text-muted">
                        {parameters.scanDirectory && (
                            <div>📁 Scan Directory: <code>{parameters.scanDirectory}</code></div>
                        )}
                        <div>📊 Max Files: {parameters.maxFilesToCheck}</div>
                        <div>⚡ Action: {parameters.corruptFileAction}</div>
                        <div>🔍 Type: {parameters.runType}</div>
                        {parameters.mp4DeepScan && <div>🎬 MP4 Deep Scan: Enabled</div>}
                        {parameters.autoMonitor && <div>👁️ Auto Monitor: Enabled</div>}
                        {parameters.unmonitorValidatedFiles && <div>📤 Unmonitor Validated: Enabled</div>}
                        {parameters.directDeletionFallback && <div>🗑️ Direct Deletion Fallback: Enabled</div>}
                        <div>🔬 NZB Sample: {parameters.nzbSegmentSamplePercentage}%</div>
                        <div>📊 NZB Threshold: {parameters.nzbSegmentThresholdPercentage}%</div>
                    </small>
                </div>
            </Collapse>
        </div>
    );
}

function JobRunsList({ 
    jobRuns, 
    isCheckRunning, 
    cancelIntegrityCheck, 
    isCancelling,
    currentRunId}: { 
    jobRuns: IntegrityJobRun[]; 
    isCheckRunning?: boolean; 
    cancelIntegrityCheck: () => Promise<void>;
    isCancelling: boolean;
    currentRunId: string | null;
    setLiveData: React.Dispatch<React.SetStateAction<any>>;
}) {
    const [expandedRuns, setExpandedRuns] = useState<Set<string>>(new Set());

    const toggleRun = (date: string) => {
        const newExpanded = new Set(expandedRuns);
        const wasExpanded = newExpanded.has(date);
        
        if (wasExpanded) {
            newExpanded.delete(date);
        } else {
            newExpanded.add(date);
        }
        setExpandedRuns(newExpanded);
    };

    // Find the run that's most likely active using run ID from WebSocket
    const findActiveRun = () => {
        if (!isCheckRunning || jobRuns.length === 0) {
            console.debug("No active run - isCheckRunning:", isCheckRunning, "jobRuns.length:", jobRuns.length);
            return null;
        }
        
        // If we have a current run ID from WebSocket, ONLY use exact matching
        if (currentRunId) {
            const exactMatch = jobRuns.find(run => run.runId === currentRunId);
            if (exactMatch) {
                console.debug("Found exact run ID match:", currentRunId);
                return exactMatch;
            }
            // If we have a run ID but no match yet, don't show any run as active
            console.debug("Current run ID not found in data yet, showing no active run:", currentRunId);
            return null;
        }
        
        console.debug("No active run found despite check running. Runs:", jobRuns.map(r => ({ 
            runId: r.runId, 
            startTime: r.startTime, 
            endTime: r.endTime,
            date: r.date 
        })));
        
        return null;
    };

    const activeRun = findActiveRun();

    return (
        <div>
            {jobRuns.map((run) => {
                const isActiveRun = isCheckRunning && activeRun && run.runId === activeRun.runId;
                
                return (
                    <Card key={run.date} className={`mb-3 ${isActiveRun ? 'border-primary' : ''}`}>
                        <Card.Header 
                            className={`${isActiveRun ? 'bg-primary bg-opacity-10' : ''} position-relative`}
                            style={{ cursor: 'pointer' }}
                            onClick={() => toggleRun(run.date)}
                        >
                            <div className="d-flex justify-content-between align-items-start">
                                <div className="flex-grow-1" style={{ paddingRight: '120px' }}>
                                    <div className="d-flex align-items-center mb-1">
                                        {isActiveRun && (
                                            <span className="me-2 badge bg-primary">
                                                🔍 Active
                                            </span>
                                        )}
                                        {isActiveRun && (
                                            <div className="spinner-border spinner-border-sm me-2" role="status">
                                                <span className="visually-hidden">Loading...</span>
                                            </div>
                                        )}
                                        <Badge bg="success" className="me-2">
                                            {run.validFiles} valid
                                        </Badge>
                                        {run.corruptFiles > 0 && (
                                            <Badge bg="danger" className="me-2">
                                                {run.corruptFiles} corrupt
                                            </Badge>
                                        )}
                                        <strong className="h6 mb-0">
                                            {run.parameters?.runType === 'manual' ? 'Manual Run' : 'Scheduled Run'}: {run.parameters?.scanDirectory}
                                        </strong>
                                    </div>
                                    
                                    <div className="text-muted small">
                                        {run.startTime ? (
                                            <div>
                                                <strong>Started:</strong> {formatTimestampForDisplay(run.startTime)}
                                                {run.endTime && (
                                                    <>
                                                        {" | "}
                                                        <strong>Completed:</strong> {formatTimestampForDisplay(run.endTime)}
                                                        {" | "}
                                                        <strong>Duration:</strong> {formatDuration(run.startTime, run.endTime)}
                                                    </>
                                                )}
                                                {isActiveRun && !run.endTime && (
                                                    <>
                                                        {" | "}
                                                        <span className="text-primary">
                                                            <strong>Running...</strong> (Duration: {formatDuration(run.startTime, new Date().toISOString())})
                                                        </span>
                                                    </>
                                                )}
                                            </div>
                                        ) : (
                                            <div>
                                                <strong>Date:</strong> {formatTimestampForDisplay(run.date)}
                                            </div>
                                        )}
                                    </div>
                                    
                                    <RunParametersDisplay parameters={run.parameters} />
                                    
                                    <div className="mt-2">
                                        <span className="fw-bold">
                                            {isActiveRun ? 
                                                `${run.validFiles + run.corruptFiles}/${run.totalFiles} files checked` : 
                                                `${run.totalFiles} files checked`
                                            }
                                            {isActiveRun && <span className="text-muted"> (updating...)</span>}
                                        </span>
                                    </div>
                                </div>
                                
                                {/* Cancel button and chevron icon in absolute top right corner */}
                                <div className="position-absolute top-0 end-0 p-3 d-flex align-items-center gap-2">
                                    {/* Cancel button positioned to the left of chevron */}
                                    {isActiveRun && isCheckRunning && (
                                        <Button
                                            variant="outline-danger"
                                            size="sm"
                                            onClick={(e) => {
                                                e.stopPropagation(); // Prevent header click
                                                cancelIntegrityCheck();
                                            }}
                                            disabled={isCancelling}
                                        >
                                            {isCancelling ? (
                                                <>
                                                    <div className="spinner-border spinner-border-sm me-1" role="status">
                                                        <span className="visually-hidden">Cancelling...</span>
                                                    </div>
                                                    Cancelling...
                                                </>
                                            ) : (
                                                <>✕ Cancel</>
                                            )}
                                        </Button>
                                    )}
                                    
                                    <svg 
                                        xmlns="http://www.w3.org/2000/svg" 
                                        width="16" 
                                        height="16" 
                                        fill="currentColor" 
                                        className={`bi bi-chevron-down text-secondary ${expandedRuns.has(run.date) ? 'rotate-180' : ''}`}
                                        style={{ transition: 'transform 0.2s ease' }}
                                        viewBox="0 0 16 16"
                                    >
                                        <path fillRule="evenodd" d="M1.646 4.646a.5.5 0 0 1 .708 0L8 10.293l5.646-5.647a.5.5 0 0 1 .708.708l-6 6a.5.5 0 0 1-.708 0l-6-6a.5.5 0 0 1 0-.708"/>
                                    </svg>
                                </div>
                            </div>
                        </Card.Header>
                    <Collapse in={expandedRuns.has(run.date)}>
                        <Card.Body>
                            <FilesTable files={run.files} />
                        </Card.Body>
                    </Collapse>
                </Card>
                );
            })}
        </div>
    );
}

function FilesTable({ files }: { files: IntegrityFileResult[] }) {
    if (files.length === 0) {
        return <p>No files found.</p>;
    }

    return (
        <Table striped bordered hover responsive>
            <thead>
                <tr>
                    <th>Status</th>
                    <th>File Name</th>
                    <th>File Path</th>
                    <th>Last Checked</th>
                    <th>Error</th>
                    <th>Action Taken</th>
                </tr>
            </thead>
            <tbody>
                {files.map((file) => (
                    <tr key={file.fileId}>
                        <td>
                            <StatusBadge status={file.status} />
                        </td>
                        <td>{file.fileName}</td>
                        <td>
                            <code className="text-muted">{file.filePath}</code>
                        </td>
                        <td>
                            {formatTimestampForDisplay(file.lastChecked)}
                        </td>
                        <td>
                            {file.errorMessage ? (
                                <span className="text-danger" title={file.errorMessage}>
                                    {file.errorMessage.length > 50 
                                        ? `${file.errorMessage.substring(0, 50)}...` 
                                        : file.errorMessage}
                                </span>
                            ) : (
                                <span className="text-muted">-</span>
                            )}
                        </td>
                        <td>
                            {file.actionTaken ? (
                                <span className="text-info" title={file.actionTaken}>
                                    {file.actionTaken.length > 30 
                                        ? `${file.actionTaken.substring(0, 30)}...` 
                                        : file.actionTaken}
                                </span>
                            ) : (
                                <span className="text-muted">-</span>
                            )}
                        </td>
                    </tr>
                ))}
            </tbody>
        </Table>
    );
}

function StatusBadge({ status }: { status: "unknown" | "valid" | "corrupt" }) {
    let variant: string;
    let icon: string;
    
    switch (status) {
        case "valid":
            variant = "success";
            icon = "✓";
            break;
        case "corrupt":
            variant = "danger";
            icon = "✗";
            break;
        case "unknown":
        default:
            variant = "warning";
            icon = "?";
    }

    return (
        <Badge bg={variant}>
            {icon} {status}
        </Badge>
    );
}
