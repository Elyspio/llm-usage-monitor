import type { Provider, ProviderDashboard, ProviderHealth, UsageWindow } from "@/core/apis/generated/types.gen";

export const providerLabel: Record<Provider, string> = { claude: "Claude", codex: "Codex" };
export const providerColor: Record<Provider, string> = { claude: "#d97757", codex: "#10a37f" };

const windowLabels: Record<string, string> = {
	five_hour: "Session 5 h",
	seven_day: "Hebdo · tous modèles",
	seven_day_opus: "Hebdo · Opus",
	seven_day_sonnet: "Hebdo · Sonnet",
};

/** Known Claude windows by id; the others (Codex slots) by duration. */
export function windowLabel(window: Pick<UsageWindow, "id" | "windowDurationMinutes">): string {
	if (windowLabels[window.id]) return windowLabels[window.id];
	if (window.windowDurationMinutes === 300) return "Fenêtre 5 h";
	if (window.windowDurationMinutes === 10_080) return "Hebdo";
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
			return { title: "Connexion expirée", action: `Relancer « ${login} » sur l'hôte du service.`, severity: "error" };
		case "CREDENTIALS_UNAVAILABLE":
			return { title: "Identifiants introuvables", action: `Se connecter avec « ${login} » sur l'hôte du service.`, severity: "error" };
		case "RATE_LIMITED":
			return { title: "Limité par le provider (429)", action: "Pas de nouvel essai immédiat : attente de 15, 30 puis 60 min.", severity: "warning" };
		case "CLI_UNAVAILABLE":
			return { title: "CLI introuvable", action: "Installer le CLI ou corriger son chemin dans la configuration du service.", severity: "error" };
		case "TIMEOUT":
			return { title: "Délai dépassé", action: "Le provider n'a pas répondu à temps ; la prochaine lecture réessaiera.", severity: "warning" };
		case "FETCH_FAILED":
		case "HTTP_ERROR":
			return { title: "Provider injoignable", action: "Vérifier la connexion réseau de l'hôte du service.", severity: "warning" };
		case "ACCESS_DENIED":
			return { title: "Accès refusé", action: "Le compte n'a pas accès à l'usage : vérifier l'abonnement.", severity: "error" };
		case "INVALID_RESPONSE":
		case "NO_USAGE_DATA":
			return { title: "Réponse inattendue", action: "Le format du provider a peut-être changé : voir les logs du service.", severity: "error" };
		default:
			return { title: code, action: "Voir les logs du service.", severity: "error" };
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
