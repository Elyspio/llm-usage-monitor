import { Chip, CircularProgress, Paper, Stack, ToggleButton, ToggleButtonGroup, Typography, useTheme } from "@mui/material";
import { ChartsReferenceLine } from "@mui/x-charts/ChartsReferenceLine";
import { LineChart } from "@mui/x-charts/LineChart";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { getHistoryOptions } from "@/core/apis/generated/@tanstack/react-query.gen";
import type { UsageHistory } from "@/core/apis/generated/types.gen";
import { providerColor, providerLabel, windowColor, windowLabel } from "@/core/dashboard";
import { fmtDayLabel, fmtHour } from "@/core/format";
import { QueryError } from "@components/QueryError";
import { useNow } from "@hooks/useNow";

type Range = "24h" | "7d";

const yAxis = [{ min: 0, max: 100, valueFormatter: (value: number) => `${value}%` }];

type Series = { key: string; label: string; color: string; data: (number | null)[] };

/** Series share a merged time axis and keep their last valid value until the next reading. */
export function toChart(history: UsageHistory, durations: Record<string, number | null>): { times: Date[]; series: Series[] } {
	const rangeEnd = Date.parse(history.to);
	const times = [
		...new Set([...history.series.flatMap((series) => series.points.map((point) => Date.parse(point.fetchedAt))), ...(Number.isFinite(rangeEnd) ? [rangeEnd] : [])]),
	].sort((a, b) => a - b);
	const series = history.series.map((item) => {
		const values = new Map(item.points.map((point) => [Date.parse(point.fetchedAt), Math.max(0, 100 - point.usedPercent)]));
		let lastValue: number | null = null;
		const data = times.map((time) => {
			lastValue = values.get(time) ?? lastValue;
			return lastValue;
		});
		const key = `${item.provider}:${item.windowId}`;
		return {
			key,
			label: `${providerLabel[item.provider]} · ${windowLabel({ id: item.windowId, windowDurationMinutes: durations[key] ?? null })}`,
			color: windowColor(item.provider, item.windowId),
			data,
		};
	});
	return { times: times.map((time) => new Date(time)), series };
}

/** Usage history over 24 h or 7 days. Its own clock ticks every minute: the chart is never redrawn for a countdown. */
export const HistoryCard = ({ durations }: { durations: Record<string, number | null> }) => {
	const theme = useTheme();
	const now = useNow(60_000);
	const [range, setRange] = useState<Range>("24h");
	const [hidden, setHidden] = useState<string[]>([]);
	// The previous range stays shown, dimmed, while the new one loads.
	const history = useQuery({ ...getHistoryOptions({ query: { range } }), refetchInterval: 60_000, placeholderData: keepPreviousData });
	const { data, isPending } = history;
	const chart = useMemo(() => (data ? toChart(data, durations) : null), [data, durations]);
	const series = useMemo(
		() =>
			(chart?.series ?? [])
				.filter((item) => !hidden.includes(item.key))
				.map((item) => ({ id: item.key, label: item.label, data: item.data, color: item.color, showMark: false, connectNulls: false })),
		[chart, hidden]
	);
	const xAxis = useMemo(
		() => [
			{
				scaleType: "time" as const,
				data: chart?.times ?? [],
				valueFormatter: (date: Date) => (range === "24h" ? fmtHour(date) : `${fmtDayLabel(date, now)} ${fmtHour(date)}`),
			},
		],
		[chart, range, now]
	);
	const toggle = (key: string) => setHidden((current) => (current.includes(key) ? current.filter((item) => item !== key) : [...current, key]));

	return (
		<Paper component="section" aria-label="History" variant="outlined" sx={{ p: 2.5, height: "100%", opacity: history.isPlaceholderData ? 0.7 : 1 }}>
			<Stack direction="row" sx={{ justifyContent: "space-between", alignItems: "center", mb: 1 }}>
				<Typography variant="h6" component="h2" sx={{ fontWeight: 700 }}>
					History · % remaining
				</Typography>
				<ToggleButtonGroup size="small" exclusive value={range} onChange={(_, value: Range | null) => value && setRange(value)}>
					<ToggleButton value="24h">24 h</ToggleButton>
					<ToggleButton value="7d">7 d</ToggleButton>
				</ToggleButtonGroup>
			</Stack>
			{data !== undefined && (
				<Stack sx={{ mb: 1 }}>
					<QueryError query={history} subject="the history" />
				</Stack>
			)}
			{isPending ? (
				<CircularProgress />
			) : !data || !chart ? (
				<QueryError query={history} subject="the history" />
			) : chart.series.length === 0 ? (
				<Typography sx={{ color: "text.secondary", py: 8, textAlign: "center" }}>No reading over the period.</Typography>
			) : (
				<>
					<Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: "wrap", mb: 1 }}>
						{chart.series.map((series) => (
							<Chip
								key={series.key}
								size="small"
								label={series.label}
								variant={hidden.includes(series.key) ? "outlined" : "filled"}
								onClick={() => toggle(series.key)}
								sx={{ borderColor: series.color }}
							/>
						))}
					</Stack>
					<LineChart height={300} skipAnimation hideLegend grid={{ horizontal: true }} xAxis={xAxis} yAxis={yAxis} series={series}>
						{data.triggerRuns.map((run) => (
							<ChartsReferenceLine
								key={run.id}
								x={new Date(run.startedAt)}
								lineStyle={{
									stroke: run.status === "failed" ? theme.palette.error.main : providerColor[run.provider],
									strokeDasharray: run.manual ? "4 4" : undefined,
								}}
							/>
						))}
					</LineChart>
					<Typography variant="caption" sx={{ color: "text.secondary" }}>
						The last known value is carried over between readings. Vertical lines: triggers (dashed: manual, red: failed).
					</Typography>
				</>
			)}
		</Paper>
	);
};
