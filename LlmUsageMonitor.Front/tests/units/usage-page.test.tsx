import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { afterAll, beforeAll, describe, expect, it } from "vite-plus/test";
import type { TokenUsageReport } from "@/core/apis/generated/types.gen";
import { client } from "@/core/apis/generated/client.gen";
import { UsagePage } from "@pages/UsagePage";
import { apiUrl } from "./fixtures";

const today = new Date();
today.setHours(0, 0, 0, 0);
const from = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 6);

const report: TokenUsageReport = {
	from: from.toISOString(),
	to: new Date().toISOString(),
	step: "day",
	timeZone: "Europe/Paris",
	machines: [
		{ id: "pc-1", name: "PC bureau", lastUploadAt: new Date().toISOString() },
		{ id: "pc-2", name: "Laptop", lastUploadAt: new Date().toISOString() },
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
const server = setupServer(
	http.get(`${apiUrl}/api/token-usage`, ({ request }) => {
		requests.push(new URL(request.url));
		return HttpResponse.json(report);
	})
);

beforeAll(() => {
	client.setConfig({ baseUrl: apiUrl });
	server.listen({ onUnhandledRequest: "error" });
});
afterAll(() => server.close());

describe("UsagePage", () => {
	it("shows the total cost, the providers and the models, and asks the report in the browser time zone", async () => {
		render(
			<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
				<UsagePage />
			</QueryClientProvider>
		);

		const total = await screen.findByRole("region", { name: "Total" });
		expect(within(total).getAllByText(/141,47/).length).toBeGreaterThan(0);
		expect(within(total).getByText("Codex")).toBeTruthy();

		const breakdown = screen.getByRole("region", { name: "Usage par modèles" });
		expect(within(breakdown).getByText("claude-opus-5")).toBeTruthy();
		expect(within(breakdown).getByText("Non tarifé")).toBeTruthy();

		expect(requests[0].searchParams.get("range")).toBe("7d");
		expect(requests[0].searchParams.get("timeZone")).toBe(Intl.DateTimeFormat().resolvedOptions().timeZone);
		expect(requests[0].searchParams.has("machineId")).toBe(false);

		fireEvent.click(screen.getByRole("button", { name: "30 j" }));
		await waitFor(() => expect(requests.at(-1)!.searchParams.get("range")).toBe("30d"));

		fireEvent.click(screen.getByRole("button", { name: "All" }));
		await waitFor(() => expect(requests.at(-1)!.searchParams.get("range")).toBe("all"));
	});
});
