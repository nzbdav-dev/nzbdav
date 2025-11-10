import { Alert, Button, Form as BootstrapForm } from "react-bootstrap";
import styles from "./route.module.css"
import type { Route } from "./+types/route";
import { useState } from "react";
import { backendClient } from "~/clients/backend-client.server";
import { Form, redirect, useNavigation } from "react-router";
import { sessionStorage } from "~/auth/authentication.server";

type OnboardingPageData = {
    error: string
}

export async function loader({ request }: Route.LoaderArgs) {
    // If auth is disabled, redirect to main app (no onboarding needed)
    if (process.env.DISABLE_FRONTEND_AUTH === 'true') {
        return redirect("/");
    }

    // if already logged in, redirect to landing page
    let session = await sessionStorage.getSession(request.headers.get("cookie"));
    let user = session.get("user");
    if (user) return redirect("/");

    // if we don't need to go through onboarding, redirect to login page
    const isOnboarding = await backendClient.isOnboarding();
    if (!isOnboarding) return redirect("/login");

    // otherwise, proceed to onboarding page!
    return { error: null };
}

export default function Index({ loaderData, actionData }: Route.ComponentProps) {
    var pageData = actionData || loaderData;
    const [username, setUsername] = useState("");
    const [password, setPassword] = useState("");
    const [confirmPassword, setConfirmPassword] = useState("");

    const navigation = useNavigation();
    const isLoading = navigation.state == "submitting";

    var submitButtonDisabled = false;
    var submitButtonText = "Register";
    if (isLoading) {
        submitButtonDisabled = true;
        submitButtonText = "Registering...";
    } else if (username == "") {
        submitButtonDisabled = true;
        submitButtonText = "Username is required";
    } else if (password === "") {
        submitButtonDisabled = true;
        submitButtonText = "Password is required";
    } else if (password != confirmPassword) {
        submitButtonDisabled = true;
        submitButtonText = "Passwords must match";
    }

    return (
        <>
            <Form className={styles["container"]} method="POST">
                <img className={styles["logo"]} src="/logo.svg"></img>
                <div className={styles["title"]}>Nzb DAV</div>
                {pageData.error &&
                    <Alert className={styles["alert"]} variant="danger">
                        {pageData.error}
                    </Alert>
                }
                {!pageData.error &&
                    <Alert className={styles["alert"]} variant="warning">
                        <p style={{ marginBottom: "5px" }}>Welcome!</p>
                        Register your admin account.
                    </Alert>
                }
                <BootstrapForm.Control
                    autoFocus
                    name="username"
                    type="text"
                    placeholder="Username"
                    value={username}
                    onChange={e => setUsername(e.currentTarget.value)} />
                <BootstrapForm.Control
                    name="password"
                    type="password"
                    placeholder="Password"
                    value={password}
                    onChange={e => setPassword(e.currentTarget.value)} />
                <BootstrapForm.Control
                    type="password"
                    placeholder="Confirm Password"
                    value={confirmPassword}
                    onChange={e => setConfirmPassword(e.currentTarget.value)} />
                <Button
                    type="submit"
                    variant="primary"
                    disabled={submitButtonDisabled}>
                    {submitButtonText}
                </Button>
            </Form>
        </>
    );
}

export async function action({ request }: Route.ActionArgs) {
    try {
        // If auth is disabled, redirect to main app
        if (process.env.DISABLE_FRONTEND_AUTH === 'true') {
            return redirect("/");
        }

        // if we don't need to go through onboarding, redirect to login page
        const isOnboarding = await backendClient.isOnboarding();
        if (!isOnboarding) return redirect("/login");

        // finish onboarding
        const formData = await request.formData();
        const username = formData.get("username")?.toString();
        const password = formData.get("password")?.toString();
        if (!username || !password) throw new Error("username and password required");
        var isSuccess = await backendClient.createAccount(username, password);
        if (!isSuccess) throw new Error("Unknown error creating account");
        let session = await sessionStorage.getSession(request.headers.get("cookie"));
        session.set("user", { username: username });
        return redirect("/", { headers: { "Set-Cookie": await sessionStorage.commitSession(session) } });
    }
    catch (error) {
        if (error instanceof Error) {
            return { error: error.message };
        }
        throw error
    }
}