import { describe, expect, it } from "vite-plus/test";
import type { UsageWindow } from "@/core/apis/generated/types.gen";
import { errorInfo, isDegraded, providerColor, sortWindows, windowColor, windowLabel, windowTiming } from "@/core/dashboard";
import { fmtAgo, fmtDayLabel, fmtHour, fmtIn, fmtPercent, fmtSpan, fmtWhen } from "@/core/format";
import { isSameNtfyServer, serverFieldErrors, validateAlertDays, validateInterval, validateThreshold, validateTokenForServer, validateTopic } from "@/core/settings.validation";

const window = (id: string, duration: number | null, resetsAt: string | null = null): UsageWindow => ({
	id,
	usedPercent: 10,
	remainingPercent: 90,
	resetsAt,
	windowDurationMinutes: duration,
});

describe("dashboard logic", () => {
	it("computes the window start from its reset and duration", () => {
		const timing = windowTiming(window("five_hour", 300, "2026-09-14T14:00:00Z"));

		expect(timing).toEqual({ start: Date.parse("2026-09-14T09:00:00Z"), end: Date.parse("2026-09-14T14:00:00Z") });
		expect(windowTiming(window("five_hour", 300, null))).toBeNull();
	});

	it("colours a window like its provider, the Claude session in a lighter orange", () => {
		expect(windowColor("claude", "five_hour")).toBe("#f4a261");
		expect(windowColor("claude", "seven_day")).toBe(providerColor.claude);
		expect(windowColor("codex", "codex/primary")).toBe(providerColor.codex);
	});

	it("puts the trigger window first, then the others by duration", () => {
		const sorted = sortWindows([window("seven_day_opus", 10_080), window("seven_day", 10_080), window("five_hour", 300)], "five_hour");

		expect(sorted.map((item) => item.id)[0]).toBe("five_hour");
	});

	it("is degraded when the last failure is newer than the last success", () => {
		const base = { consecutiveFailures: 1, backoffUntil: null, activeAlerts: [], tokenExpiresAt: null, refreshTokenExpiresAt: null };
		const failure = { code: "TIMEOUT", message: "slow", at: "2026-09-14T12:00:00Z" };

		expect(isDegraded({ ...base, lastSuccessAt: "2026-09-14T11:00:00Z", lastFailure: failure })).toBe(true);
		expect(isDegraded({ ...base, lastSuccessAt: "2026-09-14T12:03:00Z", lastFailure: failure })).toBe(false);
		expect(isDegraded({ ...base, lastSuccessAt: null, lastFailure: null })).toBe(false);
	});

	it("labels the windows and the errors", () => {
		expect(windowLabel(window("five_hour", 300))).toBe("5 h session");
		expect(windowLabel(window("codex/primary", 10_080))).toBe("Weekly");
		expect(errorInfo("RATE_LIMITED", "claude").severity).toBe("warning");
		expect(errorInfo("AUTH_EXPIRED", "codex").action).toContain("codex login --device-auth");
	});
});

describe("formatting", () => {
	it("formats spans", () => {
		expect(fmtSpan(3 * 86_400_000 + 4 * 3_600_000)).toBe("3 d 4 h");
		expect(fmtSpan(7 * 60_000 + 5_000, true)).toBe("7 min 05 s");
		expect(fmtSpan(2 * 3_600_000 + 10 * 60_000)).toBe("2 h 10 min");
		expect(fmtSpan(12_000)).toBe("12 s");
	});

	it("labels the days around now and the hours on a 24-hour clock", () => {
		const now = new Date(2026, 8, 23, 12).getTime();

		expect(fmtHour(new Date(2026, 8, 23, 17, 52))).toBe("17:52");
		expect(fmtDayLabel(new Date(2026, 8, 23, 0, 5), now)).toBe("today");
		expect(fmtDayLabel(new Date(2026, 8, 22, 23, 55), now)).toBe("yesterday");
		expect(fmtDayLabel(new Date(2026, 8, 24, 0, 0), now)).toBe("tomorrow");
		expect(fmtWhen(new Date(2026, 8, 23, 17, 52), now)).toBe("today 17:52");
		expect(fmtWhen(new Date(2026, 8, 15, 4, 0), now)).toBe("Tue 15 Sept 04:00");
	});

	it("counts down to a future time and up from a past one", () => {
		const now = new Date(2026, 8, 23, 12).getTime();

		expect(fmtIn(now + 6 * 60_000 + 59_000, now)).toBe("in 6 min 59 s");
		expect(fmtIn(now - 3 * 3_600_000, now)).toBe("3 h 00 min ago");
		expect(fmtAgo(now - 90 * 60_000, now)).toBe("1 h 30 min ago");
		expect(fmtPercent(59.6)).toBe("60%");
	});
});

describe("settings validation", () => {
	it("keeps the ntfy token only on the same server, path and query compared with their case", () => {
		expect(isSameNtfyServer("https://ntfy.sh", " HTTPS://NTFY.sh/ ")).toBe(true);
		expect(isSameNtfyServer("https://ntfy.example.org/Team", "https://NTFY.example.org/Team/")).toBe(true);
		expect(isSameNtfyServer("https://ntfy.example.org/Team", "https://ntfy.example.org/team")).toBe(false);
		expect(isSameNtfyServer("https://ntfy.example.org/team", "https://ntfy.example.org/team?Key=1")).toBe(false);
		expect(isSameNtfyServer("https://ntfy.example.org", "http://ntfy.example.org")).toBe(false);
		expect(isSameNtfyServer("https://ntfy.example.org", "https://ntfy.example.org:8443")).toBe(false);
		expect(validateTokenForServer({ savedUrl: "https://ntfy.sh/Team", url: "https://ntfy.sh/team", tokenDefined: true, token: "", removeToken: false })).not.toBeNull();
	});

	it("applies the API bounds", () => {
		expect(validateInterval(0)).not.toBeNull();
		expect(validateInterval(60)).toBeNull();
		expect(validateInterval(15)).toBeNull();
		expect(validateInterval(45)).not.toBeNull();
		expect(validateInterval(7)).not.toBeNull();
		expect(validateThreshold(21)).not.toBeNull();
		expect(validateAlertDays(7)).toBeNull();
		expect(validateAlertDays(0)).not.toBeNull();
		expect(validateAlertDays(61)).not.toBeNull();
		expect(validateTopic("bad topic")).not.toBeNull();
		expect(validateTopic("")).toBeNull();
	});

	it("reads the first message of each field of a ValidationProblemDetails", () => {
		expect(serverFieldErrors({ errors: { url: ["bad", "worse"] } })).toEqual({ url: "bad" });
		expect(serverFieldErrors("nope")).toEqual({});
	});
});
