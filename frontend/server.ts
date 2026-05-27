import compression from "compression";
import express from "express";
import morgan from "morgan";
import http from "http";
import { WebSocketServer } from "ws";

// Short-circuit the type-checking of the built output.
const BUILD_PATH = "../build/server/index.js";
const DEVELOPMENT = process.env.NODE_ENV === "development";
const PORT = Number.parseInt(process.env.PORT || "3000");

// URL_BASE controls the sub-path the app is mounted under. See app/utils/url-base.ts
// for the canonical normalization rules. The Vite build also bakes this value into the
// client bundle; the runtime env var here mounts middleware at the matching prefix so
// both ends agree.
function normalizeUrlBase(raw: string | undefined): string {
  if (!raw) return "";
  const trimmed = raw.trim();
  if (trimmed === "" || trimmed === "/") return "";
  const withLeading = trimmed.startsWith("/") ? trimmed : `/${trimmed}`;
  return withLeading.replace(/\/+$/, "");
}
const URL_BASE = normalizeUrlBase(process.env.URL_BASE);

// Initialize the express app
const app = express();
app.disable("x-powered-by");

// /health is always served at the unprefixed root regardless of URL_BASE so
// container healthchecks and reverse-proxy probes don't have to know the
// sub-path. Proxies to the backend's /health (the actual liveness signal)
// and surfaces its status — keeps the .NET process the source of truth
// while leaving the backend port internal.
app.get("/health", async (_req, res) => {
  try {
    const backendUrl = process.env.BACKEND_URL || "http://localhost:8080";
    const r = await fetch(`${backendUrl}/health`, { signal: AbortSignal.timeout(3000) });
    res.status(r.ok ? 200 : 503).type("text/plain").send(r.ok ? "Healthy" : "Backend unhealthy");
  } catch {
    res.status(503).type("text/plain").send("Backend unreachable");
  }
});

// All app middleware goes on a sub-router so it inherits the URL_BASE prefix without
// requiring per-middleware path arithmetic. Inside the router, `req.path` is already
// stripped of URL_BASE — existing path-prefix checks (`/api`, `/nzbs`, etc.) work
// unchanged.
const router = express.Router();

router.use(
  compression({
    // Don't compress proxied WebDAV/media/API responses; keep Content-Length intact for seek
    filter: (req, res) => {
      const path = decodeURIComponent(req.path || "");
      if (
        path.startsWith("/view") ||
        path.startsWith("/.ids") ||
        path.startsWith("/nzbs") ||
        path.startsWith("/content") ||
        path.startsWith("/completed-symlinks") ||
        path.startsWith("/api")
      ) {
        return false;
      }
      return compression.filter(req, res);
    },
  }),
);

// Initialize the websocket server as soon as both it and the server-module are ready
let _serverModule: any = null;
let _websocketServer: WebSocketServer | null = null;
const setWebsocketServer = (websocketServer: WebSocketServer) => {
  if (_websocketServer != null) return;
  if (_serverModule != null) _serverModule.initializeWebsocketServer(websocketServer);
  _websocketServer = websocketServer;
}
const setServerModule = (serverModule: any) => {
  if (_serverModule != null) return;
  if (_websocketServer != null) serverModule.initializeWebsocketServer(_websocketServer);
  _serverModule = serverModule;
}

// Handle development vs production
if (DEVELOPMENT) {
  console.log("Starting development server");
  const viteDevServer = await import("vite").then((vite) =>
    vite.createServer({
      server: { middlewareMode: true },
    }),
  );
  router.use(viteDevServer.middlewares);
  router.use(async (req, res, next) => {
    try {
      const serverModule = await viteDevServer.ssrLoadModule("./server/app.ts");
      setServerModule(serverModule);
      return await serverModule.app(req, res, next);
    } catch (error) {
      if (typeof error === "object" && error instanceof Error) {
        viteDevServer.ssrFixStacktrace(error);
      }
      next(error);
    }
  });
} else {
  console.log("Starting production server");
  router.use(
    "/assets",
    express.static("build/client/assets", { immutable: true, maxAge: "1y" }),
  );
  router.use(morgan("tiny", {
    skip: (req, res) => {
      return res.statusCode < 400
        || req.url === "/favicon.ico"
    }
  }));
  router.use(express.static("build/client", { maxAge: "1h" }));
  const serverModule = await import(BUILD_PATH);
  router.use(serverModule.app);
  setServerModule(serverModule);
}

// Mount the router. When URL_BASE is empty we mount at root (no prefix). Otherwise we
// mount under URL_BASE and redirect the bare host root so users hitting `/` land in
// the right place.
if (URL_BASE) {
  app.get("/", (_req, res) => res.redirect(`${URL_BASE}/`));
  app.use(URL_BASE, router);
} else {
  app.use(router);
}

// Create both the http and websocket servers
const server = http.createServer(app);
setWebsocketServer(new WebSocketServer({ server }));

// Begin listening for connections
server.listen(PORT, () => {
  console.log(`Server is running on http://localhost:${PORT}${URL_BASE}`);
});
