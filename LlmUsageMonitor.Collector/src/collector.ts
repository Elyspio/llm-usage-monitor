import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { setImmediate } from "node:timers/promises";
import { readJson, readOrCreateMachineId, writeJsonAtomic } from "./files";
import { acquireLock } from "./lock";
import { type CodexFileState, emptyCodexFileState, emptyState, parseBucketKey, prune, readClaudeLine, readCodexLine, splitLines, type UsageState } from "./parser";
import { defaultDataDir, defaultLogSources } from "./paths";
import type { CollectorLogger, CollectorSettings, CollectorStatus, LogSource, PersistedStatus, SyncResult } from "./types";

const CHUNK_SIZE = 8 * 1024 * 1024;
/** The monitor accepts at most 2000 buckets per upload. */
const MAX_BUCKETS = 2000;
/** Longer than the 30 days Claude Code keeps its sessions, so a copied session is still recognised. */
const RETENTION_DAYS = 60;
/** The current hour waits a few minutes, so a workstation clock slightly ahead never sends an hour the monitor sees as future. */
const CLOCK_MARGIN_MS = 5 * 60_000;

type FileState = { offset: number; codex?: CodexFileState };

type PersistedState = {
	version: 1;
	files: Record<string, FileState>;
	usage: UsageState;
};

const emptyStatus = (): PersistedStatus => ({ lastRunAt: null, lastSuccessAt: null, lastError: null, lastRunBy: null, trackedFiles: 0, pendingHours: 0 });

const silentLogger: CollectorLogger = { debug() {}, info() {}, warn() {}, error() {} };

export type LlmUsageCollectorOptions = {
	/** Who runs this collector (`elytools`, `cli`): written in the lock and the status. */
	holder: string;
	/** Bearer token of an account with the `llm-usage-monitor:admin` role, asked before each request. */
	getAccessToken: () => Promise<string>;
	getSettings: () => CollectorSettings | Promise<CollectorSettings>;
	/** @default defaultDataDir() */
	dataDir?: string;
	/** @default defaultLogSources() */
	sources?: LogSource[];
	logger?: CollectorLogger;
	now?: () => number;
};

/**
 * Follows the Claude Code and Codex session logs of this workstation and uploads their hourly token usage to LLM Usage
 * Monitor. The state lives in the data folder, shared by every collector of the workstation and protected by a lock:
 * each run reads it again, so Elytools and the CLI can take turns.
 */
export class LlmUsageCollector {
	readonly dataDir: string;
	private readonly logger: CollectorLogger;
	private readonly now: () => number;
	private current?: Promise<SyncResult>;
	private readonly listeners = new Set<(status: CollectorStatus) => void>();

	constructor(private readonly options: LlmUsageCollectorOptions) {
		this.dataDir = options.dataDir ?? defaultDataDir();
		this.logger = options.logger ?? silentLogger;
		this.now = options.now ?? Date.now;
	}

	private get stateFile() {
		return path.join(this.dataDir, "state.json");
	}

	private get statusFile() {
		return path.join(this.dataDir, "status.json");
	}

	onStatusChange(listener: (status: CollectorStatus) => void): () => void {
		this.listeners.add(listener);
		return () => this.listeners.delete(listener);
	}

	/** Reads the new log lines and uploads the changed hours; a call during a run of this process waits for that run. */
	async sync(): Promise<SyncResult> {
		this.current ??= this.run().finally(() => {
			this.current = undefined;
			void this.notify();
		});
		return await this.current;
	}

	async getStatus(): Promise<CollectorStatus> {
		return {
			...emptyStatus(),
			...(await readJson<PersistedStatus>(this.statusFile).catch(() => undefined)),
			running: this.current !== undefined,
			machineId: await this.getMachineId(),
		};
	}

	getMachineId(): Promise<string> {
		return readOrCreateMachineId(this.dataDir);
	}

	private async run(): Promise<SyncResult> {
		const release = await acquireLock(path.join(this.dataDir, "sync.lock"), this.options.holder, { now: this.now });
		if (!release) {
			this.logger.info("Another collector of this workstation is running a sync, skipped");
			return { outcome: "locked" };
		}

		const status = { ...emptyStatus(), ...(await readJson<PersistedStatus>(this.statusFile).catch(() => undefined)) };
		status.lastRunAt = new Date(this.now()).toISOString();
		status.lastRunBy = this.options.holder;
		void this.notify();

		let state: PersistedState | undefined;
		try {
			state = await this.loadState();
			await this.scan(state);
			prune(state.usage, this.now(), RETENTION_DAYS);
			await this.saveState(state);
			await this.upload(state);
			status.lastSuccessAt = new Date(this.now()).toISOString();
			status.lastError = null;
			return { outcome: "uploaded" };
		} catch (error) {
			status.lastError = (error as Error).message;
			this.logger.error("LLM usage sync failed", { error: status.lastError });
			return { outcome: "failed", error: status.lastError };
		} finally {
			if (state) {
				status.trackedFiles = Object.keys(state.files).length;
				status.pendingHours = Object.keys(state.usage.dirty).length;
			}
			await writeJsonAtomic(this.statusFile, status).catch((error: Error) => this.logger.warn("Cannot write the LLM usage status", { error: error.message }));
			await release();
		}
	}

	private async scan(state: PersistedState) {
		const seen = new Set<string>();
		for (const source of this.options.sources ?? defaultLogSources()) {
			for (const file of await this.listLogs(source.root)) {
				seen.add(file);
				const fileState = (state.files[file] ??= { offset: 0 });
				if (source.kind === "codex") fileState.codex ??= emptyCodexFileState();
				const read = (line: string) => (source.kind === "claude" ? readClaudeLine(state.usage, line) : readCodexLine(state.usage, fileState.codex!, file, line));
				try {
					fileState.offset = await this.readFrom(file, fileState.offset, read);
				} catch (error) {
					this.logger.warn("Cannot read session log", { file, error: (error as Error).message });
				}
			}
		}

		// Files removed by the CLI cleanup are forgotten: their hours stay in the buckets until the retention.
		for (const file of Object.keys(state.files)) {
			if (!seen.has(file)) delete state.files[file];
		}
	}

	private async listLogs(root: string): Promise<string[]> {
		if (!fs.existsSync(root)) return [];
		const entries = await fs.promises.readdir(root, { recursive: true, withFileTypes: true });
		return entries.filter((entry) => entry.isFile() && entry.name.endsWith(".jsonl")).map((entry) => path.join(entry.parentPath, entry.name));
	}

	/** Reads the complete lines written after <offset> and returns the offset of the first unread byte. */
	private async readFrom(file: string, offset: number, onLine: (line: string) => void): Promise<number> {
		const handle = await fs.promises.open(file, "r");
		try {
			const { size } = await handle.stat();
			// A shorter file was rewritten: read it again, the response ids keep the counts right.
			let position = offset > size ? 0 : offset;
			let carry = Buffer.alloc(0);
			while (position + carry.length < size) {
				const buffer = Buffer.alloc(Math.min(CHUNK_SIZE, size - position - carry.length));
				const { bytesRead } = await handle.read(buffer, 0, buffer.length, position + carry.length);
				if (bytesRead === 0) break;
				const chunk = Buffer.concat([carry, buffer.subarray(0, bytesRead)]);
				const { lines, consumed } = splitLines(chunk);
				for (const line of lines) onLine(line);
				position += consumed;
				carry = chunk.subarray(consumed);
				// Large first imports: let the event loop breathe between chunks (the Elytools window, the IPC).
				await setImmediate();
			}
			return position;
		} finally {
			await handle.close();
		}
	}

	private async upload(state: PersistedState) {
		const { apiBaseUrl, machineName } = await this.options.getSettings();
		const baseUrl = apiBaseUrl.trim().replace(/\/$/, "");
		if (!baseUrl) throw new Error("LLM Usage Monitor URL is not configured");

		const ready = this.now() - CLOCK_MARGIN_MS;
		const keys = Object.keys(state.usage.dirty).filter((key) => Date.parse(parseBucketKey(key).hour) <= ready);
		const machineId = await this.getMachineId();

		// An empty upload still records the workstation and its last contact.
		for (let index = 0; index === 0 || index < keys.length; index += MAX_BUCKETS) {
			const batch = keys.slice(index, index + MAX_BUCKETS);
			const token = await this.options.getAccessToken();
			const response = await fetch(`${baseUrl}/api/token-usage`, {
				method: "POST",
				headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
				body: JSON.stringify({
					machineId,
					machineName: machineName.trim() || os.hostname(),
					buckets: batch.map((key) => ({ ...parseBucketKey(key), tokens: state.usage.buckets[key] ?? { input: 0, cacheRead: 0, cacheWrite: 0, output: 0 } })),
				}),
			});

			if (!response.ok) {
				const body = await response.text();
				const hint = response.status === 401 || response.status === 403 ? " (sign in again, the account needs the llm-usage-monitor:admin role)" : "";
				throw new Error(`Upload refused (${response.status})${hint}: ${body.slice(0, 250)}`);
			}

			for (const key of batch) delete state.usage.dirty[key];
			await this.saveState(state);
		}
	}

	private async loadState(): Promise<PersistedState> {
		try {
			const persisted = await readJson<PersistedState>(this.stateFile);
			if (persisted?.version === 1) return persisted;
		} catch (error) {
			this.logger.warn("Unreadable LLM usage state, starting over", { error: (error as Error).message });
		}
		return { version: 1, files: {}, usage: emptyState() };
	}

	private async saveState(state: PersistedState) {
		await writeJsonAtomic(this.stateFile, state);
	}

	private async notify() {
		if (this.listeners.size === 0) return;
		const status = await this.getStatus().catch(() => undefined);
		if (!status) return;
		for (const listener of this.listeners) listener(status);
	}
}
