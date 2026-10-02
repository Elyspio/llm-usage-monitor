import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vite-plus/test";
import { SettingsPage } from "@pages/SettingsPage";
import { apiUrl, notificationSettings } from "./fixtures";
import { mockApi, renderPage as render, testQueryClient } from "./render";

let lastNotificationBody: unknown = null;
let polling = { claudeIntervalMinutes: 3, codexIntervalMinutes: 3 };

const server = mockApi(
	http.get(`${apiUrl}/api/settings/polling`, () => HttpResponse.json(polling)),
	http.put(`${apiUrl}/api/settings/polling`, async ({ request }) => {
		polling = (await request.json()) as typeof polling;
		return HttpResponse.json(polling);
	}),
	http.put(`${apiUrl}/api/settings/notifications`, async ({ request }) => {
		lastNotificationBody = await request.json();
		return HttpResponse.json(notificationSettings);
	}),
	http.put(`${apiUrl}/api/settings/triggers`, () =>
		HttpResponse.json({ title: "invalid", status: 400, errors: { "codex.model": ["Model unknown to the server."] } }, { status: 400 })
	)
);

const renderPage = (queryClient = testQueryClient()) => render(<SettingsPage />, { queryClient });

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

		await expect.poll(() => lastNotificationBody).toMatchObject({ topic: "llm_usage", token: null, credentialExpiryAlertDays: 7 });
	});

	it("asks for the token again when the ntfy server changes", async () => {
		lastNotificationBody = null;
		renderPage();
		const form = await screen.findByRole("form", { name: "Notifications ntfy" });

		fireEvent.change(within(form).getByLabelText("ntfy server"), { target: { value: "https://ntfy.example.org" } });
		fireEvent.click(within(form).getByRole("button", { name: "Save" }));

		expect(within(form).getByText("The server changed: enter its token again, or remove the token.")).toBeTruthy();
		expect(lastNotificationBody).toBeNull();

		fireEvent.change(within(form).getByLabelText("Access token"), { target: { value: "tk_new_server" } });
		fireEvent.click(within(form).getByRole("button", { name: "Save" }));

		await expect.poll(() => lastNotificationBody).toMatchObject({ url: "https://ntfy.example.org", token: "tk_new_server" });
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
		expect((within(triggerFailed).getByRole("switch", { name: "Automatic trigger failed — Claude" }) as HTMLInputElement).checked).toBe(true);
		expect((within(triggerFailed).getByRole("switch", { name: "Automatic trigger failed — Codex" }) as HTMLInputElement).checked).toBe(false);
		// Every switch is named after its event and its provider.
		expect(
			within(table)
				.getAllByRole("switch")
				.map((input) => input.getAttribute("aria-label"))
		).toContain("Back to normal — Codex");

		fireEvent.click(within(triggerFailed).getByRole("switch", { name: "Automatic trigger failed — Codex" }));
		fireEvent.click(within(form).getByRole("button", { name: "Save" }));

		await expect.poll(() => lastNotificationBody).toMatchObject({ events: { claude: { triggerFailed: true }, codex: { triggerFailed: true } } });
	});

	it("shows the saved values when coming back to the page", async () => {
		polling = { claudeIntervalMinutes: 3, codexIntervalMinutes: 3 };
		const queryClient = testQueryClient();
		const page = renderPage(queryClient);
		const form = await screen.findByRole("form", { name: "Usage reading" });

		fireEvent.change(within(form).getByLabelText("Claude interval (minutes)"), { target: { value: "5" } });
		fireEvent.click(within(form).getByRole("button", { name: "Save" }));
		expect(within(await within(form).findByRole("status")).getByText("Saved, applied immediately.")).toBeTruthy();

		fireEvent.change(within(form).getByLabelText("Claude interval (minutes)"), { target: { value: "10" } });
		expect(within(form).queryByText("Saved, applied immediately.")).toBeNull();

		page.unmount();
		renderPage(queryClient);
		const again = await screen.findByRole("form", { name: "Usage reading" });
		expect((within(again).getByLabelText("Claude interval (minutes)") as HTMLInputElement).value).toBe("5");
	});

	it("follows the server values while the form is untouched", async () => {
		polling = { claudeIntervalMinutes: 3, codexIntervalMinutes: 3 };
		const queryClient = testQueryClient();
		renderPage(queryClient);
		const form = await screen.findByRole("form", { name: "Usage reading" });

		polling = { claudeIntervalMinutes: 12, codexIntervalMinutes: 3 };
		await queryClient.refetchQueries();

		await waitFor(() => expect((within(form).getByLabelText("Claude interval (minutes)") as HTMLInputElement).value).toBe("12"));
	});

	it("tells a server failure apart from a field error", async () => {
		server.use(http.put(`${apiUrl}/api/settings/polling`, () => HttpResponse.json({ title: "boom", status: 500 }, { status: 500 })));
		renderPage();
		const form = await screen.findByRole("form", { name: "Usage reading" });

		fireEvent.click(within(form).getByRole("button", { name: "Save" }));

		expect(await within(form).findByText("Server error (500): see the service logs.")).toBeTruthy();
	});

	it("removes the stored token on demand", async () => {
		lastNotificationBody = null;
		renderPage();
		const form = await screen.findByRole("form", { name: "Notifications ntfy" });

		fireEvent.click(within(form).getByRole("switch", { name: "Remove the token" }));
		expect((within(form).getByLabelText("Access token") as HTMLInputElement).disabled).toBe(true);
		fireEvent.click(within(form).getByRole("button", { name: "Save" }));

		await expect.poll(() => lastNotificationBody).toMatchObject({ token: "" });
	});

	it("sends a test notification and tells how it went", async () => {
		let status = 204;
		server.use(http.post(`${apiUrl}/api/settings/notifications/test`, () => new HttpResponse(null, { status })));
		renderPage();
		const form = await screen.findByRole("form", { name: "Notifications ntfy" });

		fireEvent.click(within(form).getByRole("button", { name: "Send a test" }));
		expect(await within(form).findByText("Test notification sent.")).toBeTruthy();

		status = 502;
		fireEvent.click(within(form).getByRole("button", { name: "Send a test" }));
		expect(await within(form).findByText("The test send failed.")).toBeTruthy();
	});

	it("cannot send a test without a topic", async () => {
		server.use(http.get(`${apiUrl}/api/settings/notifications`, () => HttpResponse.json({ ...notificationSettings, topic: null })));
		renderPage();
		const form = await screen.findByRole("form", { name: "Notifications ntfy" });

		expect((within(form).getByRole("button", { name: "Send a test" }) as HTMLButtonElement).disabled).toBe(true);
	});

	it("shows an error with a retry when the settings cannot be loaded", async () => {
		server.use(http.get(`${apiUrl}/api/settings/triggers`, () => HttpResponse.json({ title: "boom" }, { status: 500 })));
		renderPage();

		expect(await screen.findByText("Could not load the settings.")).toBeTruthy();
		expect(screen.getByRole("heading", { level: 1, name: "Settings" })).toBeTruthy();
		expect(screen.getByRole("button", { name: "Retry" })).toBeTruthy();
	});

	it("keeps an edit made while the save is running", async () => {
		polling = { claudeIntervalMinutes: 3, codexIntervalMinutes: 3 };
		let release = () => {};
		server.use(
			http.put(`${apiUrl}/api/settings/polling`, async ({ request }) => {
				const body = (await request.json()) as typeof polling;
				await new Promise<void>((resolve) => (release = resolve));
				polling = body;
				return HttpResponse.json(body);
			})
		);
		renderPage();
		const form = await screen.findByRole("form", { name: "Usage reading" });
		const claude = () => within(form).getByLabelText("Claude interval (minutes)") as HTMLInputElement;

		fireEvent.change(claude(), { target: { value: "5" } });
		fireEvent.click(within(form).getByRole("button", { name: "Save" }));
		await waitFor(() => expect(within(form).getByRole("button", { name: "Save" }).hasAttribute("disabled")).toBe(true));
		fireEvent.change(claude(), { target: { value: "10" } });
		release();

		await waitFor(() => expect(polling.claudeIntervalMinutes).toBe(5));
		await waitFor(() => expect(within(form).getByRole("button", { name: "Save" }).hasAttribute("disabled")).toBe(false));
		expect(claude().value).toBe("10");
	});
});
