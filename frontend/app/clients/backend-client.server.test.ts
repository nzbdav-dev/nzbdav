import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { backendClient, BackendUnavailableError } from "./backend-client.server";

const fetchMock = vi.fn<typeof fetch>();

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

beforeEach(() => {
  vi.stubGlobal("fetch", fetchMock);
  vi.stubEnv("BACKEND_URL", "http://backend");
  vi.stubEnv("FRONTEND_BACKEND_API_KEY", "test-api-key");
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
  vi.clearAllMocks();
});

describe("BackendClient", () => {
  it("gets onboarding status with the backend API key", async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ isOnboarding: true }));

    await expect(backendClient.isOnboarding()).resolves.toBe(true);
    expect(fetchMock).toHaveBeenCalledWith("http://backend/api/is-onboarding", {
      method: "GET",
      headers: {
        "Content-Type": "application/json",
        "x-api-key": "test-api-key",
      },
    });
  });

  it.each([
    ["createAccount", "create-account", "status", true],
    ["authenticate", "authenticate", "authenticated", true],
  ] as const)(
    "%s posts credentials as form data",
    async (method, endpoint, resultKey, result) => {
      fetchMock.mockResolvedValueOnce(jsonResponse({ [resultKey]: result }));

      await expect(backendClient[method]("alice", "secret")).resolves.toBe(result);
      const [url, init] = fetchMock.mock.calls[0];
      const form = init?.body as FormData;

      expect(url).toBe(`http://backend/api/${endpoint}`);
      expect(init?.method).toBe("POST");
      expect(init?.headers).toEqual({ "x-api-key": "test-api-key" });
      expect(Object.fromEntries(form.entries())).toEqual({
        username: "alice",
        password: "secret",
        type: "admin",
      });
    },
  );

  it("gets queue and history payloads", async () => {
    const queue = { slots: [], noofslots: 0 };
    const history = { slots: [], noofslots: 0 };
    fetchMock
      .mockResolvedValueOnce(jsonResponse({ queue }))
      .mockResolvedValueOnce(jsonResponse({ history }));

    await expect(backendClient.getQueue(25)).resolves.toEqual(queue);
    await expect(backendClient.getHistory(10)).resolves.toEqual(history);
    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      "http://backend/api?mode=queue&start=0&limit=25",
      "http://backend/api?mode=history&start=0&pageSize=10",
    ]);
  });

  it("gets, updates, and defaults config items", async () => {
    const configItems = [
      {
        configName: "one",
        configValue: "value",
        environmentVariableName: "NZBDAV_CONFIG__ONE",
      },
    ];
    fetchMock
      .mockResolvedValueOnce(jsonResponse({ configItems }))
      .mockResolvedValueOnce(jsonResponse({}))
      .mockResolvedValueOnce(jsonResponse({ status: true }));

    await expect(backendClient.getConfig(["one"])).resolves.toEqual(configItems);
    const getForm = fetchMock.mock.calls[0][1]?.body as FormData;
    expect(getForm.getAll("config-keys")).toEqual(["one"]);

    await expect(backendClient.getConfig(["missing"])).resolves.toEqual([]);

    await expect(backendClient.updateConfig(configItems)).resolves.toBe(true);
    const updateForm = fetchMock.mock.calls[2][1]?.body as FormData;
    expect(updateForm.get("one")).toBe("value");
  });

  it("lists WebDAV directories", async () => {
    const items = [{ name: "movie", isDirectory: true, size: null }];
    fetchMock.mockResolvedValueOnce(jsonResponse({ items }));

    await expect(backendClient.listWebdavDirectory("/view")).resolves.toEqual(items);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://backend/api/list-webdav-directory");
    expect((init?.body as FormData).get("directory")).toBe("/view");
  });

  it("adds an NZB using the configured manual category", async () => {
    fetchMock
      .mockResolvedValueOnce(jsonResponse({
        configItems: [{ configName: "api.manual-category", configValue: "movies & shows" }],
      }))
      .mockResolvedValueOnce(jsonResponse({ nzo_ids: ["nzo-1"] }));
    const file = new File(["nzb"], "movie.nzb");

    await expect(backendClient.addNzb(file)).resolves.toBe("nzo-1");
    const [url, init] = fetchMock.mock.calls[1];
    expect(url).toBe(
      "http://backend/api?mode=addfile&cat=movies+%26+shows&priority=0&pp=0",
    );
    expect((init?.body as FormData).get("nzbFile")).toBeInstanceOf(File);
  });

  it("gets health queue and history with optional page sizes", async () => {
    const queue = { uncheckedCount: 0, items: [] };
    const history = { stats: [], items: [] };
    fetchMock
      .mockResolvedValueOnce(jsonResponse(queue))
      .mockResolvedValueOnce(jsonResponse(history));

    await expect(backendClient.getHealthCheckQueue(30)).resolves.toEqual(queue);
    await expect(backendClient.getHealthCheckHistory()).resolves.toEqual(history);
    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      "http://backend/api/get-health-check-queue?pageSize=30",
      "http://backend/api/get-health-check-history",
    ]);
  });

  it("gets overview stats and filtered logs", async () => {
    const overview = { window: "7d", tiles: {} };
    const logs = { entries: [], hasMore: false };
    fetchMock
      .mockResolvedValueOnce(jsonResponse(overview))
      .mockResolvedValueOnce(jsonResponse(logs));

    await expect(backendClient.getOverviewStats("7d")).resolves.toEqual(overview);
    await expect(backendClient.getLogs({
      limit: 50,
      levels: ["Warning", "Error"],
      source: "Queue",
      search: "failed request",
      beforeSequence: 42,
    })).resolves.toEqual(logs);

    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      "http://backend/api/get-overview-stats?window=7d&sections=all",
      "http://backend/api/get-logs?limit=50&levels=Warning%2CError&source=Queue&search=failed+request&beforeSequence=42",
    ]);
  });

  it("searches indexers with the requested result limit", async () => {
    const results = { results: [], errors: [] };
    fetchMock.mockResolvedValueOnce(jsonResponse(results));

    await expect(backendClient.searchIndexers("Example Movie", 25)).resolves.toEqual(results);
    const [url, init] = fetchMock.mock.calls[0];
    const form = init?.body as FormData;

    expect(url).toBe("http://backend/api/search-indexers");
    expect(init?.method).toBe("POST");
    expect(form.get("q")).toBe("Example Movie");
    expect(form.get("limit")).toBe("25");
  });

  it("queries and mutates watchtower state", async () => {
    const data = { items: [], total: 0 };
    fetchMock
      .mockResolvedValueOnce(jsonResponse(data))
      .mockResolvedValueOnce(jsonResponse({ status: true }));

    await expect(backendClient.getWatchtower({
      state: "ready",
      q: "Example",
      sort: "updated",
      offset: 20,
      limit: 10,
      expander: "episodes",
      statsOnly: true,
    })).resolves.toEqual(data);
    await expect(backendClient.watchtowerMutate({
      action: "park",
      id: "wanted-1",
    })).resolves.toBe(true);

    expect(fetchMock.mock.calls[0][0]).toBe(
      "http://backend/api/get-watchtower?state=ready&q=Example&sort=updated&offset=20&limit=10&expander=episodes&statsOnly=1",
    );
    const mutation = fetchMock.mock.calls[1][1];
    expect(Object.fromEntries((mutation?.body as FormData).entries())).toEqual({
      action: "park",
      id: "wanted-1",
    });
  });

  it("includes the backend error when a non-503 request fails", async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ error: "bad request" }, 400));

    await expect(backendClient.getQueue(1)).rejects.toThrow(
      "Failed to get queue: bad request",
    );
  });

  it("throws BackendUnavailableError when fetch fails", async () => {
    fetchMock.mockRejectedValueOnce(new TypeError("fetch failed"));

    await expect(backendClient.isOnboarding()).rejects.toBeInstanceOf(BackendUnavailableError);
  });

  it("preserves the undici cause code on BackendUnavailableError", async () => {
    const cause = Object.assign(new Error("connect ECONNREFUSED 127.0.0.1:5000"), {
      code: "ECONNREFUSED",
    });
    fetchMock.mockRejectedValueOnce(Object.assign(new TypeError("fetch failed"), { cause }));

    const error = await backendClient.isOnboarding().then(
      () => null,
      (e: unknown) => e,
    );

    expect(error).toBeInstanceOf(BackendUnavailableError);
    expect(error).toMatchObject({
      name: "BackendUnavailableError",
      code: "ECONNREFUSED",
      message: "Failed to fetch onboarding status: fetch failed (ECONNREFUSED)",
    });
  });

  it("throws BackendUnavailableError on 503 migrating responses", async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ status: "migrating" }, 503));

    const error = await backendClient.isOnboarding().then(
      () => null,
      (e: unknown) => e,
    );

    expect(error).toBeInstanceOf(BackendUnavailableError);
    expect(error).toMatchObject({
      name: "BackendUnavailableError",
      code: "MIGRATING",
      message: "Failed to fetch onboarding status: backend is starting or migrating",
    });
  });

  it("maps stream tracing status fields and defaults retained values", async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({
      enabled: true,
      source: "ui",
      expiresAtUnixMs: 123,
      capacity: 100000,
      eventCount: 4,
      sessionCount: 1,
      retainedEventCount: 4,
      overwrittenEventCount: 0,
      oldestRetainedSequence: 1,
      newestRetainedSequence: 4,
      oldestRetainedAtUnixMs: 100,
      newestRetainedAtUnixMs: 120,
      overflowed: false,
    }));

    await expect(backendClient.getStreamTracingStatus()).resolves.toEqual({
      enabled: true,
      source: "ui",
      expiresAtUnixMs: 123,
      capacity: 100000,
      eventCount: 4,
      sessionCount: 1,
      retained: false,
      retainedUntilUnixMs: 0,
      retainedEventCount: 4,
      overwrittenEventCount: 0,
      oldestRetainedSequence: 1,
      newestRetainedSequence: 4,
      oldestRetainedAtUnixMs: 100,
      newestRetainedAtUnixMs: 120,
      overflowed: false,
    });
  });

  it("posts discard-stream-traces and maps the clean status", async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({
      enabled: false,
      source: "ui",
      expiresAtUnixMs: 0,
      capacity: 100000,
      eventCount: 0,
      sessionCount: 0,
      retained: false,
      retainedUntilUnixMs: 0,
      retainedEventCount: 0,
      overwrittenEventCount: 0,
      overflowed: false,
    }));

    await expect(backendClient.discardStreamTraces()).resolves.toMatchObject({
      retained: false,
      eventCount: 0,
    });
    expect(fetchMock).toHaveBeenCalledWith("http://backend/api/discard-stream-traces", {
      method: "POST",
      headers: { "x-api-key": "test-api-key" },
    });
  });

  it("maps a retained response after disabling stream tracing", async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({
      enabled: false,
      source: "ui",
      expiresAtUnixMs: 0,
      capacity: 100000,
      eventCount: 12,
      sessionCount: 2,
      retained: true,
      retainedUntilUnixMs: 999,
      retainedEventCount: 12,
      overwrittenEventCount: 0,
      overflowed: false,
    }));

    await expect(backendClient.setStreamTracing(false)).resolves.toMatchObject({
      enabled: false,
      retained: true,
      retainedUntilUnixMs: 999,
      eventCount: 12,
    });
  });

  it("posts capacity when enabling stream tracing", async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({
      enabled: true,
      source: "ui",
      expiresAtUnixMs: 123,
      capacity: 200000,
      eventCount: 0,
      sessionCount: 0,
      retained: false,
      retainedUntilUnixMs: 0,
      retainedEventCount: 0,
      overwrittenEventCount: 0,
      overflowed: false,
    }));

    await expect(backendClient.setStreamTracing(true, 30, 200_000)).resolves.toMatchObject({
      enabled: true,
      capacity: 200000,
    });
    expect(fetchMock).toHaveBeenCalledWith(
      "http://backend/api/set-stream-tracing",
      expect.objectContaining({ method: "POST" }),
    );
    const body = fetchMock.mock.calls[0]?.[1]?.body as FormData;
    expect(body.get("capacity")).toBe("200000");
    expect(body.get("minutes")).toBe("30");
  });
});
