import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vite-plus/test";
import type { TokenUsageReport } from "@/core/apis/generated/types.gen";
import { UsagePage } from "@pages/UsagePage";
import { apiUrl, fixtureNow, iso } from "./fixtures";
import { mockApi, renderPage } from "./render";

const today = new Date(fixtureNow);
today.setHours(0, 0, 0, 0);
const from = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 6);

const report: TokenUsageReport = {
	from: from.toISOString(),
	to: iso(0),
	step: "day",
	timeZone: "Europe/Paris",
	machines: [
		{ id: "pc-1", name: "PC bureau", lastUploadAt: iso(0) },
		{ id: "pc-2", name: "Laptop", lastUploadAt: iso(0) },
	],
	rows: [
		{
			start: today.toISOString(),
			provider: "claude",
			model: "claude-opus-5",
			tokens: { input: 1_000, cacheRead: 2_000_000, cacheWrite: 0, output: 5_000 },
			costUsd: 141.47,
			cacheSavingsUsd: 9,
			unpricedTokens: 0,
		},
		{
			start: today.toISOString(),
			provider: "claude",
			model: "<synthetic>",
			tokens: { input: 0, cacheRead: 0, cacheWrite: 0, output: 0 },
			costUsd: null,
			cacheSavingsUsd: null,
			unpricedTokens: 0,
		},
	],
};

const requests: URL[] = [];
const server = mockApi(
	http.get(`${apiUrl}/api/token-usage`, ({ request }) => {
		requests.push(new URL(request.url));
		return HttpResponse.json(report);
	})
);

describe("UsagePage", () => {
	it("shows the total cost, the providers and the models, and asks the report in the browser time zone", async () => {
		renderPage(<UsagePage />);

		const total = await screen.findByRole("region", { name: "Total" });
		expect(within(total).getAllByText(/141\.47/).length).toBeGreaterThan(0);
		expect(within(total).getByText("Codex")).toBeTruthy();

		const breakdown = screen.getByRole("region", { name: "Usage by model" });
		expect(within(breakdown).getByText("claude-opus-5")).toBeTruthy();
		expect(within(breakdown).getByText("Unpriced")).toBeTruthy();

		expect(requests[0].searchParams.get("range")).toBe("7d");
		expect(requests[0].searchParams.get("timeZone")).toBe(Intl.DateTimeFormat().resolvedOptions().timeZone);
		expect(requests[0].searchParams.has("machineId")).toBe(false);

		fireEvent.click(screen.getByRole("button", { name: "30 d" }));
		await waitFor(() => expect(requests.at(-1)!.searchParams.get("range")).toBe("30d"));

		fireEvent.click(screen.getByRole("button", { name: "All" }));
		await waitFor(() => expect(requests.at(-1)!.searchParams.get("range")).toBe("all"));
	});

	it("shows an error with a retry when the usage cannot be loaded", async () => {
		server.use(http.get(`${apiUrl}/api/token-usage`, () => HttpResponse.json({ title: "boom" }, { status: 500 })));

		renderPage(<UsagePage />);

		expect(await screen.findByText("Could not load the usage.")).toBeTruthy();
		expect(screen.getByRole("heading", { level: 1, name: "Usage" })).toBeTruthy();
	});
});
