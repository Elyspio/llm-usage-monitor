import fs from "node:fs";
import { moveIntoPlace } from "./install";

const RELEASES_URL = "https://api.github.com/repos/Elyspio/llm-usage-monitor/releases?per_page=100";
const TAG_PREFIX = "collector-v";
/** The server releases (`v*`) share the repository: pages are read until one holds a collector release. */
const MAX_RELEASE_PAGES = 10;

type GithubRelease = { tag_name: string; draft: boolean; prerelease: boolean; assets: { name: string; browser_download_url: string }[] };

export type CollectorRelease = { version: string; tag: string; assetUrl: string | undefined };

/** `llm-usage-win-x64.exe`, `llm-usage-linux-x64` or `llm-usage-linux-arm64`, as built by `vp pack -F exe`. */
export function assetName(platform: NodeJS.Platform = process.platform, arch: NodeJS.Architecture = process.arch): string {
	if (platform === "win32") return "llm-usage-win-x64.exe";
	return arch === "arm64" ? "llm-usage-linux-arm64" : "llm-usage-linux-x64";
}

/** -1, 0 or 1, on the numeric parts of `x.y.z`; a prerelease (`x.y.z-rc.1`) comes before its release. */
export function compareVersions(a: string, b: string): number {
	const parse = (value: string) => {
		const separator = value.indexOf("-");
		const core = separator < 0 ? value : value.slice(0, separator);
		const numbers = core
			.split(".")
			.slice(0, 3)
			.map((part) => Number.parseInt(part, 10) || 0);
		return { numbers, prerelease: separator < 0 ? "" : value.slice(separator + 1) };
	};
	const [left, right] = [parse(a), parse(b)];
	for (let index = 0; index < 3; index++) {
		const difference = (left.numbers[index] ?? 0) - (right.numbers[index] ?? 0);
		if (difference !== 0) return Math.sign(difference);
	}
	if (left.prerelease === right.prerelease) return 0;
	if (!left.prerelease) return 1;
	if (!right.prerelease) return -1;
	return Math.sign(left.prerelease.localeCompare(right.prerelease, "en", { numeric: true }));
}

/** The latest published `collector-v*` release of the repository, the other releases of the repository being ignored. */
export async function findLatestRelease(fetchReleases: (url: string) => Promise<Response> = fetchGithub): Promise<CollectorRelease | null> {
	for (let page = 1; page <= MAX_RELEASE_PAGES; page++) {
		const response = await fetchReleases(`${RELEASES_URL}&page=${page}`);
		if (!response.ok) throw new Error(`GitHub releases unavailable (${response.status})`);
		const releases = (await response.json()) as GithubRelease[];
		const latest = pickLatestRelease(releases);
		if (latest || releases.length === 0) return latest;
	}
	return null;
}

function fetchGithub(url: string): Promise<Response> {
	return fetch(url, { headers: { Accept: "application/vnd.github+json", "User-Agent": "llm-usage-collector" } });
}

export function pickLatestRelease(releases: GithubRelease[], platform: NodeJS.Platform = process.platform, arch: NodeJS.Architecture = process.arch): CollectorRelease | null {
	const candidates = releases
		.filter((release) => !release.draft && !release.prerelease && release.tag_name.startsWith(TAG_PREFIX))
		.map((release) => ({
			version: release.tag_name.slice(TAG_PREFIX.length),
			tag: release.tag_name,
			assetUrl: release.assets.find((asset) => asset.name === assetName(platform, arch))?.browser_download_url,
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
