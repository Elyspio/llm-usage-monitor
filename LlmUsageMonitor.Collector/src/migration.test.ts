import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { afterEach, beforeEach, describe, expect, it } from "vite-plus/test";
import { migrateLegacyData } from "./migration";

let root: string;
let dataDir: string;
let legacyDir: string;

beforeEach(async () => {
	root = await fs.promises.mkdtemp(path.join(os.tmpdir(), "llm-usage-migration-"));
	dataDir = path.join(root, "elyspio", "llm-usage");
	legacyDir = path.join(root, "elytools", "llm-usage");
	await fs.promises.mkdir(legacyDir, { recursive: true });
	await fs.promises.writeFile(path.join(legacyDir, "machine-id"), "legacy-id");
	await fs.promises.writeFile(path.join(legacyDir, "state.json"), '{"version":1}');
});

afterEach(async () => {
	await fs.promises.rm(root, { recursive: true, force: true });
});

describe("legacy data migration", () => {
	it("moves the machine id and the state of Elytools into the shared folder", async () => {
		expect(await migrateLegacyData(dataDir, [legacyDir])).toBe(legacyDir);

		expect(await fs.promises.readFile(path.join(dataDir, "machine-id"), "utf-8")).toBe("legacy-id");
		expect(await fs.promises.readFile(path.join(dataDir, "state.json"), "utf-8")).toBe('{"version":1}');
		expect(fs.existsSync(legacyDir)).toBe(false);
	});

	it("leaves a shared folder that already has an id untouched", async () => {
		await fs.promises.mkdir(dataDir, { recursive: true });
		await fs.promises.writeFile(path.join(dataDir, "machine-id"), "cli-id");

		expect(await migrateLegacyData(dataDir, [legacyDir])).toBeNull();

		expect(await fs.promises.readFile(path.join(dataDir, "machine-id"), "utf-8")).toBe("cli-id");
		expect(fs.existsSync(path.join(legacyDir, "machine-id"))).toBe(true);
	});

	it("does nothing without legacy data", async () => {
		expect(await migrateLegacyData(dataDir, [path.join(root, "missing")])).toBeNull();
		expect(fs.existsSync(dataDir)).toBe(false);
	});
});
