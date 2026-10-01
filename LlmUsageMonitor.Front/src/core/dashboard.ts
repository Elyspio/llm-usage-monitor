import type { Provider, ProviderDashboard, ProviderHealth, UsageWindow } from "@/core/apis/generated/types.gen";

export const providerLabel: Record<Provider, string> = { claude: "Claude", codex: "Codex" };
export const providerColor: Record<Provider, string> = { claude: "#d97757", codex: "#10a37f" };

/** Colour of a window on the dashboard and in the history: the provider's, a lighter orange for the Claude session. */
export const windowColor = (provider: Provider, windowId: string) => (provider === "claude" && windowId === "five_hour" ? "#f4a261" : providerColor[provider]);

/**
 * Models offered for the trigger prompt: the model ids from the providers' documentation, never the family aliases.
 * The field stays free: the installed CLI is the reference, this list is only a shortcut.
 */
export const modelSuggestions: Record<Provider, string[]> = {
	claude: ["claude-haiku-4-5", "claude-sonnet-5", "claude-opus-5", "claude-fable-5-1"],
	codex: ["gpt-6-astra", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.3-codex-spark", "gpt-5.5", "gpt-5.4", "gpt-5.4-mini"],
};

const windowLabels: Record<string, string> = {
	five_hour: "5 h session",
	seven_day: "Weekly · all models",
	seven_day_opus: "Weekly · Opus",
	seven_day_sonnet: "Weekly · Sonnet",
};

/** Known Claude windows by id; the others (Codex slots) by duration. */
export function windowLabel(window: Pick<UsageWindow, "id" | "windowDurationMinutes">): string {
	if (windowLabels[window.id]) return windowLabels[window.id];
	if (window.windowDurationMinutes === 300) return "5 h window";
	if (window.windowDurationMinutes === 10_080) return "Weekly";
	return window.id;
}

export const remainingColor = (remaining: number): "success" | "warning" | "error" => (remaining >= 50 ? "success" : remaining >= 20 ? "warning" : "error");

/** The last failure is newer than the last success: the displayed values are stale. */
export function isDegraded(health: ProviderHealth): boolean {
	const failure = health.lastFailure;
	if (!failure) return false;
	return !health.lastSuccessAt || Date.parse(failure.at) >= Date.parse(health.lastSuccessAt);
}

export type ErrorInfo = { title: string; action: string; severity: "error" | "warning" };

export function errorInfo(code: string, provider: Provider): ErrorInfo {
	const login = provider === "claude" ? "claude auth login" : "codex login --device-auth";
	switch (code) {
		case "AUTH_EXPIRED":
		case "AUTH_REQUIRED":
			return { title: "Login expired", action: `Run "${login}" again on the service host.`, severity: "error" };
		case "CREDENTIALS_UNAVAILABLE":
			return { title: "Credentials not found", action: `Log in with "${login}" on the service host.`, severity: "error" };
		case "RATE_LIMITED":
			return { title: "Rate limited by the provider (429)", action: "No immediate retry: waiting 15, 30 then 60 min.", severity: "warning" };
		case "CLI_UNAVAILABLE":
			return { title: "CLI not found", action: "Install the CLI or fix its path in the service configuration.", severity: "error" };
		case "TIMEOUT":
			return { title: "Timed out", action: "The provider did not answer in time; the next read will retry.", severity: "warning" };
		case "FETCH_FAILED":
		case "HTTP_ERROR":
			return { title: "Provider unreachable", action: "Check the network connection of the service host.", severity: "warning" };
		case "ACCESS_DENIED":
			return { title: "Access denied", action: "The account has no access to its usage: check the subscription.", severity: "error" };
		case "INVALID_RESPONSE":
		case "NO_USAGE_DATA":
			return { title: "Unexpected response", action: "The provider format may have changed: see the service logs.", severity: "error" };
		default:
			return { title: code, action: "See the service logs.", severity: "error" };
	}
}

export type WindowTiming = { start: number; end: number; elapsedPercent: number };

/** Start (reset − duration), end and elapsed share of a window; null when the reset or the duration is unknown. */
export function windowTiming(window: UsageWindow, now: number): WindowTiming | null {
	if (!window.resetsAt || !window.windowDurationMinutes) return null;
	const end = Date.parse(window.resetsAt);
	const start = end - window.windowDurationMinutes * 60_000;
	const elapsedPercent = Math.min(100, Math.max(0, ((now - start) / (end - start)) * 100));
	return { start, end, elapsedPercent };
}

/** The trigger window first, then the others by duration. */
export function sortWindows(windows: UsageWindow[], triggerWindowId: string | null | undefined): UsageWindow[] {
	return [...windows].sort((a, b) => {
		if (a.id === triggerWindowId) return -1;
		if (b.id === triggerWindowId) return 1;
		return (a.windowDurationMinutes ?? Number.MAX_SAFE_INTEGER) - (b.windowDurationMinutes ?? Number.MAX_SAFE_INTEGER);
	});
}

export const isRunning = (provider: ProviderDashboard) => provider.runningTrigger != null;
