import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vite-plus/test";
import { client } from "@/core/apis/generated/client.gen";
import { SettingsPage } from "@pages/SettingsPage";
import { apiUrl } from "./fixtures";

const notifications = {
	url: "https://ntfy.sh",
	topic: "llm_usage",
	tokenDefined: true,
	events: { triggerFailed: true, authExpired: true, readFailed: true, reset: false, triggerSucceeded: true, recovered: true },
	readFailureThreshold: 3,
	lastSendFailure: { at: new Date().toISOString(), message: "ntfy returned HTTP 502." },
};

let lastNotificationBody: unknown = null;

const server = setupServer(
	http.get(`${apiUrl}/api/settings/polling`, () => HttpResponse.json({ claudeIntervalMinutes: 3, codexIntervalMinutes: 3 })),
	http.get(`${apiUrl}/api/settings/triggers`, () => HttpResponse.json({ claude: { autoEnabled: true, model: "haiku" }, codex: { autoEnabled: false, model: "gpt-5.6-luna" } })),
	http.get(`${apiUrl}/api/settings/notifications`, () => HttpResponse.json(notifications)),
	http.put(`${apiUrl}/api/settings/notifications`, async ({ request }) => {
		lastNotificationBody = await request.json();
		return HttpResponse.json(notifications);
	}),
	http.put(`${apiUrl}/api/settings/triggers`, () =>
		HttpResponse.json({ title: "invalid", status: 400, errors: { "codex.model": ["Modèle inconnu du serveur."] } }, { status: 400 })
	)
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
			<SettingsPage />
		</QueryClientProvider>
	);

describe("SettingsPage", () => {
	it("checks the polling interval before sending it", async () => {
		renderPage();
		const form = await screen.findByRole("form", { name: "Lecture de l'usage" });

		fireEvent.change(within(form).getByLabelText("Intervalle Claude (minutes)"), { target: { value: "0" } });
		fireEvent.click(within(form).getByRole("button", { name: "Enregistrer" }));

		expect(within(form).getByText("Entre 1 et 60 minutes.")).toBeTruthy();
	});

	it("shows the field errors returned by the API", async () => {
		renderPage();
		const form = await screen.findByRole("form", { name: "Déclenchement" });

		fireEvent.click(within(form).getByRole("button", { name: "Enregistrer" }));

		expect(await within(form).findByText("Modèle inconnu du serveur.")).toBeTruthy();
	});

	it("never shows the token and keeps it when the field stays empty", async () => {
		renderPage();
		const form = await screen.findByRole("form", { name: "Notifications ntfy" });

		expect((within(form).getByLabelText("Token d'accès") as HTMLInputElement).value).toBe("");
		expect(within(form).getByText(/Un token est défini/)).toBeTruthy();
		expect(within(form).getByText(/ntfy returned HTTP 502/)).toBeTruthy();

		fireEvent.click(within(form).getByRole("button", { name: "Enregistrer" }));

		await expect.poll(() => lastNotificationBody).toMatchObject({ topic: "llm_usage", token: null });
	});
});
