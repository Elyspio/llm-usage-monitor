export type RuntimeConfig = {
	/** Version of the running build, `0.0.0-dev` outside a release. */
	version: string;
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

/** What `/conf.js` sets: in development the authority is left to Aspire. */
export type PageConfig = { version?: string; endpoints?: Partial<RuntimeConfig["endpoints"]>; oauth?: Partial<RuntimeConfig["oauth"]> };

/** Development Keycloak injected by Aspire, preferred over `/conf.js`. */
export type ConfigEnv = { VITE_OIDC_AUTHORITY?: string; VITE_OIDC_CLIENT_ID?: string };

/**
 * `/conf.js` values, with the development Keycloak injected by Aspire taking precedence. Nothing is guessed: a missing entry
 * is listed in `missing`, and the application shows it instead of starting.
 */
export function resolveRuntimeConfig(page: PageConfig | undefined, env: ConfigEnv): { config: RuntimeConfig; missing: string[] } {
	const config: RuntimeConfig = {
		version: page?.version || "0.0.0-dev",
		endpoints: { apiUrl: page?.endpoints?.apiUrl ?? "" },
		oauth: {
			authority: env.VITE_OIDC_AUTHORITY || page?.oauth?.authority || "",
			clientId: env.VITE_OIDC_CLIENT_ID || page?.oauth?.clientId || "",
			callbackUrl: page?.oauth?.callbackUrl ?? "",
		},
	};
	const entries = {
		"endpoints.apiUrl": config.endpoints.apiUrl,
		"oauth.authority": config.oauth.authority,
		"oauth.clientId": config.oauth.clientId,
		"oauth.callbackUrl": config.oauth.callbackUrl,
	};
	const missing = Object.entries(entries)
		.filter(([, value]) => !value.trim())
		.map(([key]) => key);
	return { config, missing };
}

const resolved = resolveRuntimeConfig(window["llm-usage-monitor"]?.config, import.meta.env);

export const runtimeConfig: RuntimeConfig = resolved.config;

/** Entries missing from the runtime configuration; empty when the application can start. */
export const missingRuntimeConfig: string[] = resolved.missing;
