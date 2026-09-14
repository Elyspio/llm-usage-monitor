// Runtime configuration, loaded before the application. In production the API serves its own version of this file.
window["llm-usage-monitor"] ??= {};

window["llm-usage-monitor"].config = {
	endpoints: {
		apiUrl: window.location.origin,
	},
	oauth: {
		authority: "https://auth.elyspio.fr/realms/internal",
		clientId: "i-llm-usage-monitor",
		callbackUrl: `${window.location.origin}/auth/callback`,
	},
};
