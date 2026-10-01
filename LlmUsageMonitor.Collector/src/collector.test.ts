import fs from "node:fs";
import http from "node:http";
import type { AddressInfo } from "node:net";
import os from "node:os";
import path from "node:path";
import { afterEach, beforeEach, describe, expect, it } from "vite-plus/test";
import { LlmUsageCollector } from "./collector";

type Upload = { machineId: string; machineName: string; buckets: { provider: string; model: string; hour: string; tokens: Record<string, number> }[] };

let root: string;
let server: http.Server;
let baseUrl: string;
let uploads: { authorization?: string; body: Upload }[];
let responseStatus: number;

const now = Date.parse("2026-10-01T12:03:00.000Z");

const claudeLine = (id: string, timestamp: string) =>
	JSON.stringify({ type: "assistant", requestId: `req_${id}`, timestamp, message: { id, model: "claude-opus-5", usage: { input_tokens: 10, output_tokens: 5 } } });

beforeEach(async () => {
	root = await fs.promises.mkdtemp(path.join(os.tmpdir(), "llm-usage-collector-"));
	await fs.promises.mkdir(path.join(root, "claude", "project-a"), { recursive: true });
	uploads = [];
	responseStatus = 204;
	server = http.createServer((request, response) => {
		let body = "";
		request.on("data", (chunk: Buffer) => (body += chunk.toString()));
		request.on("end", () => {
			uploads.push({ authorization: request.headers.authorization, body: JSON.parse(body) as Upload });
			response.writeHead(responseStatus).end(responseStatus === 204 ? undefined : "nope");
		});
	});
	await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
	baseUrl = `http://127.0.0.1:${(server.address() as AddressInfo).port}/`;
});

afterEach(async () => {
	await new Promise((resolve) => server.close(resolve));
	await fs.promises.rm(root, { recursive: true, force: true });
});

const collector = (holder = "cli") =>
	new LlmUsageCollector({
		holder,
		dataDir: path.join(root, "data"),
		sources: [{ kind: "claude", root: path.join(root, "claude") }],
		getAccessToken: async () => "token-1",
		getSettings: () => ({ apiBaseUrl: baseUrl, machineName: "workstation" }),
		now: () => now,
	});

const writeLog = (lines: string[]) => fs.promises.appendFile(path.join(root, "claude", "project-a", "session.jsonl"), lines.map((line) => `${line}\n`).join(""));

describe("collector", () => {
	it("uploads the hours and keeps the one started less than 5 minutes ago for later", async () => {
		await writeLog([claudeLine("m1", "2026-10-01T10:10:00.000Z"), claudeLine("m2", "2026-10-01T12:01:00.000Z")]);

		expect(await collector().sync()).toEqual({ outcome: "uploaded" });

		expect(uploads).toHaveLength(1);
		expect(uploads[0].authorization).toBe("Bearer token-1");
		expect(uploads[0].body.machineName).toBe("workstation");
		expect(uploads[0].body.buckets).toEqual([
			{ provider: "claude", model: "claude-opus-5", hour: "2026-10-01T10:00:00.000Z", tokens: { input: 10, cacheRead: 0, cacheWrite: 0, output: 5 } },
		]);
		expect(await collector().getStatus()).toMatchObject({ lastRunBy: "cli", lastError: null, trackedFiles: 1, pendingHours: 1, running: false });
	});

	it("shares the machine id and the state between the collectors of the workstation", async () => {
		await writeLog([claudeLine("m1", "2026-10-01T10:10:00.000Z")]);
		await collector("cli").sync();
		await writeLog([claudeLine("m3", "2026-10-01T10:40:00.000Z")]);

		await collector("elytools").sync();

		expect(uploads[1].body.machineId).toBe(uploads[0].body.machineId);
		// Absolute total of the hour, read from where the CLI stopped.
		expect(uploads[1].body.buckets[0].tokens).toEqual({ input: 20, cacheRead: 0, cacheWrite: 0, output: 10 });
	});

	it("skips the run while another collector holds the lock", async () => {
		const dataDir = path.join(root, "data");
		await fs.promises.mkdir(dataDir, { recursive: true });
		await fs.promises.writeFile(path.join(dataDir, "sync.lock"), JSON.stringify({ pid: process.pid, holder: "elytools", startedAt: new Date().toISOString() }));

		expect(await collector().sync()).toEqual({ outcome: "locked" });
		expect(uploads).toHaveLength(0);
	});

	it("keeps the hours pending and records the error when the monitor refuses the upload", async () => {
		await writeLog([claudeLine("m1", "2026-10-01T10:10:00.000Z")]);
		responseStatus = 403;

		const result = await collector().sync();

		expect(result.outcome).toBe("failed");
		expect(result.error).toContain("Upload refused (403)");
		const status = await collector().getStatus();
		expect(status.lastError).toContain("403");
		expect(status.pendingHours).toBe(1);
		expect(fs.existsSync(path.join(root, "data", "sync.lock"))).toBe(false);
	});
});
