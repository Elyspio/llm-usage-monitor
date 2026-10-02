import { act, fireEvent, render, screen } from "@testing-library/react";
import type { User } from "oidc-client-ts";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vite-plus/test";
import { toApiError } from "@/core/apis/api-error";
import { currentReturnPath, returnPathOf } from "@/core/auth/return-path";
import { ProtectedRoute } from "@components/auth/ProtectedRoute";
import { AuthCallback } from "@pages/AuthCallback";
import { AuthProvider } from "@/view/context/auth.context";

/** A UserManager whose events are raised by the test. */
const fake = vi.hoisted(() => {
	const listeners = new Map<string, Set<(...args: unknown[]) => void>>();
	const on = (name: string) => (listener: (...args: unknown[]) => void) => void (listeners.get(name) ?? listeners.set(name, new Set()).get(name)!).add(listener);
	const off = (name: string) => (listener: (...args: unknown[]) => void) => void listeners.get(name)?.delete(listener);
	const raise = (name: string, ...args: unknown[]) => listeners.get(name)?.forEach((listener) => listener(...args));
	return {
		raise,
		userManager: {
			getUser: vi.fn(),
			removeUser: vi.fn(async () => raise("unloaded")),
			signinRedirect: vi.fn(async () => undefined),
			signinRedirectCallback: vi.fn(),
			signoutRedirect: vi.fn(async () => undefined),
			events: {
				addUserLoaded: on("loaded"),
				removeUserLoaded: off("loaded"),
				addUserUnloaded: on("unloaded"),
				removeUserUnloaded: off("unloaded"),
				addAccessTokenExpired: on("expired"),
				removeAccessTokenExpired: off("expired"),
				addSilentRenewError: on("renewError"),
				removeSilentRenewError: off("renewError"),
			},
		},
	};
});

vi.mock("@/core/auth/user-manager", () => ({ userManager: fake.userManager }));

const user = (overrides: Partial<User> = {}) =>
	({ expired: false, access_token: "tk", profile: { sub: "1", preferred_username: "norole" }, state: undefined, ...overrides }) as User;

const renderProtected = () =>
	render(
		<AuthProvider>
			<ProtectedRoute>
				<p>Protected content</p>
			</ProtectedRoute>
		</AuthProvider>
	);

beforeEach(() => {
	vi.clearAllMocks();
	fake.userManager.getUser.mockResolvedValue(user());
	window.history.replaceState(null, "", "/");
});

describe("ProtectedRoute", () => {
	it("shows the page to a signed-in user", async () => {
		renderProtected();

		expect(await screen.findByText("Protected content")).toBeTruthy();
	});

	it("asks to sign in without a session, and keeps the requested page for after the sign-in", async () => {
		fake.userManager.getUser.mockResolvedValue(null);
		window.history.replaceState(null, "", "/history?range=7d#chart");
		renderProtected();

		fireEvent.click(await screen.findByRole("button", { name: "Sign in" }));

		expect(fake.userManager.signinRedirect).toHaveBeenCalledWith({ state: { returnTo: "/history?range=7d#chart" } });
		expect(screen.queryByText(/session has expired/)).toBeNull();
	});

	it("treats a stored but expired session as an expired one", async () => {
		fake.userManager.getUser.mockResolvedValue(user({ expired: true }));
		renderProtected();

		expect(await screen.findByText(/Your session has expired/)).toBeTruthy();
	});
});

describe("session", () => {
	it.each([
		["the silent renewal fails", "renewError"],
		["the access token expires", "expired"],
	])("goes back to the sign-in screen when %s", async (_, event) => {
		renderProtected();
		await screen.findByText("Protected content");

		act(() => fake.raise(event, new Error("renew failed")));

		expect(await screen.findByText(/Your session has expired/)).toBeTruthy();
		expect(screen.queryByText("Protected content")).toBeNull();
		expect(fake.userManager.removeUser).toHaveBeenCalled();
	});

	it("goes back to the sign-in screen when the API answers 401", async () => {
		renderProtected();
		await screen.findByText("Protected content");

		act(() => void toApiError({ title: "Unauthorized" }, new Response(null, { status: 401 })));

		expect(await screen.findByRole("button", { name: "Sign in" })).toBeTruthy();
		expect(screen.getByText(/Your session has expired/)).toBeTruthy();
	});

	it("shows access denied when the API answers 403, with a way to switch account", async () => {
		renderProtected();
		await screen.findByText("Protected content");

		act(() => void toApiError({ title: "Forbidden" }, new Response(null, { status: 403 })));

		expect(await screen.findByRole("heading", { name: "Access denied" })).toBeTruthy();
		expect(screen.getByText("norole")).toBeTruthy();
		expect(screen.queryByText("Protected content")).toBeNull();
		fireEvent.click(screen.getByRole("button", { name: "Sign out" }));
		expect(fake.userManager.signoutRedirect).toHaveBeenCalled();
	});

	it("ignores the other API failures", async () => {
		renderProtected();
		await screen.findByText("Protected content");

		act(() => void toApiError({ title: "boom" }, new Response(null, { status: 500 })));

		expect(screen.getByText("Protected content")).toBeTruthy();
	});
});

describe("AuthCallback", () => {
	const renderCallback = () =>
		render(
			<AuthProvider>
				<RouterProvider
					router={createMemoryRouter(
						[
							{ path: "/auth/callback", element: <AuthCallback /> },
							{ path: "/", element: <p>Dashboard page</p> },
							{ path: "/history", element: <p>History page</p> },
						],
						{ initialEntries: ["/auth/callback?code=abc&state=xyz"] }
					)}
				/>
			</AuthProvider>
		);

	it("goes back to the page asked before the sign-in", async () => {
		fake.userManager.signinRedirectCallback.mockResolvedValue(user({ state: { returnTo: "/history" } }));

		renderCallback();

		expect(await screen.findByText("History page")).toBeTruthy();
	});

	it("goes to the dashboard when the state is not a page of the application", async () => {
		fake.userManager.signinRedirectCallback.mockResolvedValue(user({ state: { returnTo: "//evil.example/steal" } }));

		renderCallback();

		expect(await screen.findByText("Dashboard page")).toBeTruthy();
	});

	it("offers to try again when the code exchange fails", async () => {
		fake.userManager.signinRedirectCallback.mockRejectedValue(new Error("invalid_grant"));

		renderCallback();

		fireEvent.click(await screen.findByRole("button", { name: "Try again" }));
		expect(fake.userManager.signinRedirect).toHaveBeenCalledWith({ state: { returnTo: "/" } });
	});
});

describe("return path", () => {
	it("keeps only the paths of the application", () => {
		expect(returnPathOf({ returnTo: "/usage?range=30d" })).toBe("/usage?range=30d");
		expect(returnPathOf({ returnTo: "https://evil.example" })).toBe("/");
		expect(returnPathOf({ returnTo: "/\\evil.example" })).toBe("/");
		expect(returnPathOf({ returnTo: "/auth/callback?code=1" })).toBe("/");
		expect(returnPathOf(undefined)).toBe("/");
		expect(currentReturnPath({ pathname: "/auth/callback", search: "?code=1", hash: "" })).toBe("/");
	});
});
