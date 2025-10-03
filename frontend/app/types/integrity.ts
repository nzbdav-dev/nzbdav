export type IntegrityResultsData = {
  jobRuns: IntegrityJobRun[];
  allFiles: IntegrityFileResult[];
};

export type IntegrityJobRun = {
  date: string;
  runId?: string;
  startTime?: string;
  endTime?: string;
  totalFiles: number;
  corruptFiles: number;
  validFiles: number;
  files: IntegrityFileResult[];
  parameters?: IntegrityCheckRunParameters;
};

export type IntegrityFileResult = {
  fileId: string;
  filePath: string;
  fileName: string;
  isLibraryFile: boolean;
  lastChecked: string;
  status: "unknown" | "valid" | "corrupt";
  errorMessage?: string;
  actionTaken?:
    | "none"
    | "fileDeletedSuccessfully"
    | "fileDeletedViaArr"
    | "deleteFailedDirectFallback"
    | "deleteFailedNoFallback"
    | "deleteError";
  runId?: string;
};

export type IntegrityCheckRunParameters = {
  scanDirectory?: string;
  maxFilesToCheck: number;
  corruptFileAction: "log" | "delete" | "deleteViaArr";
  mp4DeepScan: boolean;
  autoMonitor: boolean;
  unmonitorValidatedFiles: boolean;
  directDeletionFallback: boolean;
  nzbSegmentSamplePercentage: number;
  nzbSegmentThresholdPercentage: number;
  runType: "manual" | "scheduled";
};
