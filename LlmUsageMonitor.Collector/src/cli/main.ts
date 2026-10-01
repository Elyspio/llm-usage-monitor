#!/usr/bin/env node
import { spawn } from "node:child_process";
import os from "node:os";
import path from "node:path";
import { parseArgs } from "node:util";
import packageJson from "../../package.json" with { type: "json" };
import { LlmUsageCollector } from "../collector";
import { readLockOwner } from "../lock";
import { migrateLegacyData } from "../migration";
import { defaultDataDir, env } from "../paths";
import { DeviceAuth, type DevicePrompt } from "./auth";
import { CONFIG_KEYS, isConfigKey, loadConfig, setConfigValue } from "./config";
import { cleanupPreviousExecutable, install, readInstallRecord, uninstall, writeInstallRecord } from "./install";
import { FileLogger } from "./log";
import { isStandalone } from "./process";
import { compareVersions, findLatestRelease, installRelease } from "./update";

const VERSION = packageJson.version;
const dataDir = env("LLM_USAGE_DATA_DIR") ?? defaultDataDir();

const HELP = `llm-usage ${VERSION}: uploads the Claude Code and Codex token usage of this workstation to LLM Usage Monitor.

Usage: llm-usage <command>

  login                      Sign in once with a code (device flow); works over SSH
  logout                     Revoke and forget the session
  sync                       Read the new session log lines and upload the usage (what the schedule runs)
  status                     Workstation, session, last run, installation
  config                     Show the settings
  config set <key> <value>   Keys: ${CONFIG_KEYS.join(", ")}
  config unset <key>         Back to the default value
  install                    Copy this executable to a stable place and run \`sync\` every 5 minutes
  uninstall [--purge]        Remove the schedule and the executable (--purge: the data folder too)
  update [--check]           Install the latest release
  version                    Print the version

Data folder: ${dataDir} (LLM_USAGE_DATA_DIR to change it)`;
const interactive = process.stdout.isTTY === true;

async function main(argv: string[]): Promise<number> {
	const { positionals, values } = parseArgs({
		args: argv,
		allowPositionals: true,
		options: { purge: { type: "boolean" }, check: { type: "boolean" }, help: { type: "boolean", short: "h" }, version: { type: "boolean", short: "v" } },
	});
	const [command, ...rest] = positionals;
	if (values.version || command === "version") return print(VERSION);
	if (values.help || !command || command === "help") return print(HELP);

	await cleanupPreviousExecutable();
	const logger = new FileLogger(path.join(dataDir, "logs", "collector.log"), { echo: interactive });
	const { config } = await loadConfig(dataDir);
	const auth = new DeviceAuth({ dataDir, issuer: config.issuer, clientId: config.clientId });

	switch (command) {
		case "login": {
			await auth.login(showPrompt);
			logger.info("Signed in", { issuer: config.issuer });
			return print(`Signed in. The usage will be uploaded to ${config.apiBaseUrl}.`);
		}
		case "logout":
			return print((await auth.logout()) ? "Signed out." : "Not signed in.");
		case "sync": {
			// A custom data folder (tests, second profile) never takes the data of the Elytools of the workstation.
			const legacy = dataDir === defaultDataDir() ? await migrateLegacyData(dataDir) : null;
			if (legacy) logger.info("Elytools collector data migrated", { from: legacy });
			const collector = new LlmUsageCollector({
				holder: "cli",
				dataDir,
				logger,
				getAccessToken: () => auth.getAccessToken(),
				getSettings: () => ({ apiBaseUrl: config.apiBaseUrl, machineName: config.machineName }),
			});
			const result = await collector.sync();
			logger.info("Sync finished", result);
			if (result.outcome === "failed") return fail(result.error ?? "Sync failed");
			return print(result.outcome === "locked" ? "Another sync of this workstation is running, skipped." : "Usage uploaded.");
		}
		case "status":
			return await status(config, auth);
		case "config":
			return await configCommand(rest);
		case "install": {
			const record = await install(dataDir, VERSION);
			logger.info("Installed", record);
			const lines = [`Installed in ${record.path}, sync every 5 minutes (${record.scheduler}).`];
			if (!(await auth.isSignedIn())) lines.push(`Sign in once: "${record.path}" login`);
			if (record.scheduler === "systemd") lines.push(`On a server, keep the timer running without a session: loginctl enable-linger ${os.userInfo().username}`);
			return print(lines.join("\n"));
		}
		case "uninstall": {
			const record = await uninstall(dataDir, { purge: values.purge === true });
			return print(`Uninstalled${record ? ` (${record.path})` : ""}.${values.purge ? " Data folder removed." : ` The data stays in ${dataDir}.`}`);
		}
		case "update":
			return await update(values.check === true, logger);
		default:
			return fail(`Unknown command: ${command}\n\n${HELP}`);
	}
}

function showPrompt(prompt: DevicePrompt) {
	const url = prompt.verificationUriComplete ?? prompt.verificationUri;
	console.log(`Open ${prompt.verificationUri} and enter the code ${prompt.userCode}`);
	if (prompt.verificationUriComplete) console.log(`or open ${prompt.verificationUriComplete}`);
	console.log(`The code expires in ${Math.round(prompt.expiresIn / 60)} minutes. Waiting…`);
	openBrowser(url);
}

/** Best effort, only with a desktop: over SSH the user opens the URL elsewhere. */
function openBrowser(url: string) {
	const opener =
		process.platform === "win32"
			? { command: "rundll32", args: ["url.dll,FileProtocolHandler", url] }
			: process.env.DISPLAY || process.env.WAYLAND_DISPLAY
				? { command: "xdg-open", args: [url] }
				: null;
	if (!opener) return;
	try {
		spawn(opener.command, opener.args, { detached: true, stdio: "ignore" })
			.on("error", () => undefined)
			.unref();
	} catch {
		// No browser: the URL is printed.
	}
}

async function status(config: Awaited<ReturnType<typeof loadConfig>>["config"], auth: DeviceAuth) {
	const collector = new LlmUsageCollector({ holder: "cli", dataDir, getAccessToken: () => auth.getAccessToken(), getSettings: () => config });
	const current = await collector.getStatus();
	const lock = await readLockOwner(path.join(dataDir, "sync.lock"));
	const record = await readInstallRecord(dataDir);
	const date = (value: string | null) => (value ? new Date(value).toLocaleString() : "never");

	const rows: [string, string][] = [
		["Workstation", `${config.machineName || os.hostname()} (${current.machineId})`],
		["Monitor", config.apiBaseUrl],
		["Session", (await auth.isSignedIn()) ? `signed in (${config.issuer})` : "not signed in, run `llm-usage login`"],
		["Last run", current.lastRunAt ? `${date(current.lastRunAt)} by ${current.lastRunBy ?? "?"}` : "never"],
		["Last success", date(current.lastSuccessAt)],
		["Last error", current.lastError ?? "none"],
		["Logs followed", String(current.trackedFiles)],
		["Hours waiting", String(current.pendingHours)],
		["Running", lock ? `yes (${lock.holder}, pid ${lock.pid}, since ${date(lock.startedAt)})` : "no"],
		["Installed", record ? `${record.path} (${record.scheduler}, ${record.version})` : "no"],
		["Version", VERSION],
		["Data", dataDir],
	];
	const width = Math.max(...rows.map(([label]) => label.length));
	return print(rows.map(([label, value]) => `${label.padEnd(width)}  ${value}`).join("\n"));
}

async function configCommand([action = "get", key, ...values]: string[]) {
	if (action === "get") {
		const { config, overrides } = await loadConfig(dataDir);
		return print(CONFIG_KEYS.map((name) => `${name} = ${config[name] || '""'}${name in overrides ? "" : " (default)"}`).join("\n"));
	}
	if ((action !== "set" && action !== "unset") || !key) return fail("Usage: llm-usage config set <key> <value> | config unset <key>");
	if (!isConfigKey(key)) return fail(`Unknown key ${key}. Keys: ${CONFIG_KEYS.join(", ")}`);
	if (action === "set" && values.length === 0) return fail("Usage: llm-usage config set <key> <value>");
	await setConfigValue(dataDir, key, action === "set" ? values.join(" ") : undefined);
	return print(`${key} ${action === "set" ? "set" : "reset to its default"}.`);
}

async function update(checkOnly: boolean, logger: FileLogger) {
	const release = await findLatestRelease();
	if (!release || compareVersions(release.version, VERSION) <= 0) return print(`Up to date (${VERSION}).`);
	if (checkOnly) return print(`Version ${release.version} is available (installed: ${VERSION}). Run \`llm-usage update\`.`);
	if (!isStandalone()) return fail(`Version ${release.version} is available: update the npm package.`);

	const record = await readInstallRecord(dataDir);
	const target = record?.path ?? process.execPath;
	await installRelease(release, target);
	if (record) await writeInstallRecord(dataDir, { ...record, version: release.version });
	logger.info("Updated", { from: VERSION, to: release.version, path: target });
	return print(`Updated to ${release.version} (${target}).`);
}

function print(message: string) {
	console.log(message);
	return 0;
}

function fail(message: string) {
	console.error(message);
	return 1;
}

main(process.argv.slice(2)).then(
	(code) => (process.exitCode = code),
	(error: Error) => {
		new FileLogger(path.join(dataDir, "logs", "collector.log")).error("Command failed", { command: process.argv[2], error: error.message });
		console.error(process.env.LLM_USAGE_DEBUG ? (error.stack ?? error.message) : error.message);
		process.exitCode = 1;
	}
);
