import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { afterEach, beforeEach, describe, expect, it } from "vite-plus/test";
import { DEFAULT_CONFIG, loadConfig, setConfigValue } from "./config";

let dataDir: string;

beforeEach(async () => {
	dataDir = await fs.promises.mkdtemp(path.join(os.tmpdir(), "llm-usage-config-"));
});

afterEach(async () => {
	await fs.promises.rm(dataDir, { recursive: true, force: true });
});

describe("config", () => {
	it("uses the built-in defaults without a config file", async () => {
		expect(await loadConfig(dataDir)).toEqual({ config: DEFAULT_CONFIG, overrides: {} });
	});

	it("overrides a value and resets it", async () => {
		await setConfigValue(dataDir, "machineName", " laptop ");
		expect((await loadConfig(dataDir)).config.machineName).toBe("laptop");

		await setConfigValue(dataDir, "machineName", undefined);
		expect(await loadConfig(dataDir)).toEqual({ config: DEFAULT_CONFIG, overrides: {} });
	});

	it("refuses a URL setting that is not a URL", async () => {
		await expect(setConfigValue(dataDir, "apiBaseUrl", "monitor.local")).rejects.toThrow("apiBaseUrl must be an http(s) URL");
	});

	it("ignores unknown keys of the file", async () => {
		await fs.promises.writeFile(path.join(dataDir, "config.json"), JSON.stringify({ issuer: "https://idp/realms/x", other: "x" }));

		expect((await loadConfig(dataDir)).overrides).toEqual({ issuer: "https://idp/realms/x" });
	});
});
