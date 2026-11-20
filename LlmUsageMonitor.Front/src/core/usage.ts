import type { Provider, TokenCostByType, TokenCounts, TokenUsageReport, TokenUsageRow } from "@/core/apis/generated/types.gen";
import { providers } from "@/core/dashboard";
import { locale } from "@/core/format";

export type UsageRange = "24h" | "7d" | "30d" | "90d" | "all";
export type UsageMetric = "cost" | "tokens";

/** The token types, in the order of a request: what is read, what the cache serves and stores, what is written. */
export const tokenTypes = ["input", "cacheRead", "cacheWrite", "output"] as const satisfies readonly (keyof TokenCounts)[];
export type TokenType = (typeof tokenTypes)[number];

export const tokenTypeLabel: Record<TokenType, string> = { input: "Input", cacheRead: "Cache read", cacheWrite: "Cache write", output: "Output" };

export const totalTokens = (tokens: TokenCounts) => tokens.input + tokens.cacheRead + tokens.cacheWrite + tokens.output;

/**
 * Cost, cache savings and tokens of a set of rows, in total and by token type; the unpriced tokens are counted in the
 * tokens, never in the cost.
 */
export type Amounts = {
	cost: number;
	savings: number;
	tokens: number;
	unpricedTokens: number;
	priced: boolean;
	/** The cost of the priced rows by token type; it adds up to `cost`. */
	costByType: TokenCostByType;
	/** The tokens by type; they add up to `tokens`. */
	tokensByType: TokenCounts;
};

const emptyAmounts = (): Amounts => ({
	cost: 0,
	savings: 0,
	tokens: 0,
	unpricedTokens: 0,
	priced: false,
	costByType: { input: 0, cacheRead: 0, cacheWrite: 0, output: 0 },
	tokensByType: { input: 0, cacheRead: 0, cacheWrite: 0, output: 0 },
});

const addByType = (sum: Record<TokenType, number>, values: Record<TokenType, number> | null): Record<TokenType, number> =>
	values
		? { input: sum.input + values.input, cacheRead: sum.cacheRead + values.cacheRead, cacheWrite: sum.cacheWrite + values.cacheWrite, output: sum.output + values.output }
		: sum;

function add(amounts: Amounts, row: TokenUsageRow): Amounts {
	return {
		cost: amounts.cost + (row.costUsd ?? 0),
		savings: amounts.savings + (row.cacheSavingsUsd ?? 0),
		tokens: amounts.tokens + totalTokens(row.tokens),
		unpricedTokens: amounts.unpricedTokens + row.unpricedTokens,
		priced: amounts.priced || row.costUsd != null,
		costByType: addByType(amounts.costByType, row.costByType),
		tokensByType: addByType(amounts.tokensByType, row.tokens),
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
		chart: orderedSteps.map((step) => ({ start: step.start, ...step.perProvider })),
		models,
		days: orderedSteps
			.filter((step) => step.amounts.tokens > 0)
			.reverse()
			.map((step) => ({ ...step.amounts, start: step.start, share: share(step.amounts) })),
	};
}

/** A column of the usage table: the line label (model or day), an amount, or a token type in the chosen metric. */
export type LineSortKey = "label" | "cost" | "share" | "tokens" | TokenType;
export type SortDirection = "asc" | "desc";
export type LineSort = { key: LineSortKey; direction: SortDirection };

/** The direction of a first click: the label reads from A or the oldest day, an amount from the largest. */
export const firstDirection = (key: LineSortKey): SortDirection => (key === "label" ? "asc" : "desc");

/** The value a line sorts on; null where the table shows "—", which stays last in both directions. */
function sortValue(line: ModelLine | DayLine, key: LineSortKey, metric: UsageMetric): string | number | null {
	switch (key) {
		case "label":
			return "model" in line ? line.model : line.start;
		case "cost":
			return line.priced ? line.cost : null;
		case "share":
			return line.priced || metric === "tokens" ? line.share : null;
		case "tokens":
			return line.tokens;
		default:
			return metric === "cost" ? (line.priced ? line.costByType[key] : null) : line.tokensByType[key];
	}
}

/** The lines sorted on a column; equal lines keep their order. */
export function sortLines<T extends ModelLine | DayLine>(lines: T[], { key, direction }: LineSort, metric: UsageMetric): T[] {
	const sign = direction === "asc" ? 1 : -1;
	return [...lines].sort((a, b) => {
		const x = sortValue(a, key, metric);
		const y = sortValue(b, key, metric);
		if (x == null || y == null) return x == null ? (y == null ? 0 : 1) : -1;
		return sign * (typeof x === "string" ? x.localeCompare(y as string, locale) : x - (y as number));
	});
}

const usdFormat = new Intl.NumberFormat(locale, { style: "currency", currency: "USD", currencyDisplay: "narrowSymbol" });
const compactFormat = new Intl.NumberFormat(locale, { notation: "compact", maximumFractionDigits: 2 });
const percentFormat = new Intl.NumberFormat(locale, { style: "percent", minimumFractionDigits: 1, maximumFractionDigits: 1 });

/** $283.27 */
export const fmtUsd = (value: number) => usdFormat.format(value);
/** 598M · 1.84M · 313K */
export const fmtTokens = (value: number) => compactFormat.format(value);
/** 49.9% */
export const fmtShare = (value: number) => percentFormat.format(value);

export const fmtMetric = (value: number, metric: UsageMetric) => (metric === "cost" ? fmtUsd(value) : fmtTokens(value));

export const rangeLabel: Record<UsageRange, string> = { "24h": "24 h", "7d": "7 d", "30d": "30 d", "90d": "90 d", all: "All" };

/** The IANA zone of the browser, so the days of the report are the user's days. */
export const browserTimeZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone;
