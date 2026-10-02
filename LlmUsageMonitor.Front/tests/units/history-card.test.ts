import { describe, expect, it } from "vite-plus/test";
import type { UsageHistory } from "@/core/apis/generated/types.gen";
import { describeChart, toChart } from "@components/dashboard/HistoryCard";
import { describeUsageChart } from "@components/usage/UsageChart";

describe("history chart", () => {
	it("carries the last known value through missing readings and to the range end", () => {
		const history: UsageHistory = {
			from: "2026-09-17T10:00:00Z",
			to: "2026-09-17T14:00:00Z",
			triggerRuns: [],
			series: [
				{
					provider: "claude",
					windowId: "five_hour",
					points: [
						{ fetchedAt: "2026-09-17T11:00:00Z", usedPercent: 10, resetsAt: null },
						{ fetchedAt: "2026-09-17T13:00:00Z", usedPercent: 20, resetsAt: null },
					],
				},
				{
					provider: "codex",
					windowId: "seven_day",
					points: [{ fetchedAt: "2026-09-17T12:00:00Z", usedPercent: 30, resetsAt: null }],
				},
			],
		};

		const chart = toChart(history, {});

		expect(chart.times.map((time) => time.toISOString())).toEqual([
			"2026-09-17T11:00:00.000Z",
			"2026-09-17T12:00:00.000Z",
			"2026-09-17T13:00:00.000Z",
			"2026-09-17T14:00:00.000Z",
		]);
		expect(chart.series[0].data).toEqual([90, 90, 80, 80]);
		expect(chart.series[0].color).toBe("#f4a261");
		expect(chart.series[1].data).toEqual([null, 70, 70, 70]);
	});

	it("describes each series in text for screen readers", () => {
		const series = [
			{ key: "claude:five_hour", label: "Claude · 5 h session", color: "", data: [90, 40, 80] },
			{ key: "codex:seven_day", label: "Codex · Weekly", color: "", data: [null, null] },
		];

		expect(describeChart(series)).toEqual(["Claude · 5 h session: 80% remaining at the end of the period, lowest 40%.", "Codex · Weekly: no reading over the period."]);
	});

	it("describes the usage chart with the total and the busiest step of each provider", () => {
		const points = [
			{ start: new Date(2026, 8, 21).getTime(), claude: 10, codex: 0 },
			{ start: new Date(2026, 8, 22).getTime(), claude: 30, codex: 0 },
		];

		expect(describeUsageChart(points, "day", "cost")).toEqual(["Claude: $40.00 over the period, at most $30.00 (22 Sept).", "Codex: $0.00 over the period."]);
	});
});
