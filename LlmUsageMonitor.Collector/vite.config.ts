import { defaultFmtConfig, defaultLintConfig } from "@elyspio/vite-eslint-config";
import { defineConfig } from "vite-plus";

const ignorePatterns = ["dist/**", "build/**"];

/** The executables embed the Node.js that builds them: the SEA blob must match its version. */
const exeTargets = (["win", "linux"] as const).map((platform) => ({ platform, arch: "x64" as const, nodeVersion: process.versions.node }));

export default defineConfig({
	fmt: {
		...defaultFmtConfig,
		ignorePatterns: [...defaultFmtConfig.ignorePatterns, ...ignorePatterns],
	},
	lint: {
		...defaultLintConfig,
		ignorePatterns: [...defaultLintConfig.ignorePatterns, ...ignorePatterns],
	},
	test: {
		environment: "node",
		include: ["src/**/*.test.ts"],
	},
	pack: [
		{
			// The npm package: the library imported by Elytools and the `llm-usage` bin for `npx`.
			name: "lib",
			entry: { index: "src/index.ts", cli: "src/cli/main.ts" },
			platform: "node",
			format: "esm",
			dts: { entry: "src/index.ts" },
			outDir: "dist",
			clean: true,
		},
		{
			// The standalone executables (Node SEA), one chunk from the CLI entry.
			name: "exe",
			entry: { "llm-usage": "src/cli/main.ts" },
			platform: "node",
			format: "esm",
			dts: false,
			outDir: "build/bundle",
			clean: true,
			exe: { fileName: "llm-usage", outDir: "build", targets: exeTargets },
		},
	],
});
