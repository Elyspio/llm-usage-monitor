import { describe, expect, it } from "vite-plus/test";
import type { UsageHistory } from "@/core/apis/generated/types.gen";
import { toChart } from "@components/dashboard/HistoryCard";

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
});
