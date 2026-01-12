import compression from "compression";
import express from "express";
import morgan from "morgan";
import http from "http";
import { WebSocketServer } from "ws";

// Short-circuit the type-checking of the built output.
const BUILD_PATH = "../build/server/index.js";
const DEVELOPMENT = process.env.NODE_ENV === "development";
const PORT = Number.parseInt(process.env.PORT || "3000");

// Initialize the express app
const app = express();
app.use(
  compression({
    // Don't compress proxied WebDAV/media/API responses; keep Content-Length intact for seek
    filter: (req, res) => {
      const path = req.path || "";
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
app.disable("x-powered-by");

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
  app.use(viteDevServer.middlewares);
  app.use(async (req, res, next) => {
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
  app.use(
    "/assets",
    express.static("build/client/assets", { immutable: true, maxAge: "1y" }),
  );
  app.use(morgan("tiny", {
    skip: (req, res) => {
      return res.statusCode < 400
        || req.url === "/favicon.ico"
    }
  }));
  app.use(express.static("build/client", { maxAge: "1h" }));
  const serverModule = await import(BUILD_PATH);
  app.use(serverModule.app);
  setServerModule(serverModule);
}

// Create both the http and websocket servers
const server = http.createServer(app);
setWebsocketServer(new WebSocketServer({ server }));

// Begin listening for connections
server.listen(PORT, () => {
  console.log(`Server is running on http://localhost:${PORT}`);
});
