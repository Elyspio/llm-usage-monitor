import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { afterAll, afterEach, beforeAll, describe, it } from "vite-plus/test";
import { client } from "@/core/apis/generated/client.gen";
import { DashboardPage } from "@pages/DashboardPage";

const apiUrl = "http://api.test";

const server = setupServer(
	http.get(`${apiUrl}/api/dashboard`, () => HttpResponse.json({ providers: [{ provider: "claude" }, { provider: "codex" }] })),
	http.get(`${apiUrl}/api/dashboard-error`, () => HttpResponse.json({}, { status: 500 }))
);

beforeAll(() => {
	client.setConfig({ baseUrl: apiUrl });
	server.listen({ onUnhandledRequest: "error" });
});
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

const renderPage = () =>
	render(
		<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
			<DashboardPage />
		</QueryClientProvider>
	);

describe("DashboardPage", () => {
	it("shows one card per provider returned by the API", async () => {
		renderPage();

		await screen.findByText("Claude");
		await screen.findByText("Codex");
	});

	it("shows an error when the dashboard cannot be loaded", async () => {
		server.use(http.get(`${apiUrl}/api/dashboard`, () => HttpResponse.json({ title: "boom" }, { status: 500 })));

		renderPage();

		await screen.findByText("Impossible de charger le tableau de bord.");
	});
});
