import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vite-plus/test";
import type { ProviderDashboard, ResetCreditRun, ResetCreditSettings } from "@/core/apis/generated/types.gen";
import { ResetCredits } from "@components/dashboard/ResetCredits";
import { SettingsPage } from "@pages/SettingsPage";
import { apiUrl, codex, fixtureNow, hour, iso } from "./fixtures";
import { mockApi, renderPage } from "./render";

const server = mockApi();
// Native focus precedes a real button click. fireEvent.click alone leaves JSDOM's document as relatedTarget.
function openReset(name: string) {
	const button = screen.getByRole("button", { name });
	button.focus();
	fireEvent.click(button);
}
const provider: ProviderDashboard = {
	...codex,
	resetCredits: {
		balance: {
			availableCount: 2,
			credits: [
				{ id: "first", remainingUses: 1, expiresAt: iso(hour), grantedAt: iso(-hour), title: "Full reset", isUsable: true, requiresLimit: false, windowIds: [] },
				{ id: "second", remainingUses: 1, expiresAt: iso(2 * hour), grantedAt: iso(-hour), title: "Full reset", isUsable: true, requiresLimit: false, windowIds: [] },
			],
		},
		settings: { autoEnabled: false, beforeExpiryMinutes: 60 },
		nextCreditId: "first",
		nextAttemptAt: null,
		recentRuns: [],
	},
};
const result: ResetCreditRun = {
	id: "21111111-1111-4111-8111-111111111111",
	provider: "codex",
	creditId: "first",
	manual: true,
	automaticKey: null,
	startedAt: iso(0),
	expiresAt: iso(hour),
	status: "succeeded",
	attempts: 1,
	endedAt: iso(0),
	nextRetryAt: null,
	outcome: "reset",
	before: null,
	after: null,
};

describe("Earned reset credits", () => {
	it("shows the count and asks for confirmation before consuming the default credit", async () => {
		let body: unknown = null;
		server.use(
			http.post(`${apiUrl}/api/providers/codex/reset-credits/consume`, async ({ request }) => {
				body = await request.json();
				return HttpResponse.json(result);
			})
		);
		renderPage(<ResetCredits provider={provider} now={fixtureNow} />);
		expect(screen.getByText("2 available")).toBeTruthy();
		openReset("Use selected reset");
		expect(body).toBeNull();
		const dialog = screen.getByRole("dialog");
		fireEvent.click(within(dialog).getByRole("button", { name: "Confirm reset" }));
		await waitFor(() => expect(body).toMatchObject({ creditId: "first", idempotencyKey: expect.any(String) }));
		expect(await screen.findByText("Reset request succeeded.")).toBeTruthy();
	});
	it("allows choosing another credit from the details", async () => {
		let body: unknown = null;
		server.use(
			http.post(`${apiUrl}/api/providers/codex/reset-credits/consume`, async ({ request }) => {
				body = await request.json();
				return HttpResponse.json(result);
			})
		);
		renderPage(<ResetCredits provider={provider} now={fixtureNow} />);
		fireEvent.click(screen.getByRole("button", { name: "Reset details and history" }));
		openReset("Use reset 2");
		fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Confirm reset" }));
		await waitFor(() => expect(body).toMatchObject({ creditId: "second" }));
	});
	it("reuses the same logical request after an HTTP failure", async () => {
		const keys: string[] = [];
		server.use(
			http.post(`${apiUrl}/api/providers/codex/reset-credits/consume`, async ({ request }) => {
				const body = (await request.json()) as { idempotencyKey: string };
				keys.push(body.idempotencyKey);
				return keys.length === 1 ? HttpResponse.error() : HttpResponse.json(result);
			})
		);
		renderPage(<ResetCredits provider={provider} now={fixtureNow} />);
		openReset("Use selected reset");
		fireEvent.click(screen.getByRole("button", { name: "Confirm reset" }));
		fireEvent.click(await screen.findByRole("button", { name: "Retry same request" }));
		await waitFor(() => expect(keys).toHaveLength(2));
		expect(keys[0]).toBe(keys[1]);
	});
	it("distinguishes unknown availability from a known empty balance", () => {
		renderPage(<ResetCredits provider={{ ...codex, resetCredits: { ...provider.resetCredits!, balance: null } }} now={fixtureNow} />);
		expect(screen.getByText("Unknown availability")).toBeTruthy();
		expect(screen.queryByText("0 available")).toBeNull();
	});
	it("resumes a paused automatic request with its persisted key", async () => {
		let body: unknown = null;
		server.use(
			http.post(`${apiUrl}/api/providers/codex/reset-credits/consume`, async ({ request }) => {
				body = await request.json();
				return HttpResponse.json(result);
			})
		);
		renderPage(
			<ResetCredits
				provider={{
					...provider,
					resetCredits: { ...provider.resetCredits!, recentRuns: [{ ...result, manual: false, status: "running", endedAt: null, nextRetryAt: iso(hour) }] },
				}}
				now={fixtureNow}
			/>
		);
		openReset("Use selected reset");
		fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Confirm reset" }));
		await waitFor(() => expect(body).toEqual({ creditId: "first", idempotencyKey: result.id }));
	});
	it("saves the independent automation settings", async () => {
		let body: unknown = null;
		server.use(
			http.put(`${apiUrl}/api/settings/reset-credits`, async ({ request }) => {
				const saved = (await request.json()) as ResetCreditSettings;
				body = saved;
				return HttpResponse.json(saved);
			})
		);
		renderPage(<SettingsPage />);
		const form = await screen.findByRole("form", { name: "Earned resets" });
		fireEvent.click(within(form).getByRole("switch", { name: "Codex automatic earned resets" }));
		fireEvent.change(within(form).getByLabelText("Codex before expiry (minutes)"), { target: { value: "15" } });
		fireEvent.click(within(form).getByRole("button", { name: "Save" }));
		await waitFor(() => expect(body).toEqual({ claude: { autoEnabled: false, beforeExpiryMinutes: 60 }, codex: { autoEnabled: true, beforeExpiryMinutes: 15 } }));
	});
});
