import fs from "node:fs";
import path from "node:path";

/** A sync never lasts that long: past it, the lock of a crashed or hung process is taken over. */
export const LOCK_STALE_MS: number = 15 * 60_000;

type LockOwner = { pid: number; holder: string; startedAt: string };

export type LockOptions = { staleMs?: number; now?: () => number };

/**
 * Takes the lock file of the data folder, so one sync at a time runs on the workstation.
 * Returns the release function, or `null` when another live process holds the lock. A lock whose process is gone, or
 * older than {@link LOCK_STALE_MS}, is taken over.
 */
export async function acquireLock(file: string, holder: string, { staleMs = LOCK_STALE_MS, now = Date.now }: LockOptions = {}): Promise<(() => Promise<void>) | null> {
	await fs.promises.mkdir(path.dirname(file), { recursive: true });
	const owner: LockOwner = { pid: process.pid, holder, startedAt: new Date(now()).toISOString() };

	for (let attempt = 0; attempt < 2; attempt++) {
		try {
			await fs.promises.writeFile(file, JSON.stringify(owner), { flag: "wx" });
			return () => release(file, owner);
		} catch (error) {
			if ((error as NodeJS.ErrnoException).code !== "EEXIST") throw error;
		}

		const current = await readOwner(file);
		if (current && !isStale(current, staleMs, now())) return null;
		await fs.promises.rm(file, { force: true });
	}
	return null;
}

/** Who holds the lock, if anyone. */
export async function readLockOwner(file: string): Promise<LockOwner | null> {
	return await readOwner(file);
}

async function readOwner(file: string): Promise<LockOwner | null> {
	try {
		const content = await fs.promises.readFile(file, "utf-8");
		try {
			return JSON.parse(content) as LockOwner;
		} catch {
			// Created but not written yet, or cut by a crash: only its age tells.
			const { mtimeMs } = await fs.promises.stat(file);
			return { pid: 0, holder: "unknown", startedAt: new Date(mtimeMs).toISOString() };
		}
	} catch (error) {
		if ((error as NodeJS.ErrnoException).code === "ENOENT") return null;
		throw error;
	}
}

function isStale(owner: LockOwner, staleMs: number, now: number) {
	const startedAt = Date.parse(owner.startedAt);
	if (!Number.isFinite(startedAt) || now - startedAt > staleMs) return true;
	return owner.pid > 0 && !isAlive(owner.pid);
}

function isAlive(pid: number) {
	try {
		process.kill(pid, 0);
		return true;
	} catch (error) {
		// EPERM: the process exists but belongs to another user.
		return (error as NodeJS.ErrnoException).code === "EPERM";
	}
}

async function release(file: string, owner: LockOwner) {
	const current = await readOwner(file);
	// Taken over after being considered stale: the file belongs to another process now.
	if (current?.pid === owner.pid && current.startedAt === owner.startedAt) await fs.promises.rm(file, { force: true });
}
