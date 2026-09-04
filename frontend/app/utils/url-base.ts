// URL_BASE controls the sub-path the app is hosted under, e.g. "/nzbdav".
//
// - Configured via the `URL_BASE` env var. Set as a Docker build arg so it gets baked
//   into the React Router basename, Vite asset paths, and the `__URL_BASE__` global below.
//   Also read at runtime by the Express server so it mounts middleware under the same
//   prefix. Both ends must agree — the build arg and the runtime env var.
// - Empty string ("") and "/" both mean "app is at the root".
// - The Vite `define` in vite.config.ts replaces `__URL_BASE__` with the normalized
//   value (e.g. `"/nzbdav"` or `""`) at compile time.
// - The normalized form never has a trailing slash, so `URL_BASE + "/api"` is always
//   well-formed.

declare const __URL_BASE__: string;

export const URL_BASE: string =
  typeof __URL_BASE__ !== "undefined" ? __URL_BASE__ : "";

/**
 * Prefix a server-relative path with URL_BASE. Always returns a leading slash.
 *   withUrlBase("/api/foo")      // "/nzbdav/api/foo" or "/api/foo"
 *   withUrlBase("api/foo")       // "/nzbdav/api/foo" or "/api/foo"
 *   withUrlBase("/api?mode=x")   // "/nzbdav/api?mode=x" or "/api?mode=x"
 */
export function withUrlBase(path: string): string {
  const normalizedPath = path.startsWith("/") ? path : `/${path}`;
  return `${URL_BASE}${normalizedPath}`;
}

/**
 * Browser-side WebSocket URL helper. Produces a `ws[s]://host<URL_BASE>/ws` URL from
 * the current page origin. Replaces the inline `window.location.origin.replace(...)`
 * idiom that used to litter the codebase.
 */
export function getWebsocketUrl(): string {
  const wsOrigin = window.location.origin.replace(/^http/, "ws");
  return `${wsOrigin}${URL_BASE}/ws`;
}
