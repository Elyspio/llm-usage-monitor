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
	events: {
		claude: { triggerFailed: true, authExpired: true, readFailed: true, reset: false, triggerSucceeded: true, recovered: true },
		codex: { triggerFailed: false, authExpired: true, readFailed: true, reset: false, triggerSucceeded: true, recovered: true },
	},
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
		HttpResponse.json({ title: "invalid", status: 400, errors: { "codex.model": ["Model unknown to the server."] } }, { status: 400 })
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
		const form = await screen.findByRole("form", { name: "Usage reading" });

		fireEvent.change(within(form).getByLabelText("Claude interval (minutes)"), { target: { value: "0" } });
		fireEvent.click(within(form).getByRole("button", { name: "Save" }));

		expect(within(form).getByText("Between 1 and 60 minutes.")).toBeTruthy();
	});

	it("shows the field errors returned by the API", async () => {
		renderPage();
		const form = await screen.findByRole("form", { name: "Trigger" });
		const table = within(form).getByRole("table", { name: "Trigger per provider" });

		expect(
			within(table)
				.getAllByRole("columnheader")
				.map((header) => header.textContent)
		).toEqual(["Provider", "Automatic after reset", "Model"]);

		fireEvent.click(within(form).getByRole("button", { name: "Save" }));

		expect(await within(form).findByText("Model unknown to the server.")).toBeTruthy();
	});

	it("suggests the documented models and keeps the field free", async () => {
		let body: unknown = null;
		server.use(
			http.put(`${apiUrl}/api/settings/triggers`, async ({ request }) => {
				const saved = (await request.json()) as Record<string, unknown>;
				body = saved;
				return HttpResponse.json(saved);
			})
		);
		renderPage();
		const form = await screen.findByRole("form", { name: "Trigger" });
		const model = within(form).getByLabelText("Claude model");

		fireEvent.change(model, { target: { value: "sonnet" } });
		fireEvent.click(await screen.findByRole("option", { name: "claude-sonnet-5" }));
		fireEvent.change(within(form).getByLabelText("Codex model"), { target: { value: "gpt-5.6-terra" } });
		fireEvent.click(within(form).getByRole("button", { name: "Save" }));

		await expect.poll(() => body).toMatchObject({ claude: { model: "claude-sonnet-5" }, codex: { model: "gpt-5.6-terra" } });
	});

	it("never shows the token and keeps it when the field stays empty", async () => {
		renderPage();
		const form = await screen.findByRole("form", { name: "Notifications ntfy" });

		expect((within(form).getByLabelText("Access token") as HTMLInputElement).value).toBe("");
		expect(within(form).getByText(/A token is set/)).toBeTruthy();
		expect(within(form).getByText(/ntfy returned HTTP 502/)).toBeTruthy();

		fireEvent.click(within(form).getByRole("button", { name: "Save" }));

		await expect.poll(() => lastNotificationBody).toMatchObject({ topic: "llm_usage", token: null });
	});

	it("shows notification events as a provider matrix and saves each provider independently", async () => {
		renderPage();
		const form = await screen.findByRole("form", { name: "Notifications ntfy" });
		const table = within(form).getByRole("table", { name: "Notified events per provider" });

		expect(
			within(table)
				.getAllByRole("columnheader")
				.map((header) => header.textContent)
		).toEqual(["Event", "Claude", "Codex"]);
		const triggerFailed = within(table).getByRole("row", { name: /Automatic trigger failed/ });
		expect((within(triggerFailed).getByRole("switch", { name: "Claude" }) as HTMLInputElement).checked).toBe(true);
		expect((within(triggerFailed).getByRole("switch", { name: "Codex" }) as HTMLInputElement).checked).toBe(false);

		fireEvent.click(within(triggerFailed).getByRole("switch", { name: "Codex" }));
		fireEvent.click(within(form).getByRole("button", { name: "Save" }));

		await expect.poll(() => lastNotificationBody).toMatchObject({ events: { claude: { triggerFailed: true }, codex: { triggerFailed: true } } });
	});
});
