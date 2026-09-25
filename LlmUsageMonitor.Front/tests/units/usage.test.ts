import { describe, expect, it } from "vite-plus/test";
import type { TokenUsageReport, TokenUsageRow } from "@/core/apis/generated/types.gen";
import { steps, summarize } from "@/core/usage";

const day = (date: number) => new Date(2026, 8, date).toISOString();

const row = (start: string, provider: TokenUsageRow["provider"], model: string, input: number, costUsd: number | null): TokenUsageRow => ({
	start,
	provider,
	model,
	tokens: { input, cacheRead: input * 10, cacheWrite: 0, output: 0 },
	costUsd,
	cacheSavingsUsd: costUsd == null ? null : costUsd / 2,
	unpricedTokens: costUsd == null ? input * 11 : 0,
});

const report: TokenUsageReport = {
	from: day(19),
	to: new Date(2026, 8, 25, 14, 30).toISOString(),
	step: "day",
	timeZone: "Europe/Paris",
	machines: [],
	rows: [
		row(day(19), "claude", "claude-opus-5", 100, 30),
		row(day(19), "codex", "gpt-6-sol", 100, 10),
		row(day(22), "claude", "claude-opus-5", 100, 50),
		row(day(22), "claude", "<synthetic>", 100, null),
	],
};

describe("usage report", () => {
	it("lists every local day of the period, today included", () => {
		expect(steps(report)).toHaveLength(7);
		expect(steps({ from: new Date(2026, 8, 25, 13).toISOString(), to: new Date(2026, 8, 26, 12, 30).toISOString(), step: "hour" })).toHaveLength(24);
	});

	it("totals the cost by provider and leaves the unpriced tokens out of the cost", () => {
		const summary = summarize(report, "cost");

		expect(summary.total.cost).toBe(90);
		expect(summary.total.savings).toBe(45);
		expect(summary.unpricedShare).toBeCloseTo(0.25);
		expect(summary.providers.map((provider) => [provider.provider, provider.cost, provider.share])).toEqual([
			["claude", 80, 80 / 90],
			["codex", 10, 10 / 90],
		]);
	});

	it("fills the empty days of the chart with zero", () => {
		const chart = summarize(report, "cost").chart;

		expect(chart.map((point) => point.claude)).toEqual([30, 0, 0, 50, 0, 0, 0]);
		expect(chart.map((point) => point.codex)).toEqual([10, 0, 0, 0, 0, 0, 0]);
	});

	it("orders the models by cost, the unpriced ones last, and the days from the newest", () => {
		const summary = summarize(report, "cost");

		expect(summary.models.map((model) => model.model)).toEqual(["claude-opus-5", "gpt-6-sol", "<synthetic>"]);
		expect(summary.models[2].priced).toBe(false);
		expect(summary.days.map((line) => new Date(line.start).getDate())).toEqual([22, 19]);
	});

	it("measures the chart in tokens when asked", () => {
		expect(summarize(report, "tokens").chart[0]).toMatchObject({ claude: 1100, codex: 1100 });
	});
});
