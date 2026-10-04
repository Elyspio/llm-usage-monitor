import type { DashboardSnapshot, NotificationSettingsView, ProviderDashboard, TriggerRun, UsageHistory } from "@/core/apis/generated/types.gen";

export const apiUrl = "http://api.test";

/**
 * The frozen "now" of every test (tests/setup.ts): a Wednesday at local noon, far from midnight and from the DST changes,
 * so the day labels and the Mondays of the timeline never depend on when the tests run.
 */
export const fixtureNow = new Date(2026, 8, 23, 12, 0, 0).getTime();

export const iso = (offsetMs: number) => new Date(fixtureNow + offsetMs).toISOString();
export const hour = 3_600_000;

const health = { lastSuccessAt: iso(-60_000), lastFailure: null, consecutiveFailures: 0, backoffUntil: null, activeAlerts: [], tokenExpiresAt: null, refreshTokenExpiresAt: null };

export const claude: ProviderDashboard = {
	provider: "claude",
	lastReading: {
		fetchedAt: iso(-60_000),
		windows: [
			{ id: "seven_day", usedPercent: 22, remainingPercent: 78, resetsAt: iso(5 * 24 * hour), windowDurationMinutes: 10_080 },
			{ id: "five_hour", usedPercent: 40, remainingPercent: 60, resetsAt: iso(2 * hour), windowDurationMinutes: 300 },
		],
	},
	triggerWindowId: "five_hour",
	autoTriggerEnabled: true,
	pollIntervalMinutes: 3,
	nextAutoTriggerAt: iso(2 * hour + 60_000),
	runningTrigger: null,
	health,
};

export const codex: ProviderDashboard = {
	provider: "codex",
	lastReading: {
		fetchedAt: iso(-60_000),
		windows: [{ id: "codex/primary", usedPercent: 11, remainingPercent: 89, resetsAt: iso(4 * 24 * hour), windowDurationMinutes: 10_080 }],
	},
	triggerWindowId: "codex/primary",
	autoTriggerEnabled: false,
	pollIntervalMinutes: 3,
	nextAutoTriggerAt: null,
	runningTrigger: null,
	health,
};

export const dashboard: DashboardSnapshot = { providers: [claude, codex], recentTriggerRuns: [] };

export const emptyHistory: UsageHistory = { from: iso(-24 * hour), to: iso(0), series: [], triggerRuns: [] };

/** Claude whose last reading failed after the last success: the values are stale. */
export const degradedClaude: ProviderDashboard = {
	...claude,
	health: { ...health, lastFailure: { code: "AUTH_EXPIRED", message: "The Claude CLI could not refresh its login.", at: iso(-30_000) }, consecutiveFailures: 1 },
};

export const run = (overrides: Partial<TriggerRun> = {}): TriggerRun => ({
	id: "run-1",
	provider: "codex",
	manual: true,
	cycleKey: null,
	model: "gpt-5.6-luna",
	status: "running",
	startedAt: iso(-5 * 60_000),
	endedAt: null,
	errorCode: null,
	error: null,
	attempts: 1,
	nextRetryAt: null,
	durationMs: null,
	...overrides,
});

/** Two readings of the Claude session, the second after a manual trigger. */
export const history24h: UsageHistory = {
	from: iso(-24 * hour),
	to: iso(0),
	series: [
		{
			provider: "claude",
			windowId: "five_hour",
			points: [
				{ fetchedAt: iso(-3 * hour), usedPercent: 10, resetsAt: null },
				{ fetchedAt: iso(-1 * hour), usedPercent: 40, resetsAt: null },
			],
		},
		{ provider: "codex", windowId: "codex/primary", points: [{ fetchedAt: iso(-2 * hour), usedPercent: 11, resetsAt: null }] },
	],
	triggerRuns: [run({ provider: "claude", status: "succeeded", startedAt: iso(-2 * hour) })],
};

export const notificationSettings: NotificationSettingsView = {
	url: "https://ntfy.sh",
	topic: "llm_usage",
	tokenDefined: true,
	events: {
		claude: {
			triggerFailed: true,
			authExpired: true,
			readFailed: true,
			reset: false,
			triggerSucceeded: true,
			recovered: true,
			resetCreditSucceeded: true,
			resetCreditFailed: true,
		},
		codex: {
			triggerFailed: false,
			authExpired: true,
			readFailed: true,
			reset: false,
			triggerSucceeded: true,
			recovered: true,
			resetCreditSucceeded: true,
			resetCreditFailed: true,
		},
	},
	readFailureThreshold: 3,
	credentialExpiryAlertDays: 7,
	lastSendFailure: { at: iso(-hour), message: "ntfy returned HTTP 502." },
};
