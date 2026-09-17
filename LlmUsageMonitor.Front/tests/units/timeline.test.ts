import { describe, expect, it } from "vite-plus/test";
import { toTimeline } from "@/core/timeline";
import { claude } from "./fixtures";

describe("shared timeline", () => {
	it("preserves the duration ratio between a session and a week", () => {
		const now = Date.now();
		const chart = toTimeline([claude], now);
		const session = chart.rows.find((row) => row.window.id === "five_hour")!.timing!;
		const week = chart.rows.find((row) => row.window.id === "seven_day")!.timing!;
		const width = (range: typeof session) => chart.position(range.end) - chart.position(range.start);
		expect(width(week) / width(session)).toBeCloseTo(168 / 5);
		expect(chart.start).toBeLessThanOrEqual(week.start);
		expect(chart.end).toBeGreaterThan(week.end);
		expect(chart.position(now)).toBeGreaterThan(0);
		expect(chart.position(now)).toBeLessThan(100);
	});
	it("keeps an empty or undated timeline finite without inventing a window", () => {
		const now = Date.now();
		const empty = toTimeline([], now);
		expect(empty.rows).toEqual([]);
		expect(Number.isFinite(empty.position(now))).toBe(true);
		const chart = toTimeline(
			[{ ...claude, lastReading: { fetchedAt: new Date(now).toISOString(), windows: [{ id: "unknown", usedPercent: 0, resetsAt: null, windowDurationMinutes: null }] } }],
			now
		);
		expect(chart.rows[0].timing).toBeNull();
	});
	it("includes now after all windows have expired", () => {
		const now = Date.now() + 30 * 86400000;
		const chart = toTimeline([claude], now);
		expect(chart.end).toBeGreaterThan(now);
		expect(chart.ticks.length).toBeLessThanOrEqual(10);
	});
});
