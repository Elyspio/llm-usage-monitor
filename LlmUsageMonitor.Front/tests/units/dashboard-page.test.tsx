import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vite-plus/test";
import { DashboardPage } from "@pages/DashboardPage";
import { apiUrl, claude, dashboard, degradedClaude, iso, run } from "./fixtures";
import { mockApi, renderPage as render, testQueryClient } from "./render";

const runningRun = run();

const server = mockApi();

const renderPage = (queryClient = testQueryClient()) => render(<DashboardPage />, { queryClient });

describe("DashboardPage", () => {
	it("shows all windows on the shared timeline with the trigger window first", async () => {
		renderPage();

		expect(screen.getByRole("heading", { level: 1, name: "Dashboard" })).toBeTruthy();
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
		const queued = runningRun;
		let posted = 0;
		let followed = 0;
		server.use(
			http.post(`${apiUrl}/api/providers/codex/trigger`, () => {
				posted++;
				return HttpResponse.json(queued, { status: 202 });
			}),
			http.get(`${apiUrl}/api/trigger-runs/run-1`, () => {
				followed++;
				return HttpResponse.json({ ...queued, status: "succeeded", endedAt: iso(0), durationMs: 2100 });
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

	it("keeps the values after a failed refresh, with a banner to retry", async () => {
		const queryClient = testQueryClient();
		renderPage(queryClient);
		await screen.findByRole("region", { name: "Quota timeline" });

		server.use(http.get(`${apiUrl}/api/dashboard`, () => HttpResponse.error()));
		await queryClient.refetchQueries();

		expect(await screen.findByText(/Could not refresh the dashboard: The API could not be reached/)).toBeTruthy();
		expect(screen.getByRole("region", { name: "Quota timeline" })).toBeTruthy();

		server.resetHandlers();
		fireEvent.click(screen.getByRole("button", { name: "Retry" }));
		await waitFor(() => expect(screen.queryByText(/Could not refresh the dashboard/)).toBeNull());
	});

	it("says why a trigger is refused", async () => {
		server.use(http.post(`${apiUrl}/api/providers/codex/trigger`, () => HttpResponse.json({ title: "busy", status: 409 }, { status: 409 })));

		renderPage();
		const codex = await screen.findByRole("region", { name: "Codex" });
		fireEvent.click(within(codex).getByRole("button", { name: /Trigger now/ }));

		expect(await within(codex).findByText("Trigger refused. A CLI process is already running for this provider.")).toBeTruthy();
		expect(within(codex).getByRole("alert").textContent).toContain("A CLI process is already running");
		expect((within(codex).getByRole("button", { name: /Trigger now/ }) as HTMLButtonElement).disabled).toBe(false);
	});

	it("releases the trigger button when the queued run cannot be followed", async () => {
		server.use(
			http.post(`${apiUrl}/api/providers/codex/trigger`, () => HttpResponse.json({ ...runningRun }, { status: 202 })),
			http.get(`${apiUrl}/api/trigger-runs/run-1`, () => HttpResponse.json({ title: "boom", status: 500 }, { status: 500 }))
		);

		renderPage();
		const codex = await screen.findByRole("region", { name: "Codex" });
		fireEvent.click(within(codex).getByRole("button", { name: /Trigger now/ }));

		expect(await within(codex).findByText(/its progress could not be read. Server error \(500\)/)).toBeTruthy();
		expect((within(codex).getByRole("button", { name: /Trigger now/ }) as HTMLButtonElement).disabled).toBe(false);
	});

	it("marks a window whose reset has passed until the next reading", async () => {
		const expired = { ...claude.lastReading!.windows[1], resetsAt: iso(-10 * 60_000) };
		server.use(
			http.get(`${apiUrl}/api/dashboard`, () =>
				HttpResponse.json({ ...dashboard, providers: [{ ...claude, lastReading: { ...claude.lastReading!, windows: [expired] } }, dashboard.providers[1]] })
			)
		);

		renderPage();

		expect(await screen.findByText("Reset passed · waiting for a reading")).toBeTruthy();
	});

	it("says when the API cannot be reached on the first load, and retries", async () => {
		server.use(http.get(`${apiUrl}/api/dashboard`, () => HttpResponse.error()));

		renderPage();

		expect(await screen.findByText("The API could not be reached: check the connection.")).toBeTruthy();
		server.resetHandlers();
		fireEvent.click(screen.getByRole("button", { name: "Retry" }));
		expect(await screen.findByRole("region", { name: "Quota timeline" })).toBeTruthy();
	});
});
