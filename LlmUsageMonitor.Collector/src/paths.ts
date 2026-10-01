import os from "node:os";
import path from "node:path";
import type { LogSource } from "./types";

/** An environment variable, an empty value counting as unset. */
export const env = (name: string): string | undefined => process.env[name] || undefined;

/**
 * The data folder shared by every collector of the workstation (Elytools and the CLI): one machine id, one state.
 * `%LOCALAPPDATA%\elyspio\llm-usage` on Windows, `$XDG_DATA_HOME/elyspio/llm-usage` elsewhere.
 */
export function defaultDataDir(): string {
	const home = os.homedir();
	const base = process.platform === "win32" ? (env("LOCALAPPDATA") ?? path.join(home, "AppData", "Local")) : (env("XDG_DATA_HOME") ?? path.join(home, ".local", "share"));
	return path.join(base, "elyspio", "llm-usage");
}

/**
 * Where Elytools kept its collector data before the shared folder: its app folder is under %LOCALAPPDATA%, or under
 * ~/Library/Application Support when the variable is not set (Linux included).
 */
export function legacyElytoolsDirs(): string[] {
	const roots = [env("LOCALAPPDATA"), path.join(os.homedir(), "Library", "Application Support")].filter((root): root is string => !!root);
	return [...new Set(roots)].map((root) => path.join(root, "elytools", "llm-usage"));
}

/** The session logs of Claude Code and Codex, with the same environment overrides as the CLIs. */
export function defaultLogSources(): LogSource[] {
	const home = os.homedir();
	const claudeRoots = [env("CLAUDE_CONFIG_DIR") ?? path.join(home, ".claude"), path.join(home, ".config", "claude")];
	const codexHome = env("CODEX_HOME") ?? path.join(home, ".codex");
	return [
		...[...new Set(claudeRoots)].map((root) => ({ kind: "claude" as const, root: path.join(root, "projects") })),
		{ kind: "codex", root: path.join(codexHome, "sessions") },
		{ kind: "codex", root: path.join(codexHome, "archived_sessions") },
	];
}
