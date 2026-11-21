import type { Provider, ProviderErrorCode, ProviderHealth, ReasoningEffort, UsageWindow } from "@/core/apis/generated/types.gen";

export const providers: Provider[] = ["claude", "codex", "copilot"];

/** The providers whose windows a prompt opens; Copilot is monitored only. */
export type TriggerProvider = Exclude<Provider, "copilot">;
export const triggerProviders: TriggerProvider[] = ["claude", "codex"];

export const providerLabel: Record<Provider, string> = { claude: "Claude", codex: "Codex", copilot: "Copilot" };
export const providerColor: Record<Provider, string> = { claude: "#d97757", codex: "#10a37f", copilot: "#8957e5" };

/** The Copilot premium requests, renewed each month. */
export const premiumWindowId = "premium_interactions";

/** Providers shown on the window timeline: a calendar month would squeeze the 5 h and weekly windows. */
export const hasTimelineWindows = (provider: Provider) => provider !== "copilot";

/** The Fable weekly limit of Claude, under the id the earned resets give it. */
export const fableWindowId = "seven_day_overage_included";

const claudeWindowColors: Record<string, string> = { five_hour: "#f4a261", [fableWindowId]: "#b5532f" };

/** Colour of a window on the dashboard and in the history: the provider's, lighter for the Claude session and darker for Fable. */
export const windowColor = (provider: Provider, windowId: string) => (provider === "claude" && claudeWindowColors[windowId]) || providerColor[provider];

/** A window counted inside another one, drawn under it: Fable usage is a share of the weekly limit. */
export const parentWindowId: Record<string, string> = { [fableWindowId]: "seven_day" };

/** Label of a nested window next to its parent, where "Weekly" would repeat. */
export const nestedWindowLabel: Record<string, string> = { [fableWindowId]: "Fable only" };

/**
 * Models offered for the trigger prompt: the model ids from the providers' documentation, never the family aliases.
 * The field stays free: the installed CLI is the reference, this list is only a shortcut.
 */
export const modelSuggestions: Record<TriggerProvider, string[]> = {
	claude: ["claude-haiku-5-5"],
	codex: ["gpt-6-luna"],
};

/** Reasoning efforts of the trigger prompt, lowest first; ultra is Codex only. */
export const effortOptions: Record<TriggerProvider, ReasoningEffort[]> = {
	claude: ["none", "low", "medium", "high", "xhigh", "max"],
	codex: ["none", "low", "medium", "high", "xhigh", "max", "ultra"],
};

const windowLabels: Record<string, string> = {
	five_hour: "5 h session",
	seven_day: "Weekly · all models",
	seven_day_opus: "Weekly · Opus",
	seven_day_sonnet: "Weekly · Sonnet",
	[fableWindowId]: "Weekly · Fable only",
	[premiumWindowId]: "Monthly · premium requests",
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

const loginCommand: Record<Provider, string> = { claude: "claude auth login", codex: "codex login --device-auth", copilot: "copilot login" };

export type ErrorInfo = { title: string; action: string; severity: "error" | "warning" };

/** `serviceUser`: the OS account running the service, whose CLI logins are read. */
export function errorInfo(code: ProviderErrorCode, provider: Provider, serviceUser: string): ErrorInfo {
	const login = loginCommand[provider];
	switch (code) {
		case "authExpired":
		case "authRequired":
			return { title: "Login expired", action: `Run "${login}" again on the service host, as user "${serviceUser}".`, severity: "error" };
		case "credentialsUnavailable":
			return { title: "Credentials not found", action: `Log in with "${login}" on the service host, as user "${serviceUser}".`, severity: "error" };
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

/** The trigger window first, then the others by duration; a nested window right after its parent. */
export function sortWindows(windows: UsageWindow[], triggerWindowId: string | null | undefined): UsageWindow[] {
	const sorted = [...windows].sort((a, b) => {
		if (a.id === triggerWindowId) return -1;
		if (b.id === triggerWindowId) return 1;
		return (a.windowDurationMinutes ?? Number.MAX_SAFE_INTEGER) - (b.windowDurationMinutes ?? Number.MAX_SAFE_INTEGER);
	});
	const isNested = (window: UsageWindow) => sorted.some((parent) => parent.id === parentWindowId[window.id]);
	return sorted.filter((window) => !isNested(window)).flatMap((window) => [window, ...sorted.filter((child) => parentWindowId[child.id] === window.id)]);
}

/**
 * Share of a monthly allowance an even use would have consumed by now, from 0 to 100: the month starts one calendar month
 * before its reset. Null when the reset is unknown.
 */
export function monthPace(resetsAt: string | null | undefined, now: number): number | null {
	if (!resetsAt) return null;
	const end = Date.parse(resetsAt);
	if (!Number.isFinite(end)) return null;
	const start = new Date(end);
	start.setUTCMonth(start.getUTCMonth() - 1);
	const span = end - start.getTime();
	return span > 0 ? Math.max(0, Math.min(100, ((now - start.getTime()) / span) * 100)) : null;
}
