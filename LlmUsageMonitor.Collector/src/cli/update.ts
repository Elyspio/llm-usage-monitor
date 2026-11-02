import { createHash } from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { run, type RunResult } from "./process";

const RELEASES_API = "https://api.github.com/repos/Elyspio/llm-usage-monitor/releases";
const RELEASES_URL = `${RELEASES_API}?per_page=100`;
const CHECKSUMS_ASSET = "SHA256SUMS";
const TAG_PREFIX = "collector-v";
/** The server releases (`v*`) share the repository: pages are read until one holds a collector release. */
const MAX_RELEASE_PAGES = 10;

type GithubRelease = { tag_name: string; draft: boolean; prerelease: boolean; assets: { name: string; browser_download_url: string }[] };

export type CollectorRelease = { version: string; tag: string; assetUrl: string | undefined; checksumsUrl: string | undefined };

/**
 * `llm-usage-win-x64.exe`, `llm-usage-linux-x64` or `llm-usage-linux-arm64`, as built by `vp pack -F exe`; undefined on
 * the platforms without an executable. Windows arm64 runs the x64 one through emulation.
 */
export function assetName(platform: NodeJS.Platform = process.platform, arch: NodeJS.Architecture = process.arch): string | undefined {
	if (arch !== "x64" && arch !== "arm64") return undefined;
	if (platform === "win32") return "llm-usage-win-x64.exe";
	if (platform === "linux") return `llm-usage-linux-${arch}`;
	return undefined;
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

/** The release of exactly <version>, a prerelease too; null when it does not exist. */
export async function findRelease(version: string, fetchRelease: (url: string) => Promise<Response> = fetchGithub): Promise<CollectorRelease | null> {
	const response = await fetchRelease(`${RELEASES_API}/tags/${TAG_PREFIX}${version}`);
	if (response.status === 404) return null;
	if (!response.ok) throw new Error(`GitHub releases unavailable (${response.status})`);
	return toCollectorRelease((await response.json()) as GithubRelease);
}

function toCollectorRelease(release: GithubRelease, platform: NodeJS.Platform = process.platform, arch: NodeJS.Architecture = process.arch): CollectorRelease {
	const url = (name: string | undefined) => release.assets.find((asset) => asset.name === name)?.browser_download_url;
	return {
		version: release.tag_name.slice(TAG_PREFIX.length),
		tag: release.tag_name,
		assetUrl: url(assetName(platform, arch)),
		checksumsUrl: url(CHECKSUMS_ASSET),
	};
}

function fetchGithub(url: string): Promise<Response> {
	return fetch(url, { headers: { Accept: "application/vnd.github+json", "User-Agent": "llm-usage-collector" } });
}

export function pickLatestRelease(releases: GithubRelease[], platform: NodeJS.Platform = process.platform, arch: NodeJS.Architecture = process.arch): CollectorRelease | null {
	const candidates = releases
		.filter((release) => !release.draft && !release.prerelease && release.tag_name.startsWith(TAG_PREFIX))
		.map((release) => toCollectorRelease(release, platform, arch))
		.sort((a, b) => compareVersions(b.version, a.version));
	return candidates[0] ?? null;
}

type InstallReleaseOptions = {
	fetchUrl?: (url: string) => Promise<Response>;
	verify?: (file: string) => Promise<void>;
};

/** Downloads the executable of <release> over <target>, once its checksum is verified and it has run. On failure <target> is left untouched. */
export async function installRelease(release: CollectorRelease, target: string, options: InstallReleaseOptions = {}): Promise<void> {
	const { fetchUrl = download, verify = (file) => verifyExecutable(file, release.version) } = options;
	const name = assetName() ?? `llm-usage-${process.platform}-${process.arch}`;
	if (!release.assetUrl) throw new Error(`${release.tag} has no ${name} asset`);
	if (!release.checksumsUrl) throw new Error(`${release.tag} has no ${CHECKSUMS_ASSET}`);

	const content = new Uint8Array(await (await fetchOk(fetchUrl, release.assetUrl, release.tag)).arrayBuffer());
	const expected = findChecksum(await (await fetchOk(fetchUrl, release.checksumsUrl, release.tag)).text(), name);
	if (!expected) throw new Error(`${CHECKSUMS_ASSET} of ${release.tag} has no ${name}`);
	if (createHash("sha256").update(content).digest("hex") !== expected) throw new Error(`Checksum mismatch for ${name} of ${release.tag}`);

	await fs.promises.mkdir(path.dirname(target), { recursive: true });
	const temporary = `${target}.new`;
	try {
		await fs.promises.writeFile(temporary, content, { mode: 0o755 });
		await verify(temporary);
		await moveIntoPlace(temporary, target);
	} finally {
		await fs.promises.rm(temporary, { force: true });
	}
}

/** Runs <file>, which must print <version>: catches an executable this machine cannot start before anything is scheduled. */
export async function verifyExecutable(
	file: string,
	version: string,
	runFile: (command: string, args: string[]) => Promise<RunResult> = run,
	platform: NodeJS.Platform = process.platform
): Promise<void> {
	const result = await runFile(file, ["--version"]);
	if (result.code !== 0) {
		const hint = platform === "linux" ? " It may need libatomic1 (apt install libatomic1)." : "";
		throw new Error(`The downloaded executable does not start (${result.code}): ${(result.stderr || result.stdout).trim()}.${hint}`);
	}
	if (!result.stdout.includes(version)) throw new Error(`The downloaded executable printed ${result.stdout.trim()} instead of ${version}`);
}

/** Renames <temporary> over <target>. Windows cannot overwrite a running executable but can rename it: `<target>.old` is removed on the next run. */
export async function moveIntoPlace(temporary: string, target: string): Promise<void> {
	if (process.platform === "win32") {
		if (fs.existsSync(target)) {
			await fs.promises.rm(`${target}.old`, { force: true });
			await fs.promises.rename(target, `${target}.old`);
		}
	} else {
		await fs.promises.chmod(temporary, 0o755);
	}
	await fs.promises.rename(temporary, target);
}

function download(url: string): Promise<Response> {
	return fetch(url, { headers: { "User-Agent": "llm-usage-collector" } });
}

async function fetchOk(fetchUrl: (url: string) => Promise<Response>, url: string, tag: string): Promise<Response> {
	const response = await fetchUrl(url);
	if (!response.ok) throw new Error(`Download of ${tag} failed (${response.status})`);
	return response;
}

/** The hash of <name> in the output of `sha256sum` (`<hash>  <name>`, or `<hash> *<name>` in binary mode). */
function findChecksum(checksums: string, name: string): string | undefined {
	for (const line of checksums.split("\n")) {
		const match = /^([0-9a-f]{64}) [ *](.+?)\s*$/i.exec(line);
		if (match?.[2] === name) return match[1]?.toLowerCase();
	}
	return undefined;
}
