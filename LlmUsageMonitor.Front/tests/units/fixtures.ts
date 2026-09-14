import type { DashboardSnapshot, ProviderDashboard, UsageHistory } from "@/core/apis/generated/types.gen";

export const apiUrl = "http://api.test";

const now = Date.now();
const iso = (offsetMs: number) => new Date(now + offsetMs).toISOString();
const hour = 3_600_000;

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
