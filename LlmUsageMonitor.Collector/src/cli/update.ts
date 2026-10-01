import fs from "node:fs";
import { moveIntoPlace } from "./install";

const RELEASES_URL = "https://api.github.com/repos/Elyspio/llm-usage-monitor/releases?per_page=50";
const TAG_PREFIX = "collector-v";

type GithubRelease = { tag_name: string; draft: boolean; prerelease: boolean; assets: { name: string; browser_download_url: string }[] };

export type CollectorRelease = { version: string; tag: string; assetUrl: string | undefined };

/** `llm-usage-win-x64.exe` or `llm-usage-linux-x64`, as built by `vp pack -F exe`. */
export function assetName(platform: NodeJS.Platform = process.platform): string {
	return platform === "win32" ? "llm-usage-win-x64.exe" : "llm-usage-linux-x64";
}

/** -1, 0 or 1, on the numeric parts of `x.y.z`. */
export function compareVersions(a: string, b: string): number {
	const parse = (value: string) =>
		value
			.split(/[.-]/)
			.slice(0, 3)
			.map((part) => Number.parseInt(part, 10) || 0);
	const [left, right] = [parse(a), parse(b)];
	for (let index = 0; index < 3; index++) {
		const difference = (left[index] ?? 0) - (right[index] ?? 0);
		if (difference !== 0) return Math.sign(difference);
	}
	return 0;
}

/** The latest published `collector-v*` release of the repository, the other releases of the repository being ignored. */
export async function findLatestRelease(): Promise<CollectorRelease | null> {
	const response = await fetch(RELEASES_URL, { headers: { Accept: "application/vnd.github+json", "User-Agent": "llm-usage-collector" } });
	if (!response.ok) throw new Error(`GitHub releases unavailable (${response.status})`);
	return pickLatestRelease((await response.json()) as GithubRelease[]);
}

export function pickLatestRelease(releases: GithubRelease[], platform: NodeJS.Platform = process.platform): CollectorRelease | null {
	const candidates = releases
		.filter((release) => !release.draft && !release.prerelease && release.tag_name.startsWith(TAG_PREFIX))
		.map((release) => ({
			version: release.tag_name.slice(TAG_PREFIX.length),
			tag: release.tag_name,
			assetUrl: release.assets.find((asset) => asset.name === assetName(platform))?.browser_download_url,
		}))
		.sort((a, b) => compareVersions(b.version, a.version));
	return candidates[0] ?? null;
}

/** Downloads the executable of <release> over <target>. */
export async function installRelease(release: CollectorRelease, target: string): Promise<void> {
	if (!release.assetUrl) throw new Error(`${release.tag} has no ${assetName()} asset`);
	const response = await fetch(release.assetUrl, { headers: { "User-Agent": "llm-usage-collector" } });
	if (!response.ok) throw new Error(`Download of ${release.tag} failed (${response.status})`);

	const temporary = `${target}.new`;
	await fs.promises.writeFile(temporary, new Uint8Array(await response.arrayBuffer()));
	await moveIntoPlace(temporary, target);
}
