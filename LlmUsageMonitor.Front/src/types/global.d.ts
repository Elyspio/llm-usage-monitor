import type { RuntimeConfig } from "@/config/runtime.config";

declare global {
	/** Runtime state exposed by the host page. */
	interface Window {
		"llm-usage-monitor"?: {
			/** Environment-specific configuration, set by `/conf.js`. */
			config?: RuntimeConfig;
		};
	}

	interface ImportMetaEnv {
		/** Development Keycloak realm URL, injected by Aspire. */
		readonly VITE_OIDC_AUTHORITY?: string;
		/** Development client identifier, injected by Aspire. */
		readonly VITE_OIDC_CLIENT_ID?: string;
	}
}
