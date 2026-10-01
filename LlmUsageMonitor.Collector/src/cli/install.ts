import { spawn } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { readJson, writeJsonAtomic } from "../files";
import { env } from "../paths";
import { isStandalone, run, runChecked } from "./process";

export const TASK_NAME = "LLM Usage Collector";
export const SYSTEMD_UNIT = "llm-usage-collector";
const CRON_MARKER = "# llm-usage-collector";

export type Scheduler = "schtasks" | "systemd" | "cron";

export type InstallRecord = { path: string; scheduler: Scheduler; installedAt: string; version: string };

const recordFile = (dataDir: string) => path.join(dataDir, "install.json");

export async function readInstallRecord(dataDir: string): Promise<InstallRecord | undefined> {
	return await readJson<InstallRecord>(recordFile(dataDir));
}

export async function writeInstallRecord(dataDir: string, record: InstallRecord): Promise<void> {
	await writeJsonAtomic(recordFile(dataDir), record);
}

/** `%LOCALAPPDATA%\Programs\llm-usage\llm-usage.exe`, or `~/.local/bin/llm-usage`. */
export function defaultInstallPath(): string {
	const home = os.homedir();
	if (process.platform === "win32") return path.join(env("LOCALAPPDATA") ?? path.join(home, "AppData", "Local"), "Programs", "llm-usage", "llm-usage.exe");
	return path.join(home, ".local", "bin", "llm-usage");
}

/**
 * Copies the running executable to its stable place and schedules `sync` every 5 minutes for the current user.
 * Installing again replaces the binary and the schedule.
 */
export async function install(dataDir: string, version: string): Promise<InstallRecord> {
	if (!isStandalone()) throw new Error("`install` only works from the standalone executable. From npm, schedule `llm-usage sync` yourself.");

	const target = defaultInstallPath();
	if (!samePath(process.execPath, target)) await replaceFile(process.execPath, target);

	const scheduler = await schedule(target);
	const record: InstallRecord = { path: target, scheduler, installedAt: new Date().toISOString(), version };
	await writeInstallRecord(dataDir, record);
	return record;
}

/** Removes the schedule and the installed executable; `purge` also removes the data folder. */
export async function uninstall(dataDir: string, { purge }: { purge: boolean }): Promise<InstallRecord | undefined> {
	const record = await readInstallRecord(dataDir);
	await unschedule(record?.scheduler);

	const target = record?.path ?? defaultInstallPath();
	if (fs.existsSync(target)) await removeExecutable(target);

	if (purge) await fs.promises.rm(dataDir, { recursive: true, force: true });
	else await fs.promises.rm(recordFile(dataDir), { force: true });
	return record;
}

/** Writes <target> from <source> through a temporary file; a running <target> on Windows is moved aside first. */
export async function replaceFile(source: string, target: string): Promise<void> {
	await fs.promises.mkdir(path.dirname(target), { recursive: true });
	const temporary = `${target}.new`;
	await fs.promises.copyFile(source, temporary);
	await moveIntoPlace(temporary, target);
}

/** Renames <temporary> over <target>. Windows cannot overwrite a running executable but can rename it: `<target>.old` is removed on the next run. */
export async function moveIntoPlace(temporary: string, target: string): Promise<void> {
	if (process.platform === "win32") {
		if (fs.existsSync(target)) {
			await fs.promises.rm(`${target}.old`, { force: true });
			await fs.promises.rename(target, `${target}.old`);
		}
	} else {
		await fs.promises.chmod(temporary, 0o755);
	}
	await fs.promises.rename(temporary, target);
}

/** Removes what an update or an install left next to the running executable. */
export async function cleanupPreviousExecutable(): Promise<void> {
	if (isStandalone()) await fs.promises.rm(`${process.execPath}.old`, { force: true }).catch(() => undefined);
}

async function schedule(executable: string): Promise<Scheduler> {
	if (process.platform === "win32") {
		const file = path.join(os.tmpdir(), `llm-usage-task-${process.pid}.xml`);
		// schtasks reads the XML in UTF-16 with its BOM.
		await fs.promises.writeFile(file, Buffer.from(`﻿${windowsTaskXml(executable, new Date())}`, "utf16le"));
		try {
			await runChecked("schtasks", ["/Create", "/TN", TASK_NAME, "/XML", file, "/F"]);
		} finally {
			await fs.promises.rm(file, { force: true });
		}
		return "schtasks";
	}

	if ((await run("systemctl", ["--user", "show-environment"])).code === 0) {
		const dir = systemdUserDir();
		const units = systemdUnits(executable);
		await fs.promises.mkdir(dir, { recursive: true });
		await fs.promises.writeFile(path.join(dir, `${SYSTEMD_UNIT}.service`), units.service);
		await fs.promises.writeFile(path.join(dir, `${SYSTEMD_UNIT}.timer`), units.timer);
		await runChecked("systemctl", ["--user", "daemon-reload"]);
		await runChecked("systemctl", ["--user", "enable", "--now", `${SYSTEMD_UNIT}.timer`]);
		return "systemd";
	}

	const current = await run("crontab", ["-l"]);
	if (current.code === -1) throw new Error("Neither systemd --user nor crontab is available to schedule the sync");
	// `crontab -l` fails when the user has no crontab yet.
	await runChecked("crontab", ["-"], withCronLine(current.code === 0 ? current.stdout : "", executable));
	return "cron";
}

/** Removes the schedule of <scheduler>, or of every scheduler of the platform when unknown. Missing ones are ignored. */
async function unschedule(scheduler: Scheduler | undefined) {
	if (process.platform === "win32") {
		await run("schtasks", ["/Delete", "/TN", TASK_NAME, "/F"]);
		return;
	}
	if (!scheduler || scheduler === "systemd") {
		await run("systemctl", ["--user", "disable", "--now", `${SYSTEMD_UNIT}.timer`]);
		const dir = systemdUserDir();
		const removed = await Promise.all(
			[`${SYSTEMD_UNIT}.service`, `${SYSTEMD_UNIT}.timer`].map((unit) =>
				fs.promises.rm(path.join(dir, unit)).then(
					() => true,
					() => false
				)
			)
		);
		if (removed.some(Boolean)) await run("systemctl", ["--user", "daemon-reload"]);
	}
	if (!scheduler || scheduler === "cron") {
		const current = await run("crontab", ["-l"]);
		if (current.code === 0 && current.stdout.includes(CRON_MARKER)) await runChecked("crontab", ["-"], withoutCronLine(current.stdout));
	}
}

async function removeExecutable(target: string) {
	if (process.platform === "win32" && samePath(process.execPath, target)) {
		// A running executable cannot be deleted on Windows: a detached shell removes it once this process has exited.
		const script = `ping -n 3 127.0.0.1 >nul & del /f /q "${target}" "${target}.old" & rmdir "${path.dirname(target)}"`;
		spawn("cmd.exe", ["/d", "/c", script], { detached: true, stdio: "ignore", windowsHide: true, windowsVerbatimArguments: true }).unref();
		return;
	}
	await fs.promises.rm(target, { force: true });
	await fs.promises.rm(`${target}.old`, { force: true });
	if (process.platform === "win32") await fs.promises.rmdir(path.dirname(target)).catch(() => undefined);
}

function samePath(a: string, b: string) {
	const normalize = (value: string) => (process.platform === "win32" ? path.resolve(value).toLowerCase() : path.resolve(value));
	return normalize(a) === normalize(b);
}

function systemdUserDir() {
	return path.join(env("XDG_CONFIG_HOME") ?? path.join(os.homedir(), ".config"), "systemd", "user");
}

const xmlEscape = (value: string) => value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");

/** Local time without offset, as Task Scheduler expects in StartBoundary. */
const localIso = (date: Date) => {
	const pad = (value: number) => String(value).padStart(2, "0");
	return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:00`;
};

/**
 * A task of the current user, every 5 minutes, on battery too. `conhost --headless` runs the console executable
 * without flashing a window.
 */
export function windowsTaskXml(executable: string, start: Date): string {
	return `<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Uploads the Claude Code and Codex token usage of this workstation to LLM Usage Monitor every 5 minutes.</Description>
  </RegistrationInfo>
  <Triggers>
    <TimeTrigger>
      <Repetition>
        <Interval>PT5M</Interval>
        <StopAtDurationEnd>false</StopAtDurationEnd>
      </Repetition>
      <StartBoundary>${localIso(start)}</StartBoundary>
      <Enabled>true</Enabled>
    </TimeTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <ExecutionTimeLimit>PT15M</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>conhost.exe</Command>
      <Arguments>--headless "${xmlEscape(executable)}" sync</Arguments>
    </Exec>
  </Actions>
</Task>
`;
}

export function systemdUnits(executable: string): { service: string; timer: string } {
	return {
		service: `[Unit]
Description=Upload the Claude Code and Codex token usage to LLM Usage Monitor

[Service]
Type=oneshot
ExecStart="${executable}" sync
`,
		timer: `[Unit]
Description=Run the LLM Usage Collector every 5 minutes

[Timer]
OnCalendar=*:0/5
Persistent=true

[Install]
WantedBy=timers.target
`,
	};
}

export function withCronLine(crontab: string, executable: string): string {
	return `${withoutCronLine(crontab)}*/5 * * * * "${executable}" sync >/dev/null 2>&1 ${CRON_MARKER}\n`;
}

export function withoutCronLine(crontab: string): string {
	const kept = crontab
		.split("\n")
		.filter((line) => !line.includes(CRON_MARKER))
		.join("\n")
		.trimEnd();
	return kept ? `${kept}\n` : "";
}
