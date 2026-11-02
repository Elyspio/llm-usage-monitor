import { createHash } from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { afterEach, beforeEach, describe, expect, it } from "vite-plus/test";
import { systemdUnits, windowsTaskXml, withCronLine, withoutCronLine } from "./install";
import { assetName, compareVersions, findLatestRelease, findRelease, installRelease, pickLatestRelease, verifyExecutable } from "./update";

describe("schedules", () => {
	it("runs the installed executable hidden every 5 minutes on Windows, on battery too", () => {
		const xml = windowsTaskXml(String.raw`C:\Users\me & co\llm-usage.exe`, new Date(2026, 9, 1, 8, 5));

		expect(xml).toContain("<StartBoundary>2026-10-01T08:05:00</StartBoundary>");
		expect(xml).toContain("<Interval>PT5M</Interval>");
		expect(xml).toContain("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
		expect(xml).toContain(String.raw`<Arguments>--headless "C:\Users\me &amp; co\llm-usage.exe" sync</Arguments>`);
	});

	it("runs a systemd timer every 5 minutes", () => {
		const { service, timer } = systemdUnits("/home/me/.local/bin/llm-usage");

		expect(service).toContain('ExecStart="/home/me/.local/bin/llm-usage" sync');
		expect(timer).toContain("OnCalendar=*:0/5");
	});

	it("adds its crontab line once and removes only it", () => {
		const existing = "# backups\n0 3 * * * /usr/bin/backup\n\n";

		const installed = withCronLine(withCronLine(existing, "/old/llm-usage"), "/home/me/.local/bin/llm-usage");

		expect(installed).toBe('# backups\n0 3 * * * /usr/bin/backup\n*/5 * * * * "/home/me/.local/bin/llm-usage" sync >/dev/null 2>&1 # llm-usage-collector\n');
		expect(withoutCronLine(installed)).toBe("# backups\n0 3 * * * /usr/bin/backup\n");
		expect(withoutCronLine(withCronLine("", "/x"))).toBe("");
	});
});

describe("updates", () => {
	const release = (tag: string, extra: object = {}) => ({
		tag_name: tag,
		draft: false,
		prerelease: false,
		assets: [
			{ name: "llm-usage-win-x64.exe", browser_download_url: `https://x/${tag}/win` },
			{ name: "llm-usage-linux-x64", browser_download_url: `https://x/${tag}/linux` },
			{ name: "llm-usage-linux-arm64", browser_download_url: `https://x/${tag}/linux-arm64` },
		],
		...extra,
	});

	it("compares versions numerically", () => {
		expect(compareVersions("0.10.0", "0.9.3")).toBe(1);
		expect(compareVersions("1.0.0", "1.0.0")).toBe(0);
		expect(compareVersions("1.2.0", "1.10.0")).toBe(-1);
	});

	it("puts a prerelease before its release", () => {
		expect(compareVersions("0.2.0", "0.2.0-rc.1")).toBe(1);
		expect(compareVersions("0.2.0-rc.1", "0.2.0")).toBe(-1);
		expect(compareVersions("0.2.0-rc.2", "0.2.0-rc.10")).toBe(-1);
		expect(compareVersions("0.2.0-rc.1", "0.1.9")).toBe(1);
	});

	it("picks the latest published collector release and the asset of the platform", () => {
		const releases = [release("collector-v0.2.0", { draft: true }), release("v9.0.0"), release("collector-v0.10.0"), release("collector-v0.9.0")];

		expect(pickLatestRelease(releases, "linux", "x64")).toEqual({
			version: "0.10.0",
			tag: "collector-v0.10.0",
			assetUrl: "https://x/collector-v0.10.0/linux",
			checksumsUrl: undefined,
		});
		expect(pickLatestRelease(releases, "linux", "arm64")?.assetUrl).toBe("https://x/collector-v0.10.0/linux-arm64");
		expect(pickLatestRelease(releases, "win32", "x64")?.assetUrl).toBe("https://x/collector-v0.10.0/win");
		expect(pickLatestRelease([release("v1.0.0")])).toBeNull();
	});

	it("reads the release pages until one holds a collector release", async () => {
		const pages = [[release("v1.2.0"), release("v1.1.0")], [release("v1.0.0"), release("collector-v0.2.0")], [release("collector-v0.1.0")]];
		const requested: string[] = [];

		const latest = await findLatestRelease(async (url) => {
			requested.push(url);
			const page = Number(new URL(url).searchParams.get("page"));
			return Response.json(pages[page - 1] ?? []);
		});

		expect(latest?.tag).toBe("collector-v0.2.0");
		expect(requested).toHaveLength(2);
	});

	it("finds no release when the pages run out", async () => {
		const latest = await findLatestRelease(async (url) => Response.json(new URL(url).searchParams.get("page") === "1" ? [release("v1.0.0")] : []));

		expect(latest).toBeNull();
	});

	it("has an executable only for the platforms it is built for", () => {
		expect(assetName("win32", "x64")).toBe("llm-usage-win-x64.exe");
		expect(assetName("win32", "arm64")).toBe("llm-usage-win-x64.exe");
		expect(assetName("linux", "arm64")).toBe("llm-usage-linux-arm64");
		expect(assetName("darwin", "arm64")).toBeUndefined();
		expect(assetName("linux", "arm")).toBeUndefined();
	});

	it("finds the release of an exact version, a prerelease too", async () => {
		const requested: string[] = [];

		const found = await findRelease("0.3.0-rc.1", async (url) => {
			requested.push(url);
			return Response.json(release("collector-v0.3.0-rc.1", { prerelease: true }));
		});

		expect(requested).toEqual(["https://api.github.com/repos/Elyspio/llm-usage-monitor/releases/tags/collector-v0.3.0-rc.1"]);
		expect(found?.version).toBe("0.3.0-rc.1");
	});

	it("finds nothing when the version has no release", async () => {
		expect(await findRelease("9.9.9", async () => new Response(null, { status: 404 }))).toBeNull();
	});

	it("fails when GitHub cannot tell whether the release exists", async () => {
		await expect(findRelease("0.3.0", async () => new Response(null, { status: 403 }))).rejects.toThrow("GitHub releases unavailable (403)");
	});
});

describe("installRelease", () => {
	const name = assetName() ?? "llm-usage-linux-x64";
	const release = { version: "0.3.0", tag: "collector-v0.3.0", assetUrl: "https://x/asset", checksumsUrl: "https://x/sums" };
	const content = "new executable";
	const sha256 = (value: string) => createHash("sha256").update(value).digest("hex");
	const responses = (asset: string, sums: string) => async (url: string) => new Response(url === release.assetUrl ? asset : sums);

	let dir: string;
	let target: string;

	beforeEach(async () => {
		dir = await fs.promises.mkdtemp(path.join(os.tmpdir(), "llm-usage-install-"));
		target = path.join(dir, "sub", "llm-usage");
		await fs.promises.mkdir(path.dirname(target));
		await fs.promises.writeFile(target, "old executable");
	});

	afterEach(async () => {
		await fs.promises.rm(dir, { recursive: true, force: true });
	});

	const leftovers = async () => (await fs.promises.readdir(path.dirname(target))).filter((file) => file.includes(".new"));

	it("replaces the executable once checked and run", async () => {
		const verified: string[] = [];

		await installRelease(release, target, {
			fetchUrl: responses(content, `${"0".repeat(64)}  other\n${sha256(content)}  ${name}\n`),
			verify: async (file) => {
				verified.push(await fs.promises.readFile(file, "utf8"));
			},
		});

		expect(verified).toEqual([content]);
		expect(await fs.promises.readFile(target, "utf8")).toBe(content);
		expect(await leftovers()).toEqual([]);
	});

	it("creates the folder of a first install", async () => {
		const first = path.join(dir, "a", "b", "llm-usage");

		await installRelease(release, first, { fetchUrl: responses(content, `${sha256(content)} *${name}\n`), verify: async () => undefined });

		expect(await fs.promises.readFile(first, "utf8")).toBe(content);
	});

	it("keeps the installed executable when the checksum differs", async () => {
		const run = installRelease(release, target, { fetchUrl: responses("tampered", `${sha256(content)}  ${name}\n`), verify: async () => undefined });

		await expect(run).rejects.toThrow(`Checksum mismatch for ${name}`);
		expect(await fs.promises.readFile(target, "utf8")).toBe("old executable");
		expect(await leftovers()).toEqual([]);
	});

	it("refuses an asset missing from the checksums, or a release without checksums", async () => {
		const options = { fetchUrl: responses(content, `${sha256(content)}  other\n`), verify: async () => undefined };

		await expect(installRelease(release, target, options)).rejects.toThrow(`SHA256SUMS of collector-v0.3.0 has no ${name}`);
		await expect(installRelease({ ...release, checksumsUrl: undefined }, target, options)).rejects.toThrow("collector-v0.3.0 has no SHA256SUMS");
	});

	it("keeps the installed executable when the new one does not run", async () => {
		const run = installRelease(release, target, {
			fetchUrl: responses(content, `${sha256(content)}  ${name}\n`),
			verify: async () => {
				throw new Error("does not run");
			},
		});

		await expect(run).rejects.toThrow("does not run");
		expect(await fs.promises.readFile(target, "utf8")).toBe("old executable");
		expect(await leftovers()).toEqual([]);
	});
});

describe("verifyExecutable", () => {
	const result =
		(code: number, stdout = "", stderr = "") =>
		async () => ({ code, stdout, stderr });

	it("accepts an executable printing the expected version", async () => {
		await expect(verifyExecutable("/x", "0.3.0", result(0, "0.3.0\n"))).resolves.toBeUndefined();
	});

	it("rejects another version", async () => {
		await expect(verifyExecutable("/x", "0.3.0", result(0, "0.2.0\n"))).rejects.toThrow("printed 0.2.0");
	});

	it("names libatomic1 on Linux when the executable does not start", async () => {
		const run = verifyExecutable("/x", "0.3.0", result(127, "", "error while loading shared libraries: libatomic.so.1"), "linux");

		await expect(run).rejects.toThrow(/libatomic\.so\.1[\s\S]*apt install libatomic1/);
		await expect(verifyExecutable("/x", "0.3.0", result(1, "", "boom"), "win32")).rejects.not.toThrow("libatomic1");
	});
});
