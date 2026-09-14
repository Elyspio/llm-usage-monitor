import { getDefaultConfig } from "@elyspio/vite-eslint-config";
import { defineConfig } from "vite-plus";

const config = getDefaultConfig({ basePath: import.meta.dirname, port: 3000 });

// Aspire injects the API endpoint; the fallback matches the WebApi launch profile.
const apiUrl = process.env.services__api__https__0 ?? "https://localhost:7231";

// Same origin as in production: the API also answers the Hangfire dashboard, its OIDC callback and the Swagger UI.
// The Host header is kept so the API builds its redirect URIs on https://localhost:3000.
const apiPaths = ["/api", "/hangfire", "/signin-oidc", "/swagger", "/openapi"];

export default defineConfig({
	...config,
	// The OpenAPI document is written by the WebApi build and committed as is.
	fmt: { ...config.fmt, ignorePatterns: [...config.fmt.ignorePatterns, "openapi/**"] },
	server: {
		...config.server,
		proxy: Object.fromEntries(apiPaths.map((path) => [path, { target: apiUrl, secure: false }])),
	},
	test: {
		environment: "jsdom",
		include: ["tests/**/*.test.{ts,tsx}"],
		setupFiles: ["tests/setup.ts"],
	},
});
