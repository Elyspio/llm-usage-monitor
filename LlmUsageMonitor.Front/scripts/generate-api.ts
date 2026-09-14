import { generateHeyApi } from "@elyspio/vite-eslint-config";

// The WebApi build writes the OpenAPI document into openapi/. The "./" prefix matters: hey-api reads "a/b" as a registry shorthand.
await generateHeyApi({ input: "./openapi/llm-usage-monitor.json", output: "src/core/apis/generated" });
