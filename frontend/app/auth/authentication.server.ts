import { createCookieSessionStorage } from "react-router";
import crypto from "crypto"
import { backendClient } from "~/clients/backend-client.server";
import type { IncomingMessage } from "http";

export const IS_FRONTEND_AUTH_DISABLED = process.env.DISABLE_FRONTEND_AUTH === 'true';

type User = {
  username: string;
};

const oneYear = 60 * 60 * 24 * 365; // seconds
const sessionStorage = createCookieSessionStorage({
  cookie: {
    name: "__session",
    httpOnly: true,
    path: "/",
    sameSite: "strict",
    secrets: [process?.env?.SESSION_KEY || crypto.randomBytes(64).toString('hex')],
    secure: ["true", "yes"].includes(process?.env?.SECURE_COOKIES || ""),
    maxAge: oneYear,
  },
});

export async function isAuthenticated(request: Request | IncomingMessage): Promise<boolean> {
  // If auth is disabled, always return true
  if (IS_FRONTEND_AUTH_DISABLED) return true;

  // Otherwise, check session storage
  const cookieHeader = request instanceof Request
    ? request.headers.get("cookie")
    : request.headers.cookie;
  if (!cookieHeader) return false;
  const session = await sessionStorage.getSession(cookieHeader);
  const user = session.get("user");
  return !!user;
}

export async function login(request: Request): Promise<ResponseInit> {
  let user = await authenticate(request);
  let session = await sessionStorage.getSession(request.headers.get("cookie"));
  session.set("user", user);
  return { headers: { "Set-Cookie": await sessionStorage.commitSession(session) } };
}

export async function logout(request: Request): Promise<ResponseInit> {
  let session = await sessionStorage.getSession(request.headers.get("cookie"));
  session.unset("user");
  return { headers: { "Set-Cookie": await sessionStorage.commitSession(session) } };
}

export async function setSessionUser(request: Request, username: string): Promise<ResponseInit> {
  let session = await sessionStorage.getSession(request.headers.get("cookie"));
  session.set("user", { username: username })
  return { headers: { "Set-Cookie": await sessionStorage.commitSession(session) } };
}

async function authenticate(request: Request): Promise<User> {
  const formData = await request.formData();
  const username = formData.get("username")?.toString();
  const password = formData.get("password")?.toString();
  if (!username || !password) throw new Error("username and password required");
  if (await backendClient.authenticate(username, password)) return { username: username };
  throw new Error("Invalid credentials");
}
