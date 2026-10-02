// Runtime configuration of the development server, loaded before the application. In production the API serves its own
// version of this file from its settings. The Keycloak authority comes from Aspire (VITE_OIDC_AUTHORITY): without it the
// application lists the missing entry instead of signing in against another realm.
window["llm-usage-monitor"] ??= {};

window["llm-usage-monitor"].config = {
	endpoints: {
		apiUrl: window.location.origin,
	},
	oauth: {
		clientId: "i-llm-usage-monitor",
		callbackUrl: `${window.location.origin}/auth/callback`,
	},
};
