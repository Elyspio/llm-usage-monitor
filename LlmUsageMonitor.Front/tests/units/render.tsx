import { CssBaseline, ThemeProvider } from "@mui/material";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import { http, HttpResponse, type RequestHandler } from "msw";
import { setupServer } from "msw/node";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router";
import { afterAll, afterEach, beforeAll } from "vite-plus/test";
import { client } from "@/core/apis/generated/client.gen";
import { theme } from "@/view/theme";
import { apiUrl, dashboard, emptyHistory, hour, iso, notificationSettings } from "./fixtures";

/** Answers of a healthy API; a test overrides one with `server.use(...)`, reset after each test. */
const defaultHandlers: RequestHandler[] = [
	http.get(`${apiUrl}/api/dashboard`, () => HttpResponse.json(dashboard)),
	http.get(`${apiUrl}/api/history`, () => HttpResponse.json(emptyHistory)),
	http.get(`${apiUrl}/api/settings/polling`, () => HttpResponse.json({ claudeIntervalMinutes: 3, codexIntervalMinutes: 3 })),
	http.get(`${apiUrl}/api/settings/triggers`, () => HttpResponse.json({ claude: { autoEnabled: true, model: "haiku" }, codex: { autoEnabled: false, model: "gpt-5.6-luna" } })),
	http.get(`${apiUrl}/api/settings/notifications`, () => HttpResponse.json(notificationSettings)),
	http.get(`${apiUrl}/api/token-usage`, () => HttpResponse.json({ from: iso(-7 * 24 * hour), to: iso(0), step: "day", timeZone: "Europe/Paris", machines: [], rows: [] })),
];

/** The MSW server of a test file, started for the whole file; an unexpected request fails the test. */
export function mockApi(...handlers: RequestHandler[]) {
	const server = setupServer(...handlers, ...defaultHandlers);
	beforeAll(() => {
		client.setConfig({ baseUrl: apiUrl });
		server.listen({ onUnhandledRequest: "error" });
	});
	afterEach(() => server.resetHandlers());
	afterAll(() => server.close());
	return server;
}

/** A query client without retry: a failure shows at once. */
export const testQueryClient = () => new QueryClient({ defaultOptions: { queries: { retry: false } } });

/** Renders a page as the application does: theme, query client and a router at `path`. */
export function renderPage(page: ReactNode, { queryClient = testQueryClient(), path = "/" }: { queryClient?: QueryClient; path?: string } = {}) {
	const wrap = (ui: ReactNode) => (
		<ThemeProvider theme={theme}>
			<CssBaseline />
			<QueryClientProvider client={queryClient}>
				<MemoryRouter initialEntries={[path]}>{ui}</MemoryRouter>
			</QueryClientProvider>
		</ThemeProvider>
	);
	const result = render(wrap(page));
	return { ...result, queryClient, rerenderPage: (next: ReactNode) => result.rerender(wrap(next)) };
}
