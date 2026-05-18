import { redirect } from "react-router";
import type { Route } from "./+types/route";

export async function loader({ request }: Route.LoaderArgs) {
    return redirect("/dashboard")
}