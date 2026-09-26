import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vite-plus/test";
import { client } from "@/core/apis/generated/client.gen";
import type { TriggerRun } from "@/core/apis/generated/types.gen";
import { DashboardPage } from "@pages/DashboardPage";
import { apiUrl, dashboard, degradedClaude, emptyHistory } from "./fixtures";

const server = setupServer(
	http.get(`${apiUrl}/api/dashboard`, () => HttpResponse.json(dashboard)),
	http.get(`${apiUrl}/api/history`, () => HttpResponse.json(emptyHistory))
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
	it("shows all windows on the shared timeline with the trigger window first", async () => {
		renderPage();

		const timeline = await screen.findByRole("region", { name: "Quota timeline" });
		const cards = within(timeline).getAllByText(/Claude · 5 h session|Claude · Weekly · all models/);
		expect(cards.map((card) => card.textContent)).toEqual(["Claude · 5 h session", "Claude · Weekly · all models"]);
		expect(within(timeline).getByText("60%")).toBeTruthy();
		expect(within(timeline).getByRole("progressbar", { name: "Claude 5 h session used" }).getAttribute("value")).toBe("40");
		expect(await screen.findByRole("region", { name: "Codex" })).toBeTruthy();
		expect(screen.getByText("Automatic trigger disabled in the settings.")).toBeTruthy();
		expect(screen.queryByRole("region", { name: "History" })).toBeNull();
		expect(screen.queryByRole("region", { name: "Trigger log" })).toBeNull();
	});

	it("keeps the last valid values greyed out with the error and the raw detail", async () => {
		server.use(http.get(`${apiUrl}/api/dashboard`, () => HttpResponse.json({ ...dashboard, providers: [degradedClaude, dashboard.providers[1]] })));

		renderPage();

		const claude = await screen.findByRole("region", { name: "Claude" });
		expect(within(claude).getAllByText("Login expired").length).toBeGreaterThan(0);
		expect(within(claude).getByText(/claude auth login/)).toBeTruthy();
		expect(within(claude).getByText(/AUTH_EXPIRED · The Claude CLI could not refresh its login./)).toBeTruthy();
		expect(screen.getAllByText(/stale · read .* ago/).length).toBe(2);
	});

	it("queues a manual trigger and follows it until it ends", async () => {
		const run: TriggerRun = {
			id: "run-1",
			provider: "codex",
			manual: true,
			cycleKey: null,
			model: "gpt-5.6-luna",
			status: "running",
			startedAt: new Date().toISOString(),
			endedAt: null,
			errorCode: null,
			error: null,
			durationMs: null,
		};
		let posted = 0;
		let followed = 0;
		server.use(
			http.post(`${apiUrl}/api/providers/codex/trigger`, () => {
				posted++;
				return HttpResponse.json(run, { status: 202 });
			}),
			http.get(`${apiUrl}/api/trigger-runs/run-1`, () => {
				followed++;
				return HttpResponse.json({ ...run, status: "succeeded", endedAt: new Date().toISOString(), durationMs: 2100 });
			})
		);

		renderPage();
		const codex = await screen.findByRole("region", { name: "Codex" });
		fireEvent.click(within(codex).getByRole("button", { name: /Trigger now/ }));

		await screen.findByRole("region", { name: "Codex" });
		await expect.poll(() => [posted, followed]).toEqual([1, 1]);
	});

	it("shows an error when the dashboard cannot be loaded", async () => {
		server.use(http.get(`${apiUrl}/api/dashboard`, () => HttpResponse.json({ title: "boom" }, { status: 500 })));

		renderPage();

		expect(await screen.findByText("Could not load the dashboard.")).toBeTruthy();
	});
});
