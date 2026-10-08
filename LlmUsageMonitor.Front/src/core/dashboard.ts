import type { Provider, ProviderErrorCode, ProviderHealth, UsageWindow } from "@/core/apis/generated/types.gen";

export const providers: Provider[] = ["claude", "codex"];

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

/** The last failure is newer than the last success: the displayed values are stale. */
export function isDegraded(health: ProviderHealth): boolean {
	const failure = health.lastFailure;
	if (!failure) return false;
	return !health.lastSuccessAt || Date.parse(failure.at) >= Date.parse(health.lastSuccessAt);
}

export type ErrorInfo = { title: string; action: string; severity: "error" | "warning" };

export function errorInfo(code: ProviderErrorCode, provider: Provider): ErrorInfo {
	const login = provider === "claude" ? "claude auth login" : "codex login --device-auth";
	switch (code) {
		case "authExpired":
		case "authRequired":
			return { title: "Login expired", action: `Run "${login}" again on the service host.`, severity: "error" };
		case "credentialsUnavailable":
			return { title: "Credentials not found", action: `Log in with "${login}" on the service host.`, severity: "error" };
		case "rateLimited":
			return { title: "Rate limited by the provider (429)", action: "No immediate retry: waiting 15, 30 then 60 min.", severity: "warning" };
		case "cliUnavailable":
			return { title: "CLI not found", action: "Install the CLI or fix its path in the service configuration.", severity: "error" };
		case "timeout":
			return { title: "Timed out", action: "The provider did not answer in time; the next read will retry.", severity: "warning" };
		case "fetchFailed":
		case "httpError":
			return { title: "Provider unreachable", action: "Check the network connection of the service host.", severity: "warning" };
		case "accessDenied":
			return { title: "Access denied", action: "The account has no access to its usage: check the subscription.", severity: "error" };
		case "cliUnsupportedOption":
			return {
				title: "CLI updated: option rejected",
				action: "The CLI updated itself and rejects an option of the service: see the message for its version.",
				severity: "error",
			};
		case "overloaded":
			return { title: "Provider overloaded", action: "Transient: the trigger is retried after 2, 5 then 10 min.", severity: "warning" };
		case "invalidResponse":
		case "noUsageData":
			return { title: "Unexpected response", action: "The provider format may have changed: see the service logs.", severity: "error" };
		default:
			return { title: code, action: "See the service logs.", severity: "error" };
	}
}

export type WindowTiming = { start: number; end: number };

/** Start (reset − duration) and end of a window; null when the reset or the duration is unknown. */
export function windowTiming(window: UsageWindow): WindowTiming | null {
	if (!window.resetsAt || !window.windowDurationMinutes) return null;
	const end = Date.parse(window.resetsAt);
	const start = end - window.windowDurationMinutes * 60_000;
	return { start, end };
}

/** The trigger window first, then the others by duration. */
export function sortWindows(windows: UsageWindow[], triggerWindowId: string | null | undefined): UsageWindow[] {
	return [...windows].sort((a, b) => {
		if (a.id === triggerWindowId) return -1;
		if (b.id === triggerWindowId) return 1;
		return (a.windowDurationMinutes ?? Number.MAX_SAFE_INTEGER) - (b.windowDurationMinutes ?? Number.MAX_SAFE_INTEGER);
	});
}
