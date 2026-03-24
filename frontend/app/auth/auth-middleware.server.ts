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
  res.redirect(302, "/login");
}
