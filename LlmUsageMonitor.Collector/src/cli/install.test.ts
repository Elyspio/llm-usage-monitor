import { describe, expect, it } from "vite-plus/test";
import { systemdUnits, windowsTaskXml, withCronLine, withoutCronLine } from "./install";
import { compareVersions, pickLatestRelease } from "./update";

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
		],
		...extra,
	});

	it("compares versions numerically", () => {
		expect(compareVersions("0.10.0", "0.9.3")).toBe(1);
		expect(compareVersions("1.0.0", "1.0.0")).toBe(0);
		expect(compareVersions("1.2.0", "1.10.0")).toBe(-1);
	});

	it("picks the latest published collector release and the asset of the platform", () => {
		const releases = [release("collector-v0.2.0", { draft: true }), release("v9.0.0"), release("collector-v0.10.0"), release("collector-v0.9.0")];

		expect(pickLatestRelease(releases, "linux")).toEqual({ version: "0.10.0", tag: "collector-v0.10.0", assetUrl: "https://x/collector-v0.10.0/linux" });
		expect(pickLatestRelease(releases, "win32")?.assetUrl).toBe("https://x/collector-v0.10.0/win");
		expect(pickLatestRelease([release("v1.0.0")])).toBeNull();
	});
});
