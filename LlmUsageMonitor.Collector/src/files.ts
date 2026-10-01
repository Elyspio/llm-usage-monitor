import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { setTimeout } from "node:timers/promises";

/** Reads a JSON file; `undefined` when it does not exist. */
export async function readJson<T>(file: string): Promise<T | undefined> {
	try {
		return JSON.parse(await fs.promises.readFile(file, "utf-8")) as T;
	} catch (error) {
		if ((error as NodeJS.ErrnoException).code === "ENOENT") return undefined;
		throw error;
	}
}

/**
 * Writes through a temporary file, so a reader never sees half a file. On Windows the rename fails while another
 * process reads the target: it is tried again a few times.
 */
export async function writeJsonAtomic(file: string, value: unknown, mode?: number): Promise<void> {
	await fs.promises.mkdir(path.dirname(file), { recursive: true });
	const temporary = `${file}.${process.pid}.tmp`;
	await fs.promises.writeFile(temporary, JSON.stringify(value), { mode });
	for (let attempt = 1; ; attempt++) {
		try {
			await fs.promises.rename(temporary, file);
			return;
		} catch (error) {
			const code = (error as NodeJS.ErrnoException).code;
			if (attempt >= 5 || (code !== "EPERM" && code !== "EBUSY" && code !== "EACCES")) {
				await fs.promises.rm(temporary, { force: true });
				throw error;
			}
			await setTimeout(50 * attempt);
		}
	}
}

/**
 * The workstation id, generated once in the data folder. Kept apart from the state: losing the state re-reads the logs
 * under the same id, and the monitor replaces the hours with the same totals.
 */
export async function readOrCreateMachineId(dataDir: string): Promise<string> {
	const file = path.join(dataDir, "machine-id");
	const existing = await readMachineId(file);
	if (existing) return existing;

	await fs.promises.mkdir(dataDir, { recursive: true });
	try {
		// Exclusive: a collector starting at the same time keeps the id written first.
		await fs.promises.writeFile(file, crypto.randomUUID(), { flag: "wx" });
	} catch (error) {
		if ((error as NodeJS.ErrnoException).code !== "EEXIST") throw error;
	}
	const created = await readMachineId(file);
	if (!created) throw new Error(`Empty machine id in ${file}`);
	return created;
}

async function readMachineId(file: string) {
	try {
		return (await fs.promises.readFile(file, "utf-8")).trim();
	} catch (error) {
		if ((error as NodeJS.ErrnoException).code === "ENOENT") return "";
		throw error;
	}
}
