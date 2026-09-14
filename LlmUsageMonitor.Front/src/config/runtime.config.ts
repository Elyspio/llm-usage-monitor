export type RuntimeConfig = {
	endpoints: {
		/** Base URL of the API, without the `/api` prefix carried by every route. */
		apiUrl: string;
	};
	oauth: {
		/** OpenID Connect authority (Keycloak realm URL). */
		authority: string;
		/** Public client identifier. */
		clientId: string;
		/** Sign-in callback URL. */
		callbackUrl: string;
	};
};

const origin = window.location.origin;

const runtime: RuntimeConfig = window["llm-usage-monitor"]?.config ?? {
	endpoints: { apiUrl: origin },
	oauth: { authority: "https://oidc.invalid", clientId: "i-llm-usage-monitor", callbackUrl: `${origin}/auth/callback` },
};

/** `/conf.js` values, with the development Keycloak injected by Aspire taking precedence. */
export const runtimeConfig: RuntimeConfig = {
	endpoints: runtime.endpoints,
	oauth: {
		...runtime.oauth,
		authority: import.meta.env.VITE_OIDC_AUTHORITY ?? runtime.oauth.authority,
		clientId: import.meta.env.VITE_OIDC_CLIENT_ID ?? runtime.oauth.clientId,
	},
};
