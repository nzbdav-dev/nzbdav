import { describe, expect, it, vi } from "vitest";
import {
    beginLatestRequest,
    canConnectMigration,
    canEditCategoryMappings,
    canEditReleaseSelection,
    canResetMigration,
    canStartScanMigration,
    isMigrationWorkActive,
    loadTableRetainingLastGood,
    requestSymlinkApply,
    runUiMutation,
    type SessionStatus,
} from "./use-altmount-migration";

describe("requestSymlinkApply", () => {
    it.each([
        [undefined, false],
        [true, true],
    ] as const)("sends unreadable acknowledgement %s", async (input, expected) => {
        const originalFetch = globalThis.fetch;
        const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
            new Response(JSON.stringify({ status: true }), {
                status: 200,
                headers: { "content-type": "application/json" },
            }),
        );
        globalThis.fetch = fetchMock;
        try {
            await requestSymlinkApply(input);

            expect(fetchMock).toHaveBeenCalledOnce();
            const [url, init] = fetchMock.mock.calls[0];
            expect(url).toBe("/api/altmount-migration/symlinks/apply");
            expect(init?.method).toBe("POST");
            expect(JSON.parse(String(init?.body))).toEqual({
                confirm: true,
                acknowledgeUnreadable: expected,
            });
        } finally {
            globalThis.fetch = originalFetch;
        }
    });
});

describe("beginLatestRequest", () => {
    it("invalidates older request tickets when a newer refresh starts", () => {
        const generation = { current: 0 };
        const firstIsLatest = beginLatestRequest(generation);
        const secondIsLatest = beginLatestRequest(generation);

        expect(firstIsLatest()).toBe(false);
        expect(secondIsLatest()).toBe(true);
    });

    it("keeps the current request ticket valid until another request starts", () => {
        const generation = { current: 7 };
        const isLatest = beginLatestRequest(generation);

        expect(isLatest()).toBe(true);
        expect(isLatest()).toBe(true);
        expect(generation.current).toBe(8);
    });
});

describe("isMigrationWorkActive", () => {
    it.each<SessionStatus>(["scanning", "scan_cancelling", "running", "paused", "cancelling", "linking", "applying", "restoring"])(
        "blocks destructive wizard actions while status is %s",
        (status) => expect(isMigrationWorkActive(status)).toBe(true),
    );

    it.each<SessionStatus>(["idle", "connected", "mapped", "scanned", "complete", "cancelled", "linked"])(
        "allows destructive wizard actions after status is %s",
        (status) => expect(isMigrationWorkActive(status)).toBe(false),
    );

    it("treats an unloaded status as inactive", () => {
        expect(isMigrationWorkActive(undefined)).toBe(false);
    });
});

describe("canResetMigration", () => {
    it.each<SessionStatus>(["scanning", "scan_cancelling", "running", "paused", "cancelling", "linking", "applying", "restoring"])(
        "blocks Reset Wizard while status is %s",
        (status) => expect(canResetMigration(status, null)).toBe(false),
    );

    it("allows reset only after status has loaded and no mutation is busy", () => {
        expect(canResetMigration("scanned", null)).toBe(true);
        expect(canResetMigration(undefined, null)).toBe(false);
        expect(canResetMigration("scanned", "reset")).toBe(false);
    });
});

describe("review mutation state guards", () => {
    it.each<SessionStatus>(["idle", "connected", "mapped", "scanned", "complete", "cancelled", "linked"])(
        "allows Connect from resting state %s",
        (status) => expect(canConnectMigration(status)).toBe(true),
    );

    it.each<SessionStatus>(["scanning", "scan_cancelling", "running", "paused", "cancelling", "linking", "applying", "restoring"])(
        "blocks Connect during active state %s",
        (status) => expect(canConnectMigration(status)).toBe(false),
    );

    it.each<SessionStatus>(["connected", "mapped", "scanned", "complete", "cancelled", "linked"])(
        "allows Scan from resting configured state %s",
        (status) => expect(canStartScanMigration(status)).toBe(true),
    );

    it.each<SessionStatus>(["idle", "scanning", "scan_cancelling", "running", "paused", "cancelling", "linking", "applying", "restoring"])(
        "blocks Scan from illegal state %s",
        (status) => expect(canStartScanMigration(status)).toBe(false),
    );

    it.each<SessionStatus>(["connected", "mapped", "scanned"])(
        "allows category mapping edits while status is %s",
        (status) => expect(canEditCategoryMappings(status)).toBe(true),
    );

    it.each<SessionStatus>(["idle", "scanning", "scan_cancelling", "running", "paused", "cancelling", "complete", "cancelled", "linking", "linked", "applying", "restoring"])(
        "locks category mapping edits while status is %s",
        (status) => expect(canEditCategoryMappings(status)).toBe(false),
    );

    it("allows release selection edits only for a completed scan", () => {
        const statuses: (SessionStatus | undefined)[] = [
            undefined, "idle", "connected", "mapped", "scanning", "scan_cancelling", "running", "paused", "cancelling",
            "complete", "cancelled", "linking", "linked", "applying", "restoring",
        ];
        expect(canEditReleaseSelection("scanned")).toBe(true);
        statuses.forEach((status) => expect(canEditReleaseSelection(status)).toBe(false));
    });
});

describe("runUiMutation", () => {
    it("returns true without recording an error after a successful mutation", async () => {
        const recordError = vi.fn();

        await expect(runUiMutation(() => Promise.resolve(), recordError)).resolves.toBe(true);
        expect(recordError).not.toHaveBeenCalled();
    });

    it("records a rejected mutation and returns false", async () => {
        const recordError = vi.fn();

        await expect(runUiMutation(
            () => Promise.reject(new Error("API rejected the mutation")),
            recordError,
        )).resolves.toBe(false);
        expect(recordError).toHaveBeenCalledOnce();
        expect(recordError).toHaveBeenCalledWith("API rejected the mutation");
    });
});

describe("loadTableRetainingLastGood", () => {
    it("commits a successful response", async () => {
        const commit = vi.fn();
        const recordError = vi.fn();

        await expect(loadTableRetainingLastGood(
            () => Promise.resolve(["fresh"]),
            commit,
            recordError,
        )).resolves.toBe(true);
        expect(commit).toHaveBeenCalledWith(["fresh"]);
        expect(recordError).not.toHaveBeenCalled();
    });

    it("records a failure without replacing the last committed data", async () => {
        const commit = vi.fn();
        const recordError = vi.fn();

        await expect(loadTableRetainingLastGood(
            () => Promise.reject(new Error("table unavailable")),
            commit,
            recordError,
        )).resolves.toBe(false);
        expect(commit).not.toHaveBeenCalled();
        expect(recordError).toHaveBeenCalledWith("table unavailable");
    });
});
