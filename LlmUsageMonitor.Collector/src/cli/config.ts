import path from "node:path";
import { readJson, writeJsonAtomic } from "../files";

export const CONFIG_KEYS = ["apiBaseUrl", "machineName", "issuer", "clientId"] as const;
export type ConfigKey = (typeof CONFIG_KEYS)[number];
export type CliConfig = Record<ConfigKey, string>;

/** Built into the executable: a workstation of mine only needs `llm-usage login`. */
export const DEFAULT_CONFIG: CliConfig = {
	apiBaseUrl: "https://monitor.llm.elyspio.fr",
	/** Empty: the hostname. */
	machineName: "",
	issuer: "https://auth.elyspio.fr/realms/internal",
	clientId: "i-llm-usage-collector",
};

const URL_KEYS = new Set<ConfigKey>(["apiBaseUrl", "issuer"]);

const configFile = (dataDir: string) => path.join(dataDir, "config.json");

export const isConfigKey = (key: string): key is ConfigKey => (CONFIG_KEYS as readonly string[]).includes(key);

/** The defaults overridden by `config.json`. */
export async function loadConfig(dataDir: string): Promise<{ config: CliConfig; overrides: Partial<CliConfig> }> {
	const overrides = (await readJson<Partial<CliConfig>>(configFile(dataDir))) ?? {};
	const known = Object.fromEntries(Object.entries(overrides).filter(([key, value]) => isConfigKey(key) && typeof value === "string")) as Partial<CliConfig>;
	return { config: { ...DEFAULT_CONFIG, ...known }, overrides: known };
}

/** Sets a value, or removes the override when `value` is undefined. */
export async function setConfigValue(dataDir: string, key: ConfigKey, value: string | undefined): Promise<void> {
	if (value !== undefined && URL_KEYS.has(key)) {
		const url = URL.parse(value);
		if (!url || (url.protocol !== "https:" && url.protocol !== "http:")) throw new Error(`${key} must be an http(s) URL`);
	}
	const { overrides } = await loadConfig(dataDir);
	if (value === undefined) delete overrides[key];
	else overrides[key] = value.trim();
	await writeJsonAtomic(configFile(dataDir), overrides);
}
