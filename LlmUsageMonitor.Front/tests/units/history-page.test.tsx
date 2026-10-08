import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vite-plus/test";
import { HistoryPage } from "@pages/HistoryPage";
import { apiUrl, dashboard, history24h, hour, iso, run } from "./fixtures";
import { mockApi, renderPage } from "./render";

const server = mockApi();

describe("HistoryPage", () => {
	it("groups usage history and the trigger journal on the dedicated page", async () => {
		renderPage(<HistoryPage />);

		expect(screen.getByRole("heading", { level: 1, name: "History" })).toBeTruthy();
		expect(await screen.findByRole("region", { name: "History" })).toBeTruthy();
		expect(screen.getByRole("region", { name: "Trigger log" })).toBeTruthy();
		expect(await screen.findByText("No reading over the period.")).toBeTruthy();
		expect(screen.getByText("No trigger yet.")).toBeTruthy();
	});

	it("shows an error with a retry when the history cannot be loaded", async () => {
		server.use(http.get(`${apiUrl}/api/dashboard`, () => HttpResponse.json({ title: "boom" }, { status: 500 })));

		renderPage(<HistoryPage />);

		expect(await screen.findByText("Could not load the history.")).toBeTruthy();
		expect(screen.getByText("Server error (500): see the service logs.")).toBeTruthy();
		expect(screen.getByRole("button", { name: "Retry" })).toBeTruthy();
	});

	it("lists the latest triggers with their mode, effort, outcome, attempts and error", async () => {
		const runs = [
			run({ id: "a", status: "running" }),
			run({ id: "b", provider: "claude", manual: false, status: "succeeded", model: "claude-haiku-4-5", effort: "low", durationMs: 2100, startedAt: iso(-2 * hour) }),
			run({
				id: "c",
				manual: false,
				status: "failed",
				attempts: 3,
				nextRetryAt: iso(10 * 60_000),
				errorCode: "OVERLOADED",
				error: "The provider is overloaded.",
				startedAt: iso(-26 * hour),
			}),
		];
		server.use(http.get(`${apiUrl}/api/dashboard`, () => HttpResponse.json({ ...dashboard, recentTriggerRuns: runs })));

		renderPage(<HistoryPage />);

		const log = await screen.findByRole("region", { name: "Trigger log" });
		expect(within(log).getByText("Last 3")).toBeTruthy();
		const [, running, succeeded, failed] = within(log).getAllByRole("row");
		expect(within(running).getByText("running")).toBeTruthy();
		expect(within(running).getByText("today 11:55")).toBeTruthy();
		expect(within(succeeded).getByText("auto")).toBeTruthy();
		expect(within(succeeded).getByText("succeeded")).toBeTruthy();
		expect(within(succeeded).getByText("claude-haiku-4-5 · low · 2.1 s")).toBeTruthy();
		expect(within(failed).getByText("yesterday 10:00")).toBeTruthy();
		expect(within(failed).getByText("gpt-5.6-luna · 3 attempts · retry scheduled")).toBeTruthy();
		expect(within(failed).getByText("OVERLOADED")).toBeTruthy();
		expect(within(failed).getByText("The provider is overloaded.")).toBeTruthy();
	});
});

describe("HistoryCard", () => {
	const requests: string[] = [];

	it("draws the series, toggles them and switches the period", async () => {
		server.use(
			http.get(`${apiUrl}/api/history`, ({ request }) => {
				requests.push(new URL(request.url).searchParams.get("range")!);
				return HttpResponse.json(history24h);
			})
		);

		renderPage(<HistoryPage />);

		const card = await screen.findByRole("region", { name: "History" });
		const session = await within(card).findByRole("button", { name: "Claude · 5 h session" });
		expect(session.getAttribute("aria-pressed")).toBe("true");
		expect(within(card).getByText("Claude · 5 h session: 60% remaining at the end of the period, lowest 60%.")).toBeTruthy();

		fireEvent.click(session);
		expect(session.getAttribute("aria-pressed")).toBe("false");
		// The text summary follows the chart: the hidden series is no longer described.
		const summary = within(card).getByRole("list", { name: "Chart summary" });
		expect(within(summary).queryByText(/Claude · 5 h session/)).toBeNull();
		expect(within(summary).getByText(/Codex · Weekly/)).toBeTruthy();

		const period = within(card).getByRole("group", { name: "History period" });
		fireEvent.click(within(period).getByRole("button", { name: "7 d" }));
		await waitFor(() => expect(requests.at(-1)).toBe("7d"));
		expect(requests[0]).toBe("24h");
	});

	it("shows an error with a retry when the chart cannot be loaded", async () => {
		server.use(http.get(`${apiUrl}/api/history`, () => HttpResponse.json({ title: "boom" }, { status: 503 })));

		renderPage(<HistoryPage />);

		const card = await screen.findByRole("region", { name: "History" });
		expect(await within(card).findByText("Could not load the history.")).toBeTruthy();
		expect(within(card).getByRole("button", { name: "Retry" })).toBeTruthy();
	});
});
