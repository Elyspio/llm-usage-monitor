import { describe, expect, it } from "vite-plus/test";
import type { UsageWindow } from "@/core/apis/generated/types.gen";
import { errorInfo, isDegraded, providerColor, sortWindows, windowColor, windowLabel, windowTiming } from "@/core/dashboard";
import { fmtDuration, fmtSpan } from "@/core/format";
import { serverFieldErrors, validateInterval, validateThreshold, validateTopic } from "@/core/settings.validation";

const window = (id: string, duration: number | null, resetsAt: string | null = null): UsageWindow => ({
	id,
	usedPercent: 10,
	remainingPercent: 90,
	resetsAt,
	windowDurationMinutes: duration,
});

describe("dashboard logic", () => {
	it("computes the window start and elapsed share from its reset and duration", () => {
		const now = Date.parse("2026-09-14T12:00:00Z");

		const timing = windowTiming(window("five_hour", 300, "2026-09-14T14:00:00Z"), now);

		expect(timing).toEqual({ start: Date.parse("2026-09-14T09:00:00Z"), end: Date.parse("2026-09-14T14:00:00Z"), elapsedPercent: 60 });
		expect(windowTiming(window("five_hour", 300, null), now)).toBeNull();
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

	it("labels the windows and the errors in French", () => {
		expect(windowLabel(window("five_hour", 300))).toBe("Session 5 h");
		expect(windowLabel(window("codex/primary", 10_080))).toBe("Hebdo");
		expect(errorInfo("RATE_LIMITED", "claude").severity).toBe("warning");
		expect(errorInfo("AUTH_EXPIRED", "codex").action).toContain("codex login --device-auth");
	});
});

describe("formatting", () => {
	it("formats spans and durations", () => {
		expect(fmtSpan(3 * 86_400_000 + 4 * 3_600_000)).toBe("3 j 4 h");
		expect(fmtSpan(7 * 60_000 + 5_000, true)).toBe("7 min 05 s");
		expect(fmtDuration(10_080)).toBe("7 j");
		expect(fmtDuration(300)).toBe("5 h");
		expect(fmtDuration(null)).toBe("durée inconnue");
	});
});

describe("settings validation", () => {
	it("applies the API bounds", () => {
		expect(validateInterval(0)).not.toBeNull();
		expect(validateInterval(60)).toBeNull();
		expect(validateThreshold(21)).not.toBeNull();
		expect(validateTopic("bad topic")).not.toBeNull();
		expect(validateTopic("")).toBeNull();
	});

	it("reads the first message of each field of a ValidationProblemDetails", () => {
		expect(serverFieldErrors({ errors: { url: ["bad", "worse"] } })).toEqual({ url: "bad" });
		expect(serverFieldErrors("nope")).toEqual({});
	});
});
