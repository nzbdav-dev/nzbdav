import { useCallback, useEffect, useRef, useState } from "react";

// --- API shapes (camelCase, matching the backend's default JSON policy) -------

export type SessionStatus =
    | "idle"
    | "connected"
    | "mapped"
    | "scanning"
    | "scan_cancelling"
    | "scanned"
    | "running"
    | "paused"
    | "cancelling"
    | "complete"
    | "cancelled"
    // Step 6 uses transient linking/applying states and rests at linked.
    | "linking"
    | "linked"
    | "applying"
    | "restoring";

export function isMigrationWorkActive(status: SessionStatus | undefined): boolean {
    return status === "scanning" || status === "scan_cancelling"
        || status === "running" || status === "paused" || status === "cancelling"
        || status === "linking" || status === "applying" || status === "restoring";
}

export function canConnectMigration(status: SessionStatus | undefined): boolean {
    return status === "idle" || status === "connected" || status === "mapped" || status === "scanned"
        || status === "complete" || status === "cancelled" || status === "linked";
}

export function canStartScanMigration(status: SessionStatus | undefined): boolean {
    return status === "connected" || status === "mapped" || status === "scanned"
        || status === "complete" || status === "cancelled" || status === "linked";
}

export function canResetMigration(status: SessionStatus | undefined, busy: string | null): boolean {
    return status !== undefined && !isMigrationWorkActive(status) && busy === null;
}

export function canEditCategoryMappings(status: SessionStatus | undefined): boolean {
    return status === "connected" || status === "mapped" || status === "scanned";
}

export function canEditReleaseSelection(status: SessionStatus | undefined): boolean {
    return status === "scanned";
}

export type CategoryMapRow = {
    altmountCategory: string;
    altmountDir?: string | null;
    altmountType?: string | null;
    targetCategory?: string | null;
    action: string;
    discoveredBy: string;
};

export type StatusResponse = {
    sessionStatus: SessionStatus;
    roots: {
        altmountMetadataRoot?: string | null;
        altmountConfigPath?: string | null;
        altmountStoreRoot?: string | null;
    };
    symlinks?: {
        symlinkLibraryRoot?: string | null;
        symlinkBackupDir?: string | null;
    };
    maxQueueDepth: number;
    submitWorkers: number;
    timestamps: {
        scanStartedAt?: string | null;
        scanCompletedAt?: string | null;
        runStartedAt?: string | null;
        runCompletedAt?: string | null;
    };
    submissions: Record<string, number>;
    submissionIssues?: SubmissionIssue[];
    historyCleanup: {
        eligible: number;
        cleared: number;
        pending: number;
    };
};

export type SubmissionIssue = {
    storeRef: string;
    submitFileName: string;
    state: "failed" | "evicted";
    reason: string;
};

export type HistoryCleanupSummary = {
    eligible: number;
    removed: number;
    alreadyAbsent: number;
    skipped: number;
    failed: number;
};

export type MigrationDataSummary = {
    runs: number;
    releases: number;
    files: number;
};

export type SummaryResponse = {
    sessionStatus: SessionStatus;
    counts: {
        total: number;
        green: number;
        amber: number;
        red: number;
        submittable: number;
        noStoreRef: number;
        alreadyMigrated: number;
    };
    cost: { estFetchBytesLazy: number; estFetchBytesEager: number };
    warnings: { blockingCollisions: number; unmapped: number; scanErrors: number };
    canRun: boolean;
};

export type ReleaseRow = {
    storeRef: string;
    submitFileName: string;
    jobName: string;
    jobNameDiverges: boolean;
    altmountCategory?: string | null;
    targetCategory?: string | null;
    verdict?: "green" | "amber" | "red" | null;
    verdictReasons: string[];
    metaFileCount: number;
    totalBytes?: number | null;
    estFetchBytesLazy: number;
    estFetchBytesEager: number;
    isRarRelease: boolean;
    hasPassword: boolean;
    encryption?: string | null;
    worstFileStatus?: string | null;
    included: boolean;
    collisionGroupKey?: string | null;
};

export type ReleasesResponse = {
    total: number;
    page: number;
    pageSize: number;
    releases: ReleaseRow[];
};

export type CollisionMember = { storeRef: string; submitFileName: string; verdict: string; reasons: string[] };
export type CollisionGroup = { key: string; blocking: boolean; members: CollisionMember[] };

export type SymlinkRow = {
    id: number;
    symlinkPath: string;
    oldTarget: string;
    newTarget?: string | null;
    status: string;
    matchMethod?: string | null;
    storeRef?: string | null;
    error?: string | null;
};

export type SymlinkListResponse = {
    total: number;
    page: number;
    pageSize: number;
    counts: Record<string, number>;
    rows: SymlinkRow[];
};

export type SymlinkFilters = {
    page: number;
    pageSize: number;
    status: string;
    q: string;
    sort: string;
};

export type SymlinkPlanForm = { libraryRoot: string; backupDir: string };

export type SymlinkBackupInfo = {
    fileName: string;
    createdAt: string;
    sizeBytes: number;
    entryCount: number;
    legacyEntryCount: number;
    isValid: boolean;
    error?: string | null;
};

export type SymlinkRestoreIssue = { path: string; reason: string };

export type SymlinkRestoreSummary = {
    fileName: string;
    total: number;
    restored: number;
    alreadyRestored: number;
    failed: number;
    requeued: number;
    issues: SymlinkRestoreIssue[];
};

export type ConnectForm = {
    metadataRoot: string;
    configPath: string;
    storeRoot: string;
    maxQueueDepth: number;
    submitWorkers: number;
};

export type ReleaseFilters = {
    page: number;
    pageSize: number;
    verdict: string;
    included: string; // "", "true", "false"
    q: string;
    sort: string;
};

const BASE = "/api/altmount-migration";

type FetchInit = Parameters<typeof fetch>[1];

async function apiJson<T>(url: string, init?: FetchInit): Promise<T> {
    const res = await fetch(url, { cache: "no-store", ...init });
    const body = (await res.json().catch(() => null)) as (T & { error?: string }) | null;
    if (!res.ok) {
        const message = body?.error || `Request failed (${res.status})`;
        const err = new Error(message) as Error & { status?: number };
        err.status = res.status;
        throw err;
    }
    return body as T;
}

function jsonInit(method: string, payload: unknown): FetchInit {
    return {
        method,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(payload),
    };
}

export async function requestSymlinkApply(acknowledgeUnreadable = false): Promise<void> {
    await apiJson(
        `${BASE}/symlinks/apply`,
        jsonInit("POST", { confirm: true, acknowledgeUnreadable }),
    );
}

export async function runUiMutation(
    fn: () => Promise<void>,
    recordError: (message: string) => void,
): Promise<boolean> {
    try {
        await fn();
        return true;
    } catch (e) {
        recordError(e instanceof Error ? e.message : String(e));
        return false;
    }
}

export async function loadTableRetainingLastGood<T>(
    load: () => Promise<T>,
    commit: (data: T) => void,
    recordError: (message: string) => void,
): Promise<boolean> {
    try {
        commit(await load());
        return true;
    } catch (e) {
        recordError(e instanceof Error ? e.message : String(e));
        return false;
    }
}

export function beginLatestRequest(generation: { current: number }): () => boolean {
    const requestGeneration = ++generation.current;
    return () => requestGeneration === generation.current;
}

export function useAltmountMigration() {
    const [status, setStatus] = useState<StatusResponse | null>(null);
    const [summary, setSummary] = useState<SummaryResponse | null>(null);
    const [categories, setCategories] = useState<CategoryMapRow[]>([]);
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState<string | null>(null);
    const [historyCleanupResult, setHistoryCleanupResult] = useState<HistoryCleanupSummary | null>(null);
    const [symlinkRestoreResult, setSymlinkRestoreResult] = useState<SymlinkRestoreSummary | null>(null);
    const [migrationData, setMigrationData] = useState<MigrationDataSummary | null>(null);
    const mounted = useRef(true);
    const refreshGeneration = useRef(0);

    useEffect(() => {
        mounted.current = true;
        return () => {
            mounted.current = false;
        };
    }, []);

    const refresh = useCallback(async () => {
        const isLatest = beginLatestRequest(refreshGeneration);
        try {
            const [s, sum] = await Promise.all([
                apiJson<StatusResponse>(`${BASE}/status`),
                apiJson<SummaryResponse>(`${BASE}/summary`),
            ]);
            if (!mounted.current || !isLatest()) return;
            setStatus(s);
            setSummary(sum);
            setError(null);
        } catch (e) {
            if (mounted.current && isLatest())
                setError(e instanceof Error ? e.message : String(e));
        }
    }, []);

    const loadCategories = useCallback(async () => {
        try {
            const data = await apiJson<{ categories: CategoryMapRow[] }>(`${BASE}/categories`);
            if (mounted.current) setCategories(data.categories);
        } catch (e) {
            if (mounted.current) setError((e as Error).message);
        }
    }, []);

    // Initial load.
    useEffect(() => {
        void refresh();
        void loadCategories();
    }, [refresh, loadCategories]);

    // Poll while background work is in progress (scan, run, or Step 6 link/apply).
    const sessionStatus = status?.sessionStatus;
    useEffect(() => {
        if (!isMigrationWorkActive(sessionStatus)) return;
        const interval = window.setInterval(() => void refresh(), 2500);
        return () => window.clearInterval(interval);
    }, [sessionStatus, refresh]);

    const withBusy = useCallback(async (key: string, fn: () => Promise<void>) => {
        setBusy(key);
        setError(null);
        try {
            return await runUiMutation(fn, (message) => {
                if (mounted.current) setError(message);
            });
        } finally {
            if (mounted.current) setBusy(null);
        }
    }, []);

    const connect = useCallback((form: ConnectForm) => withBusy("connect", async () => {
        await apiJson(`${BASE}/connect`, jsonInit("POST", form));
        await Promise.all([refresh(), loadCategories()]);
    }), [withBusy, refresh, loadCategories]);

    const saveCategories = useCallback((mappings: { altmountCategory: string; targetCategory: string | null; action: string }[]) =>
        withBusy("categories", async () => {
            await apiJson(`${BASE}/categories`, jsonInit("PUT", { mappings }));
            await Promise.all([refresh(), loadCategories()]);
        }), [withBusy, refresh, loadCategories]);

    const startScan = useCallback(() => withBusy("scan", async () => {
        await apiJson(`${BASE}/scan`, { method: "POST" });
        await refresh();
    }), [withBusy, refresh]);

    const cancelScan = useCallback(() => withBusy("scan", async () => {
        await apiJson(`${BASE}/scan`, { method: "DELETE" });
        await refresh();
    }), [withBusy, refresh]);

    const loadReleases = useCallback(async (filters: ReleaseFilters): Promise<ReleasesResponse> => {
        const params = new URLSearchParams({
            page: String(filters.page),
            pageSize: String(filters.pageSize),
        });
        if (filters.verdict) params.set("verdict", filters.verdict);
        if (filters.included) params.set("included", filters.included);
        if (filters.q) params.set("q", filters.q);
        if (filters.sort) params.set("sort", filters.sort);
        return apiJson<ReleasesResponse>(`${BASE}/releases?${params.toString()}`);
    }, []);

    const setInclude = useCallback((storeRefs: string[], included: boolean) =>
        withBusy("include", async () => {
            await apiJson(`${BASE}/releases/include`, jsonInit("PUT", { storeRefs, included }));
            await refresh();
        }), [withBusy, refresh]);

    const loadCollisions = useCallback(async (): Promise<CollisionGroup[]> => {
        const data = await apiJson<{ groups: CollisionGroup[] }>(`${BASE}/collisions`);
        return data.groups;
    }, []);

    const startRun = useCallback(() => withBusy("run", async () => {
        await apiJson(`${BASE}/run`, { method: "POST" });
        await refresh();
    }), [withBusy, refresh]);

    const resumeRun = useCallback(() => withBusy("run", async () => {
        await apiJson(`${BASE}/run/resume`, { method: "POST" });
        await refresh();
    }), [withBusy, refresh]);

    const pauseRun = useCallback(() => withBusy("run", async () => {
        await apiJson(`${BASE}/run`, { method: "DELETE" });
        await refresh();
    }), [withBusy, refresh]);

    const cancelRun = useCallback(() => withBusy("run", async () => {
        await apiJson(`${BASE}/run?cancel=true`, { method: "DELETE" });
        await refresh();
    }), [withBusy, refresh]);

    const reset = useCallback(() => withBusy("reset", async () => {
        await apiJson(`${BASE}/reset`, { method: "POST" });
        await Promise.all([refresh(), loadCategories()]);
    }), [withBusy, refresh, loadCategories]);

    const loadMigrationData = useCallback(async () => {
        try {
            const data = await apiJson<{ summary: MigrationDataSummary }>(`${BASE}/migration-data`);
            if (mounted.current) setMigrationData(data.summary);
        } catch (e) {
            if (mounted.current) setError((e as Error).message);
        }
    }, []);

    const forgetMigrationData = useCallback(() => withBusy("forget-migration-data", async () => {
        await apiJson(`${BASE}/migration-data/forget`, jsonInit("POST", { confirm: true }));
        if (mounted.current) setMigrationData({ runs: 0, releases: 0, files: 0 });
        await Promise.all([refresh(), loadCategories()]);
    }), [withBusy, refresh, loadCategories]);

    const cleanupHistory = useCallback(() => withBusy("history-cleanup", async () => {
        const result = await apiJson<{ cleanup: HistoryCleanupSummary }>(
            `${BASE}/history/cleanup`,
            jsonInit("POST", { confirm: true }),
        );
        if (mounted.current) setHistoryCleanupResult(result.cleanup);
        await refresh();
    }), [withBusy, refresh]);

    // --- Step 6: symlink continuity ----------------------------------------

    const planSymlinks = useCallback((form: SymlinkPlanForm) => withBusy("symlink-plan", async () => {
        await apiJson(`${BASE}/symlinks/plan`, jsonInit("POST", form));
        await refresh();
    }), [withBusy, refresh]);

    const loadSymlinks = useCallback(async (f: SymlinkFilters): Promise<SymlinkListResponse> => {
        const params = new URLSearchParams({ page: String(f.page), pageSize: String(f.pageSize) });
        if (f.status) params.set("status", f.status);
        if (f.q) params.set("q", f.q);
        if (f.sort) params.set("sort", f.sort);
        return apiJson<SymlinkListResponse>(`${BASE}/symlinks?${params.toString()}`);
    }, []);

    const applySymlinks = useCallback((acknowledgeUnreadable = false) => withBusy("symlink-apply", async () => {
        await requestSymlinkApply(acknowledgeUnreadable);
        await refresh();
    }), [withBusy, refresh]);

    const loadSymlinkBackups = useCallback(async (): Promise<SymlinkBackupInfo[]> => {
        const data = await apiJson<{ backups: SymlinkBackupInfo[] }>(`${BASE}/symlinks/backups`);
        return data.backups;
    }, []);

    const restoreSymlinks = useCallback((fileName: string) => withBusy("symlink-restore", async () => {
        const data = await apiJson<{ restore: SymlinkRestoreSummary }>(
            `${BASE}/symlinks/restore`,
            jsonInit("POST", { fileName, confirm: true }),
        );
        if (mounted.current) setSymlinkRestoreResult(data.restore);
        await refresh();
    }), [withBusy, refresh]);

    const symlinkCsvHref = (f: Pick<SymlinkFilters, "status" | "q" | "sort">): string => {
        const params = new URLSearchParams({ format: "csv" });
        if (f.status) params.set("status", f.status);
        if (f.q) params.set("q", f.q);
        if (f.sort) params.set("sort", f.sort);
        return `${BASE}/symlinks?${params.toString()}`;
    };

    return {
        status,
        summary,
        categories,
        error,
        busy,
        historyCleanupResult,
        symlinkRestoreResult,
        migrationData,
        setError,
        refresh,
        connect,
        loadCategories,
        saveCategories,
        startScan,
        cancelScan,
        loadReleases,
        setInclude,
        loadCollisions,
        startRun,
        resumeRun,
        pauseRun,
        cancelRun,
        cleanupHistory,
        reset,
        loadMigrationData,
        forgetMigrationData,
        planSymlinks,
        loadSymlinks,
        applySymlinks,
        loadSymlinkBackups,
        restoreSymlinks,
        symlinkCsvHref,
    };
}
