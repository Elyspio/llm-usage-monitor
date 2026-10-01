export type LlmProvider = "claude" | "codex";

/**
 * Token counts of an hour, in the LLM Usage Monitor contract: input excludes the cached input.
 */
export type LlmTokenCounts = {
	input: number;
	cacheRead: number;
	cacheWrite: number;
	output: number;
};

/** Winston-compatible: the collector only logs a message and an optional object. */
export type CollectorLogger = {
	debug(message: string, meta?: unknown): void;
	info(message: string, meta?: unknown): void;
	warn(message: string, meta?: unknown): void;
	error(message: string, meta?: unknown): void;
};

/** Read before each upload, so a change of the settings applies to the next run. */
export type CollectorSettings = {
	/** Origin of LLM Usage Monitor, e.g. https://monitor.llm.elyspio.fr */
	apiBaseUrl: string;
	/** Label of the workstation in the monitor; the hostname when empty. */
	machineName: string;
};

/** Where the session logs of a CLI are: the `.jsonl` files under <root>, at any depth. */
export type LogSource = { kind: LlmProvider; root: string };

/**
 * - `uploaded`: the logs were read and every finished hour was sent.
 * - `locked`: another process of this workstation (Elytools or the CLI) is running a sync; nothing was done.
 * - `failed`: see `error`, also kept in the status.
 */
export type SyncResult = { outcome: "uploaded" | "locked" | "failed"; error?: string };

/** What the last run left in the data folder, whichever process ran it. */
export type PersistedStatus = {
	lastRunAt: string | null;
	lastSuccessAt: string | null;
	lastError: string | null;
	/** `elytools` or `cli`. */
	lastRunBy: string | null;
	/** Session log files followed on this workstation. */
	trackedFiles: number;
	/** Hours changed locally and not uploaded yet. */
	pendingHours: number;
};

export type CollectorStatus = PersistedStatus & {
	/** A sync of this process is running. */
	running: boolean;
	machineId: string;
};
