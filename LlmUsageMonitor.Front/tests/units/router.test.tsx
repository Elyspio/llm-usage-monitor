import { ThemeProvider } from "@mui/material";
import { QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { User } from "oidc-client-ts";
import { createMemoryRouter, RouterProvider } from "react-router";
import { describe, expect, it, vi } from "vite-plus/test";
import { AuthProvider } from "@/view/context/auth.context";
import { appRoutes } from "@/view/router";
import { theme } from "@/view/theme";
import { mockApi, testQueryClient } from "./render";

const userManager = vi.hoisted(() => ({
	getUser: vi.fn(async () => ({ expired: false, access_token: "tk", profile: { sub: "1", preferred_username: "admin" } }) as User),
	signoutRedirect: vi.fn(async () => undefined),
	events: {
		addUserLoaded: vi.fn(),
		removeUserLoaded: vi.fn(),
		addUserUnloaded: vi.fn(),
		removeUserUnloaded: vi.fn(),
		addAccessTokenExpired: vi.fn(),
		removeAccessTokenExpired: vi.fn(),
		addSilentRenewError: vi.fn(),
		removeSilentRenewError: vi.fn(),
	},
}));
vi.mock("@/core/auth/user-manager", () => ({ userManager }));

mockApi();

const renderApp = (path: string) =>
	render(
		<ThemeProvider theme={theme}>
			<QueryClientProvider client={testQueryClient()}>
				<AuthProvider>
					<RouterProvider router={createMemoryRouter(appRoutes, { initialEntries: [path] })} />
				</AuthProvider>
			</QueryClientProvider>
		</ThemeProvider>
	);

describe("router and layout", () => {
	it("loads the requested page lazily inside the layout", async () => {
		renderApp("/settings");

		expect(await screen.findByRole("heading", { level: 1, name: "Settings" })).toBeTruthy();
		const navigation = screen.getByRole("navigation", { name: "Main navigation" });
		expect(within(navigation).getByRole("link", { name: "Settings" }).getAttribute("aria-current")).toBe("page");
		expect(within(navigation).getByRole("link", { name: "History" }).getAttribute("aria-current")).toBeNull();
		expect(screen.getByText("admin")).toBeTruthy();
	});

	it("navigates between the pages from the sidebar", async () => {
		renderApp("/");
		expect(await screen.findByRole("heading", { level: 1, name: "Dashboard" })).toBeTruthy();

		fireEvent.click(screen.getByRole("link", { name: "History" }));

		expect(await screen.findByRole("heading", { level: 1, name: "History" })).toBeTruthy();
	});

	it("offers a skip link to the content and a sign out", async () => {
		renderApp("/usage");
		await screen.findByRole("heading", { level: 1, name: "Usage" });

		expect(screen.getByRole("link", { name: "Skip to content" }).getAttribute("href")).toBe("#main");
		fireEvent.click(screen.getByRole("button", { name: "Sign out" }));
		expect(userManager.signoutRedirect).toHaveBeenCalled();
	});
});
