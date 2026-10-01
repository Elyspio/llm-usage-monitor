import type { LlmProvider, LlmTokenCounts } from "./types";

/**
 * Pure parsing of the Claude Code and Codex session logs into hourly buckets.
 * The state is plain JSON: it is persisted between runs so each file is read only once, from where the previous run stopped.
 */

const HOUR_MS = 3_600_000;

/** A counted response: kept to ignore its repetitions, in the same file or in a copied session. */
type SeenEntry = [hour: number, provider: LlmProvider, model: string, input: number, cacheRead: number, cacheWrite: number, output: number];

/** What a Codex file remembers between two reads. */
export type CodexFileState = {
	/** Model of the last turn_context line. */
	model: string;
	/** The file has token_usage_record lines: its token_count lines repeat them and are ignored. */
	hasRecords: boolean;
	/** total_tokens of the last token_count line, to skip its repetitions in the files without records. */
	lastTotal: number;
};

export type UsageState = {
	/** Absolute totals by bucket key (see {@link bucketKey}). */
	buckets: Record<string, LlmTokenCounts>;
	/** Buckets changed since the last successful upload. */
	dirty: Record<string, true>;
	seen: Record<string, SeenEntry>;
};

export const emptyState = (): UsageState => ({ buckets: {}, dirty: {}, seen: {} });

export const emptyCodexFileState = (): CodexFileState => ({ model: "unknown", hasRecords: false, lastTotal: -1 });

/** `provider|model|2026-09-25T14:00:00.000Z` */
export const bucketKey = (provider: LlmProvider, model: string, hour: number) => `${provider}|${model}|${new Date(hour).toISOString()}`;

export function parseBucketKey(key: string): { provider: LlmProvider; model: string; hour: string } {
	const last = key.lastIndexOf("|");
	const first = key.indexOf("|");
	return { provider: key.slice(0, first) as LlmProvider, model: key.slice(first + 1, last), hour: key.slice(last + 1) };
}

const truncateToHour = (time: number) => time - (((time % HOUR_MS) + HOUR_MS) % HOUR_MS);

const toNumber = (value: unknown) => (typeof value === "number" && Number.isFinite(value) && value > 0 ? Math.round(value) : 0);

/**
 * Adds the counts of a response to its bucket. A response already seen replaces its previous counts, so a line written
 * twice (streaming, resumed or forked session) is counted once.
 */
function record(state: UsageState, id: string, provider: LlmProvider, model: string, timestamp: unknown, counts: LlmTokenCounts): void {
	const time = typeof timestamp === "string" ? Date.parse(timestamp) : Number.NaN;
	if (!Number.isFinite(time)) return;
	if (counts.input + counts.cacheRead + counts.cacheWrite + counts.output === 0) return;

	const previous = state.seen[id];
	if (previous) {
		const [hour, previousProvider, previousModel, input, cacheRead, cacheWrite, output] = previous;
		if (input === counts.input && cacheRead === counts.cacheRead && cacheWrite === counts.cacheWrite && output === counts.output) return;
		apply(state, bucketKey(previousProvider, previousModel, hour), { input: -input, cacheRead: -cacheRead, cacheWrite: -cacheWrite, output: -output });
	}

	const hour = previous?.[0] ?? truncateToHour(time);
	state.seen[id] = [hour, provider, model, counts.input, counts.cacheRead, counts.cacheWrite, counts.output];
	apply(state, bucketKey(provider, model, hour), counts);
}

function apply(state: UsageState, key: string, delta: LlmTokenCounts): void {
	const bucket = state.buckets[key] ?? { input: 0, cacheRead: 0, cacheWrite: 0, output: 0 };
	state.buckets[key] = {
		input: Math.max(0, bucket.input + delta.input),
		cacheRead: Math.max(0, bucket.cacheRead + delta.cacheRead),
		cacheWrite: Math.max(0, bucket.cacheWrite + delta.cacheWrite),
		output: Math.max(0, bucket.output + delta.output),
	};
	state.dirty[key] = true;
}

function parse(line: string): any {
	try {
		return JSON.parse(line);
	} catch {
		// A line being written or a corrupted one: the next read starts after the last complete line anyway.
		return null;
	}
}

/**
 * A Claude Code line: an assistant message with its usage. Repeated once per content block, with the same message id
 * and request id.
 */
export function readClaudeLine(state: UsageState, line: string): void {
	if (!line.includes('"usage"') || !line.includes('"assistant"')) return;
	const entry = parse(line);
	const message = entry?.message;
	const usage = message?.usage;
	if (entry?.type !== "assistant" || !usage || typeof message.model !== "string") return;

	const id = `claude:${message.id ?? entry.uuid}:${entry.requestId ?? ""}`;
	record(state, id, "claude", message.model, entry.timestamp, {
		input: toNumber(usage.input_tokens),
		cacheRead: toNumber(usage.cache_read_input_tokens),
		cacheWrite: toNumber(usage.cache_creation_input_tokens),
		output: toNumber(usage.output_tokens),
	});
}

type CodexUsage = { input_tokens?: number; cached_input_tokens?: number; cache_write_input_tokens?: number; output_tokens?: number; total_tokens?: number };

/** Codex counts the cached input inside input_tokens, and the reasoning inside output_tokens. */
function codexCounts(usage: CodexUsage): LlmTokenCounts {
	const cacheRead = toNumber(usage.cached_input_tokens);
	const cacheWrite = toNumber(usage.cache_write_input_tokens);
	return {
		input: Math.max(0, toNumber(usage.input_tokens) - cacheRead - cacheWrite),
		cacheRead,
		cacheWrite,
		output: toNumber(usage.output_tokens),
	};
}

/**
 * A Codex line. Recent sessions write one token_usage_record per response, before its token_count; older ones only
 * write token_count events, whose last_token_usage is the usage of the response.
 */
export function readCodexLine(state: UsageState, file: CodexFileState, fileId: string, line: string): void {
	if (!line.includes('"turn_context"') && !line.includes('"token_usage_record"') && !line.includes('"token_count"')) return;
	const entry = parse(line);
	const payload = entry?.payload;
	if (!payload) return;

	if (entry.type === "turn_context") {
		if (typeof payload.model === "string" && payload.model) file.model = payload.model;
		return;
	}

	if (entry.type === "token_usage_record") {
		file.hasRecords = true;
		if (!payload.usage) return;
		record(state, `codex:${payload.response_id ?? `${fileId}:${entry.timestamp}`}`, "codex", file.model, entry.timestamp, codexCounts(payload.usage));
		return;
	}

	if (entry.type === "event_msg" && payload.type === "token_count" && !file.hasRecords) {
		const info = payload.info;
		const total = toNumber(info?.total_token_usage?.total_tokens);
		if (!info?.last_token_usage || total === file.lastTotal) return;
		file.lastTotal = total;
		record(state, `codex:${fileId}:${total}`, "codex", file.model, entry.timestamp, codexCounts(info.last_token_usage));
	}
}

/** Forgets the responses older than the retention: their session files are gone, so they cannot come back. */
export function prune(state: UsageState, now: number, retentionDays: number): void {
	const limit = now - retentionDays * 24 * HOUR_MS;
	for (const [id, entry] of Object.entries(state.seen)) {
		if (entry[0] < limit) delete state.seen[id];
	}
	for (const key of Object.keys(state.buckets)) {
		if (Date.parse(parseBucketKey(key).hour) < limit && !state.dirty[key]) delete state.buckets[key];
	}
}

/**
 * Splits a chunk of a file into complete lines. The bytes after the last line break are left for the next read: the
 * CLI may still be writing that line.
 */
export function splitLines(chunk: Buffer): { lines: string[]; consumed: number } {
	const end = chunk.lastIndexOf(0x0a);
	if (end < 0) return { lines: [], consumed: 0 };
	return { lines: chunk.subarray(0, end).toString("utf8").split("\n"), consumed: end + 1 };
}
