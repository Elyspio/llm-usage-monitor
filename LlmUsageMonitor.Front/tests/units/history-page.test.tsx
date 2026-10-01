import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { afterAll, beforeAll, describe, expect, it } from "vite-plus/test";
import { client } from "@/core/apis/generated/client.gen";
import { HistoryPage } from "@pages/HistoryPage";
import { apiUrl, dashboard, emptyHistory } from "./fixtures";

const server = setupServer(
	http.get(`${apiUrl}/api/dashboard`, () => HttpResponse.json(dashboard)),
	http.get(`${apiUrl}/api/history`, () => HttpResponse.json(emptyHistory))
);

beforeAll(() => {
	client.setConfig({ baseUrl: apiUrl });
	server.listen({ onUnhandledRequest: "error" });
});
afterAll(() => server.close());

describe("HistoryPage", () => {
	it("groups usage history and the trigger journal on the dedicated page", async () => {
		render(
			<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
				<HistoryPage />
			</QueryClientProvider>
		);

		expect(await screen.findByRole("heading", { name: "History" })).toBeTruthy();
		expect(screen.getByRole("region", { name: "History" })).toBeTruthy();
		expect(screen.getByRole("region", { name: "Trigger log" })).toBeTruthy();
	});
});
