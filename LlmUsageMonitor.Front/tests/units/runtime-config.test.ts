import { describe, expect, it } from "vite-plus/test";
import { resolveRuntimeConfig } from "@/config/runtime.config";

const page = {
	endpoints: { apiUrl: "https://localhost:3000" },
	oauth: { clientId: "i-llm-usage-monitor", callbackUrl: "https://localhost:3000/auth/callback" },
};

describe("runtime configuration", () => {
	it("takes the Keycloak realm injected by Aspire over the page configuration", () => {
		const { config, missing } = resolveRuntimeConfig(
			{ ...page, oauth: { ...page.oauth, authority: "https://auth.example.org/realms/other" } },
			{ VITE_OIDC_AUTHORITY: "http://localhost:8123/realms/llm-usage-monitor" }
		);

		expect(missing).toEqual([]);
		expect(config.oauth.authority).toBe("http://localhost:8123/realms/llm-usage-monitor");
		expect(config.oauth.clientId).toBe("i-llm-usage-monitor");
	});

	it("lists the missing entries instead of guessing them", () => {
		expect(resolveRuntimeConfig(page, {}).missing).toEqual(["oauth.authority"]);
		expect(resolveRuntimeConfig(undefined, {}).missing).toEqual(["endpoints.apiUrl", "oauth.authority", "oauth.clientId", "oauth.callbackUrl"]);
	});
});
