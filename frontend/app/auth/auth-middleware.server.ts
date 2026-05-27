import type express from "express";
import { isAuthenticated } from "~/auth/authentication.server";

// Paths that do not require authentication. Every other path is protected.
const PUBLIC_PATHS = [
  "/__manifest",
  "/login",
  "/login.data",
  "/onboarding",
  "/onboarding.data",
];

// URL_BASE is read at runtime — the Express server mounts middleware under this prefix,
// so within this middleware `req.path` is already stripped. But `res.redirect("/login")`
// emits an absolute path back to the browser, which the browser interprets relative to
// the origin, not the URL_BASE mount. So we have to put the prefix back on outgoing
// Location values manually. Mirror of the normalizer in `server.ts`.
function normalizeUrlBase(raw: string | undefined): string {
  if (!raw) return "";
  const trimmed = raw.trim();
  if (trimmed === "" || trimmed === "/") return "";
  const withLeading = trimmed.startsWith("/") ? trimmed : `/${trimmed}`;
  return withLeading.replace(/\/+$/, "");
}
const URL_BASE = normalizeUrlBase(process.env.URL_BASE);

export async function authMiddleware(
  req: express.Request,
  res: express.Response,
  next: express.NextFunction,
): Promise<void> {
  // Allow explicitly public paths
  const pathname = decodeURIComponent(req.path);
  if (PUBLIC_PATHS.includes(pathname)) return next();

  // Allow authenticated sessions
  if (await isAuthenticated(req)) return next();

  // Redirect everything else to the login page
  res.redirect(302, `${URL_BASE}/login`);
}
