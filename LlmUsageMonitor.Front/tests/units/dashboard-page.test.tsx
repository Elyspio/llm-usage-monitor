import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vite-plus/test";
import { DashboardPage } from "@pages/DashboardPage";
import { apiUrl, claude, codex, copilot, dashboard, degradedClaude, hour, iso, run } from "./fixtures";
import { mockApi, renderPage as render, testQueryClient } from "./render";

const runningRun = run();

const server = mockApi();

const renderPage = (queryClient = testQueryClient()) => render(<DashboardPage />, { queryClient });

describe("DashboardPage", () => {
	it("shows all windows on the shared timeline with the trigger window first", async () => {
		renderPage();

		expect(screen.getByRole("heading", { level: 1, name: "Dashboard" })).toBeTruthy();
		const timeline = await screen.findByRole("region", { name: "Quota timeline" });
		const group = within(timeline).getByRole("region", { name: "Claude windows" });
		expect(within(group).getByRole("heading", { level: 3, name: "Claude" })).toBeTruthy();
		const cards = within(group).getAllByText(/^5 h session$|^Weekly · all models$/);
		expect(cards.map((card) => card.textContent)).toEqual(["5 h session", "Weekly · all models"]);
		expect(within(timeline).getByText("60%")).toBeTruthy();
		expect(within(timeline).getByRole("progressbar", { name: "Claude 5 h session used" }).getAttribute("value")).toBe("40");
		expect(await screen.findByRole("region", { name: "Codex" })).toBeTruthy();
		expect(screen.getByText("Automatic trigger disabled in the settings.")).toBeTruthy();
		expect(screen.queryByRole("region", { name: "History" })).toBeNull();
		expect(screen.queryByRole("region", { name: "Trigger log" })).toBeNull();
	});

	it("shows the Copilot monthly allowance in its card, off the timeline and without trigger", async () => {
		server.use(http.get(`${apiUrl}/api/dashboard`, () => HttpResponse.json({ ...dashboard, providers: [claude, codex, copilot] })));
		renderPage();

		const card = await screen.findByRole("region", { name: "Copilot" });
		expect(within(card).getByText("15%")).toBeTruthy();
		expect(within(card).getByText("45 / 300 credits left · business plan")).toBeTruthy();
		expect(within(card).getByText(/ahead of an even pace/)).toBeTruthy();
		expect(within(card).queryByRole("button")).toBeNull();
		expect(within(screen.getByRole("region", { name: "Quota timeline" })).queryByRole("region", { name: "Copilot windows" })).toBeNull();
	});

	it("keeps the last valid values greyed out with the error and the raw detail", async () => {
		server.use(http.get(`${apiUrl}/api/dashboard`, () => HttpResponse.json({ ...dashboard, providers: [degradedClaude, dashboard.providers[1]] })));

		renderPage();

		const claude = await screen.findByRole("region", { name: "Claude" });
		expect(within(claude).getAllByText("Login expired").length).toBeGreaterThan(0);
		expect(within(claude).getByText(/claude auth login.*as user "llm-monitor"/)).toBeTruthy();
		expect(within(claude).getByText(/authExpired · The Claude CLI could not refresh its login./)).toBeTruthy();
		// Once for the provider, next to its name: not on every window.
		expect(within(screen.getByRole("region", { name: "Claude windows" })).getAllByText(/stale · read .* ago/).length).toBe(1);
	});

	it("nests the Fable weekly limit under the weekly window of its provider", async () => {
		const fable = { id: "seven_day_overage_included", usedPercent: 68, remainingPercent: 32, resetsAt: iso(5 * 24 * hour), windowDurationMinutes: 10_080 };
		const withFable = { ...claude, lastReading: { ...claude.lastReading!, windows: [fable, ...claude.lastReading!.windows] } };
		server.use(http.get(`${apiUrl}/api/dashboard`, () => HttpResponse.json({ ...dashboard, providers: [withFable, codex] })));
		renderPage();

		const group = await screen.findByRole("region", { name: "Claude windows" });
		const labels = within(group).getAllByText(/^5 h session$|^Weekly · all models$|^Fable only$/);
		expect(labels.map((label) => label.textContent)).toEqual(["5 h session", "Weekly · all models", "Fable only"]);
		expect(within(group).getByText("Counts within the weekly limit")).toBeTruthy();
		expect(within(group).getByRole("progressbar", { name: "Claude Weekly · Fable only used" }).getAttribute("value")).toBe("68");
		expect(within(screen.getByRole("region", { name: "Codex windows" })).queryByText("Fable only")).toBeNull();
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
