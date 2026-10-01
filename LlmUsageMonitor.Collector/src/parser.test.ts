import { describe, expect, it } from "vite-plus/test";
import { bucketKey, emptyCodexFileState, emptyState, parseBucketKey, prune, readClaudeLine, readCodexLine, splitLines } from "./parser";

const hour = Date.parse("2026-09-25T14:00:00.000Z");

const claudeLine = (id: string, requestId: string, timestamp: string, output = 20, model = "claude-opus-5") =>
	JSON.stringify({
		type: "assistant",
		requestId,
		timestamp,
		message: { id, model, usage: { input_tokens: 2, cache_creation_input_tokens: 300, cache_read_input_tokens: 4000, output_tokens: output } },
	});

const codexRecord = (responseId: string, timestamp: string) =>
	JSON.stringify({
		timestamp,
		type: "token_usage_record",
		payload: { response_id: responseId, usage: { input_tokens: 1000, cached_input_tokens: 800, cache_write_input_tokens: 0, output_tokens: 50, total_tokens: 1050 } },
	});

const codexCount = (total: number, timestamp: string) =>
	JSON.stringify({
		timestamp,
		type: "event_msg",
		payload: { type: "token_count", info: { total_token_usage: { total_tokens: total }, last_token_usage: { input_tokens: 100, cached_input_tokens: 60, output_tokens: 10 } } },
	});

const turnContext = (model: string) => JSON.stringify({ timestamp: "2026-09-25T14:00:00.000Z", type: "turn_context", payload: { model } });

describe("Claude Code logs", () => {
	it("counts a message once per hour and model, whatever the repetitions of its line", () => {
		const state = emptyState();

		readClaudeLine(state, claudeLine("msg_1", "req_1", "2026-09-25T14:10:00.000Z"));
		readClaudeLine(state, claudeLine("msg_1", "req_1", "2026-09-25T14:10:00.100Z"));
		readClaudeLine(state, claudeLine("msg_2", "req_2", "2026-09-25T14:59:59.000Z"));
		readClaudeLine(state, claudeLine("msg_3", "req_3", "2026-09-25T15:00:00.000Z", 20, "claude-haiku-4-5"));

		expect(state.buckets[bucketKey("claude", "claude-opus-5", hour)]).toEqual({ input: 4, cacheRead: 8000, cacheWrite: 600, output: 40 });
		expect(state.buckets[bucketKey("claude", "claude-haiku-4-5", hour + 3_600_000)]).toEqual({ input: 2, cacheRead: 4000, cacheWrite: 300, output: 20 });
		expect(Object.keys(state.dirty)).toHaveLength(2);
	});

	it("replaces the counts of a message written again with a larger output", () => {
		const state = emptyState();

		readClaudeLine(state, claudeLine("msg_1", "req_1", "2026-09-25T14:10:00.000Z", 5));
		readClaudeLine(state, claudeLine("msg_1", "req_1", "2026-09-25T14:10:01.000Z", 25));

		expect(state.buckets[bucketKey("claude", "claude-opus-5", hour)].output).toBe(25);
	});

	it("ignores the other lines and the messages without tokens", () => {
		const state = emptyState();

		readClaudeLine(state, JSON.stringify({ type: "user", message: { content: "usage assistant" } }));
		readClaudeLine(state, '{"type":"assistant","usage": broken');
		readClaudeLine(
			state,
			JSON.stringify({
				type: "assistant",
				timestamp: "2026-09-25T14:00:00Z",
				message: { id: "x", model: "<synthetic>", usage: { input_tokens: 0, output_tokens: 0 } },
			})
		);

		expect(state.buckets).toEqual({});
	});
});

describe("Codex logs", () => {
	it("counts the token_usage_records under the model of the turn and ignores their token_count", () => {
		const state = emptyState();
		const file = emptyCodexFileState();

		for (const line of [
			turnContext("gpt-6-astra"),
			codexRecord("resp_1", "2026-09-25T14:01:00.000Z"),
			codexCount(1050, "2026-09-25T14:01:00.100Z"),
			codexRecord("resp_1", "2026-09-25T14:01:00.000Z"),
		])
			readCodexLine(state, file, "rollout-1", line);

		expect(state.buckets).toEqual({ [bucketKey("codex", "gpt-6-astra", hour)]: { input: 200, cacheRead: 800, cacheWrite: 0, output: 50 } });
	});

	it("falls back on token_count in the older sessions, skipping the repeated totals", () => {
		const state = emptyState();
		const file = emptyCodexFileState();

		for (const line of [turnContext("gpt-5.6-sol"), codexCount(110, "2026-09-25T14:01:00Z"), codexCount(110, "2026-09-25T14:01:01Z"), codexCount(220, "2026-09-25T14:02:00Z")])
			readCodexLine(state, file, "rollout-old", line);

		expect(state.buckets[bucketKey("codex", "gpt-5.6-sol", hour)]).toEqual({ input: 80, cacheRead: 120, cacheWrite: 0, output: 20 });
	});
});

describe("state", () => {
	it("round-trips a bucket key with a model containing separators", () => {
		expect(parseBucketKey(bucketKey("codex", "a|b", hour))).toEqual({ provider: "codex", model: "a|b", hour: "2026-09-25T14:00:00.000Z" });
	});

	it("prunes the old responses and uploaded buckets, never the pending ones", () => {
		const state = emptyState();
		readClaudeLine(state, claudeLine("old", "r", "2026-06-01T10:00:00Z"));
		readClaudeLine(state, claudeLine("older", "r", "2026-06-01T11:00:00Z"));
		delete state.dirty[bucketKey("claude", "claude-opus-5", Date.parse("2026-06-01T10:00:00Z"))];

		prune(state, hour, 60);

		expect(state.seen).toEqual({});
		expect(Object.keys(state.buckets)).toEqual([bucketKey("claude", "claude-opus-5", Date.parse("2026-06-01T11:00:00Z"))]);
	});

	it("keeps the unfinished last line for the next read", () => {
		const { lines, consumed } = splitLines(Buffer.from('{"a":1}\n{"b":2}\n{"c":'));

		expect(lines).toEqual(['{"a":1}', '{"b":2}']);
		expect(consumed).toBe(16);
	});
});
