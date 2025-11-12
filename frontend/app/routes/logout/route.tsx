import type { Route } from "./+types/route";
import { isAuthenticated, logout } from "~/auth/authentication.server";
import { redirect } from "react-router";

export async function action({ request }: Route.ActionArgs) {
    // if already logged out, redirect to login page
    if (!await isAuthenticated(request)) return redirect("/login");

    // if we logout intent is not confirmed, redirect to landing page
    const formData = await request.formData();
    const confirm = formData.get("confirm")?.toString();
    if (confirm !== "true") return redirect("/");

    // otherwise, proceed to log out!
    const responseInit = await logout(request);
    return redirect("/login", responseInit);
}