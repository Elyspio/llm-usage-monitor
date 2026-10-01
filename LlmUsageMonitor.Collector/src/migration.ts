import fs from "node:fs";
import path from "node:path";
import { legacyElytoolsDirs } from "./paths";

const MIGRATED_FILES = ["machine-id", "state.json"];

/**
 * Moves the machine id and the state of an older collector folder into the shared data folder, once: the workstation
 * keeps its id in the monitor and its logs are not read again. Does nothing when the data folder already has an id.
 * Returns the folder migrated from, or `null`.
 */
export async function migrateLegacyData(dataDir: string, legacyDirs: string[] = legacyElytoolsDirs()): Promise<string | null> {
	if (fs.existsSync(path.join(dataDir, "machine-id"))) return null;

	const legacyDir = legacyDirs.find((dir) => path.resolve(dir) !== path.resolve(dataDir) && fs.existsSync(path.join(dir, "machine-id")));
	if (!legacyDir) return null;

	await fs.promises.mkdir(dataDir, { recursive: true });
	for (const name of MIGRATED_FILES) {
		const source = path.join(legacyDir, name);
		if (!fs.existsSync(source)) continue;
		try {
			await fs.promises.copyFile(source, path.join(dataDir, name), fs.constants.COPYFILE_EXCL);
		} catch (error) {
			// Another collector migrated at the same time: its copy is the same file.
			if ((error as NodeJS.ErrnoException).code !== "EEXIST") throw error;
		}
	}

	for (const name of [...MIGRATED_FILES, "state.json.tmp"]) await fs.promises.rm(path.join(legacyDir, name), { force: true });
	await fs.promises.rmdir(legacyDir).catch(() => undefined);
	return legacyDir;
}
