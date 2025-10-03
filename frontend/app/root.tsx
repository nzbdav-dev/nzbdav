import {
  Links,
  Meta,
  Outlet,
  redirect,
  Scripts,
  ScrollRestoration,
  useLocation,
  useNavigation,
} from "react-router";

import 'bootstrap/dist/css/bootstrap.min.css';
import "./app.css";
import type { Route } from "./+types/root";
import { sessionStorage } from "~/auth/authentication.server";
import { TopNavigation } from "./routes/_index/components/top-navigation/top-navigation";
import { LeftNavigation } from "./routes/_index/components/left-navigation/left-navigation";
import { PageLayout } from "./routes/_index/components/page-layout/page-layout";
import { Loading } from "./routes/_index/components/loading/loading";
import { backendClient } from "~/clients/backend-client.server";

export async function loader({ request }: Route.LoaderArgs) {
  // unauthenticated routes
  let path = new URL(request.url).pathname;
  if (path === "/login") return { useLayout: false };
  if (path === "/onboarding") return { useLayout: false };

  // ensure all other routes are authenticated
  let session = await sessionStorage.getSession(request.headers.get("cookie"));
  let user = session.get("user");
  if (!user) return redirect("/login");
  
  // Get integrity enabled setting for conditional navigation
  let integrityEnabled = false;
  try {
    const configItems = await backendClient.getConfig(["integrity.enabled"]);
    integrityEnabled = configItems.find(item => item.configName === "integrity.enabled")?.configValue === "true";
  } catch (error) {
    console.warn("Failed to fetch integrity.enabled setting:", error);
  }
  
  return {
    useLayout: true,
    version: process.env.NZBDAV_VERSION,
    integrityEnabled
  };
}


export function Layout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" data-bs-theme="dark">
      <head>
        <meta charSet="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <link rel="icon" href="/logo.svg" />
        <Meta />
        <Links />
      </head>
      <body>
        {children}
        <ScrollRestoration />
        <Scripts />
      </body>
    </html>
  );
}

export default function App({ loaderData }: Route.ComponentProps) {
  const { useLayout, version, integrityEnabled } = loaderData;
  const location = useLocation();
  const navigation = useNavigation();
  const isNavigating = Boolean(navigation.location);

  // display loading animiation during top-level page transitions,
  // but allow the `/explore` page to handle it's own loading screen.
  const isCurrentExplorePage = location.pathname.startsWith("/explore");
  const isNextExplorePage = navigation.location?.pathname?.startsWith("/explore");
  const showLoading = isNavigating && !(isCurrentExplorePage && isNextExplorePage);

  if (useLayout) {
    return (
      <PageLayout
        topNavComponent={TopNavigation}
        bodyChild={showLoading ? <Loading /> : <Outlet />}
        leftNavChild={<LeftNavigation version={version} integrityEnabled={integrityEnabled} />} />
    );
  }

  return <Outlet />;
}