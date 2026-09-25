import type { Provider, TokenCounts, TokenUsageReport, TokenUsageRow } from "@/core/apis/generated/types.gen";

export type UsageRange = "24h" | "7d" | "30d" | "90d";
export type UsageMetric = "cost" | "tokens";

export const providers: Provider[] = ["claude", "codex"];

export const totalTokens = (tokens: TokenCounts) => tokens.input + tokens.cacheRead + tokens.cacheWrite + tokens.output;

/** Cost, cache savings and tokens of a set of rows; the unpriced tokens are counted in the tokens, never in the cost. */
export type Amounts = { cost: number; savings: number; tokens: number; unpricedTokens: number; priced: boolean };

const emptyAmounts = (): Amounts => ({ cost: 0, savings: 0, tokens: 0, unpricedTokens: 0, priced: false });

function add(amounts: Amounts, row: TokenUsageRow): Amounts {
	return {
		cost: amounts.cost + (row.costUsd ?? 0),
		savings: amounts.savings + (row.cacheSavingsUsd ?? 0),
		tokens: amounts.tokens + totalTokens(row.tokens),
		unpricedTokens: amounts.unpricedTokens + row.unpricedTokens,
		priced: amounts.priced || row.costUsd != null,
	};
}

const value = (amounts: Amounts, metric: UsageMetric) => (metric === "cost" ? amounts.cost : amounts.tokens);

export type ProviderSummary = Amounts & { provider: Provider; share: number };
export type ModelLine = Amounts & { provider: Provider; model: string; share: number };
export type DayLine = Amounts & { start: number; share: number };
export type ChartPoint = { start: number } & Record<Provider, number>;

export type UsageSummary = {
	total: Amounts;
	/** Share of the tokens left out of the cost because their model has no price, from 0 to 1. */
	unpricedShare: number;
	providers: ProviderSummary[];
	tokens: TokenCounts;
	chart: ChartPoint[];
	models: ModelLine[];
	days: DayLine[];
};

/** The local day of a date, as a stable key. */
const dayKey = (time: number) => {
	const date = new Date(time);
	return `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`;
};

/** Every step of the period, so the chart shows the empty hours and days as zero. */
export function steps(report: Pick<TokenUsageReport, "from" | "to" | "step">): number[] {
	const from = Date.parse(report.from);
	const to = Date.parse(report.to);
	const result: number[] = [];
	if (report.step === "hour") {
		for (let time = from; time <= to; time += 3_600_000) result.push(time);
		return result;
	}
	const first = new Date(from);
	for (let day = 0; ; day++) {
		const time = new Date(first.getFullYear(), first.getMonth(), first.getDate() + day).getTime();
		if (time > to) return result;
		result.push(time);
	}
}

export function summarize(report: TokenUsageReport, metric: UsageMetric): UsageSummary {
	const stepKey = (time: number) => (report.step === "hour" ? String(time) : dayKey(time));
	const total = report.rows.reduce(add, emptyAmounts());
	const share = (amounts: Amounts) => {
		const whole = value(total, metric);
		return whole > 0 ? value(amounts, metric) / whole : 0;
	};

	const byProvider = new Map<Provider, Amounts>(providers.map((provider) => [provider, emptyAmounts()]));
	const byModel = new Map<string, { provider: Provider; model: string; amounts: Amounts }>();
	const byStep = new Map<string, { start: number; amounts: Amounts; perProvider: Record<Provider, number> }>();
	for (const start of steps(report)) byStep.set(stepKey(start), { start, amounts: emptyAmounts(), perProvider: { claude: 0, codex: 0 } });

	const tokens: TokenCounts = { input: 0, cacheRead: 0, cacheWrite: 0, output: 0 };
	for (const row of report.rows) {
		byProvider.set(row.provider, add(byProvider.get(row.provider)!, row));

		const modelKey = `${row.provider}:${row.model}`;
		const model = byModel.get(modelKey) ?? { provider: row.provider, model: row.model, amounts: emptyAmounts() };
		byModel.set(modelKey, { ...model, amounts: add(model.amounts, row) });

		const start = Date.parse(row.start);
		const step = byStep.get(stepKey(start)) ?? { start, amounts: emptyAmounts(), perProvider: { claude: 0, codex: 0 } };
		step.amounts = add(step.amounts, row);
		step.perProvider[row.provider] += metric === "cost" ? (row.costUsd ?? 0) : totalTokens(row.tokens);
		byStep.set(stepKey(start), step);

		tokens.input += row.tokens.input;
		tokens.cacheRead += row.tokens.cacheRead;
		tokens.cacheWrite += row.tokens.cacheWrite;
		tokens.output += row.tokens.output;
	}

	const orderedSteps = [...byStep.values()].sort((a, b) => a.start - b.start);
	const models = [...byModel.values()]
		.map(({ provider, model, amounts }) => ({ ...amounts, provider, model, share: share(amounts) }))
		// Priced models first by cost, then the unpriced ones by tokens.
		.sort((a, b) => Number(b.priced) - Number(a.priced) || value(b, metric) - value(a, metric) || b.tokens - a.tokens);

	return {
		total,
		unpricedShare: total.tokens > 0 ? total.unpricedTokens / total.tokens : 0,
		providers: providers.map((provider) => ({ ...byProvider.get(provider)!, provider, share: share(byProvider.get(provider)!) })),
		tokens,
		chart: orderedSteps.map((step) => ({ start: step.start, ...step.perProvider })),
		models,
		days: orderedSteps
			.filter((step) => step.amounts.tokens > 0)
			.reverse()
			.map((step) => ({ ...step.amounts, start: step.start, share: share(step.amounts) })),
	};
}

const usdFormat = new Intl.NumberFormat("fr-FR", { style: "currency", currency: "USD", currencyDisplay: "narrowSymbol" });
const compactFormat = new Intl.NumberFormat("fr-FR", { notation: "compact", maximumFractionDigits: 2 });
const percentFormat = new Intl.NumberFormat("fr-FR", { style: "percent", minimumFractionDigits: 1, maximumFractionDigits: 1 });

/** 283,27 $ */
export const fmtUsd = (value: number) => usdFormat.format(value);
/** 598 M · 1,84 M · 313 k */
export const fmtTokens = (value: number) => compactFormat.format(value);
/** 49,9 % */
export const fmtShare = (value: number) => percentFormat.format(value);

export const fmtMetric = (value: number, metric: UsageMetric) => (metric === "cost" ? fmtUsd(value) : fmtTokens(value));

export const rangeLabel: Record<UsageRange, string> = { "24h": "24 h", "7d": "7 j", "30d": "30 j", "90d": "90 j" };

/** The IANA zone of the browser, so the days of the report are the user's days. */
export const browserTimeZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone;
