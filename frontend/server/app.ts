import "react-router";
import { createRequestHandler } from "@react-router/express";
import express from "express";
import { createProxyMiddleware } from "http-proxy-middleware";
import { websocketServer } from "./websocket.server";
import { isAuthenticated } from "~/auth/authentication.server";

declare module "react-router" {
  interface AppLoadContext {
    VALUE_FROM_EXPRESS: string;
  }
}

export const app = express();
export const initializeWebsocketServer = websocketServer.initialize;

// Proxy all webdav and api requests to the backend
const forwardToBackend = createProxyMiddleware({
  target: process.env.BACKEND_URL,
  changeOrigin: true,
});

const setApiKeyForAuthenticatedRequests = async (req: express.Request) => {
  // if the path is not /api, do nothing
  if (!req.path.startsWith("/api")) return;
  var apikey = req.query.apikey || req.query.apiKey || req.headers["x-api-key"];
  var hasApiKey = apikey && typeof apikey === "string";

  // if the request already has an apikey, do nothing
  if (hasApiKey) return;

  // if the request is not authenticated, do nothing
  const authenticated = await isAuthenticated(req.headers.cookie);
  if (!authenticated) return;

  // otherwise, set the api key header
  req.headers["x-api-key"] = process.env.FRONTEND_BACKEND_API_KEY || "";
}

app.use(async (req, res, next) => {
  if (
    req.method.toUpperCase() === "PROPFIND"
    || req.method.toUpperCase() === "OPTIONS"
    || req.path.startsWith("/api")
    || req.path.startsWith("/view")
    || req.path.startsWith("/.ids")
    || req.path.startsWith("/nzbs")
    || req.path.startsWith("/content")
    || req.path.startsWith("/completed-symlinks")
  ) {
    await setApiKeyForAuthenticatedRequests(req);
    return forwardToBackend(req, res, next);
  }
  next();
});

// Let frontend handle all other requests
app.use(
  createRequestHandler({
    build: () => import("virtual:react-router/server-build"),
    getLoadContext() {
      return {
        VALUE_FROM_EXPRESS: "Hello from Express",
      };
    },
  }),
);
