import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { afterEach, beforeEach, describe, expect, it } from "vite-plus/test";
import { acquireLock, readLockOwner } from "./lock";

let dir: string;
let file: string;

beforeEach(async () => {
	dir = await fs.promises.mkdtemp(path.join(os.tmpdir(), "llm-usage-lock-"));
	file = path.join(dir, "sync.lock");
});

afterEach(async () => {
	await fs.promises.rm(dir, { recursive: true, force: true });
});

describe("sync lock", () => {
	it("is held by one process at a time and freed by its release", async () => {
		const release = await acquireLock(file, "cli");

		expect(release).not.toBeNull();
		expect(await acquireLock(file, "elytools")).toBeNull();

		await release!();
		expect(fs.existsSync(file)).toBe(false);
		expect(await acquireLock(file, "elytools")).not.toBeNull();
	});

	it("takes over the lock of a process that is gone", async () => {
		await fs.promises.writeFile(file, JSON.stringify({ pid: 2 ** 22 + 12_345, holder: "cli", startedAt: new Date().toISOString() }));

		expect(await acquireLock(file, "elytools")).not.toBeNull();
		expect((await readLockOwner(file))?.holder).toBe("elytools");
	});

	it("takes over a lock older than 15 minutes, even of a live process", async () => {
		await fs.promises.writeFile(file, JSON.stringify({ pid: process.pid, holder: "cli", startedAt: new Date(Date.now() - 16 * 60_000).toISOString() }));

		expect(await acquireLock(file, "elytools")).not.toBeNull();
	});

	it("keeps a recent lock it cannot read, and takes it over once old", async () => {
		await fs.promises.writeFile(file, "");

		expect(await acquireLock(file, "cli")).toBeNull();
		expect(await acquireLock(file, "cli", { now: () => Date.now() + 16 * 60_000 })).not.toBeNull();
	});

	it("does not remove a lock taken over by another process", async () => {
		const release = await acquireLock(file, "cli");
		await fs.promises.writeFile(file, JSON.stringify({ pid: process.pid, holder: "elytools", startedAt: "2026-10-01T00:00:00.000Z" }));

		await release!();

		expect((await readLockOwner(file))?.holder).toBe("elytools");
	});
});
