import { Alert, Chip, CircularProgress, Paper, Stack, ToggleButton, ToggleButtonGroup, Typography } from "@mui/material";
import { ChartsReferenceLine } from "@mui/x-charts/ChartsReferenceLine";
import { LineChart } from "@mui/x-charts/LineChart";
import { useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { getHistoryOptions } from "@/core/apis/generated/@tanstack/react-query.gen";
import type { UsageHistory } from "@/core/apis/generated/types.gen";
import { providerColor, providerLabel, windowLabel } from "@/core/dashboard";
import { fmtDayLabel, fmtHour } from "@/core/format";

type Range = "24h" | "7d";

type Series = { key: string; label: string; color: string; data: (number | null)[] };
const claudeSessionColor = "#f4a261";

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
			color: item.provider === "claude" && item.windowId === "five_hour" ? claudeSessionColor : providerColor[item.provider],
			data,
		};
	});
	return { times: times.map((time) => new Date(time)), series };
}

export const HistoryCard = ({ durations, now }: { durations: Record<string, number | null>; now: number }) => {
	const [range, setRange] = useState<Range>("24h");
	const [hidden, setHidden] = useState<string[]>([]);
	const { data, isPending, isError } = useQuery({ ...getHistoryOptions({ query: { range } }), refetchInterval: 60_000 });
	const chart = useMemo(() => (data ? toChart(data, durations) : null), [data, durations]);
	const visible = chart?.series.filter((series) => !hidden.includes(series.key)) ?? [];
	const toggle = (key: string) => setHidden((current) => (current.includes(key) ? current.filter((item) => item !== key) : [...current, key]));

	return (
		<Paper component="section" aria-label="Historique" variant="outlined" sx={{ p: 2.5, height: "100%" }}>
			<Stack direction="row" sx={{ justifyContent: "space-between", alignItems: "center", mb: 1 }}>
				<Typography variant="h6" component="h2" sx={{ fontWeight: 700 }}>
					Historique · % restant
				</Typography>
				<ToggleButtonGroup size="small" exclusive value={range} onChange={(_, value: Range | null) => value && setRange(value)}>
					<ToggleButton value="24h">24 h</ToggleButton>
					<ToggleButton value="7d">7 j</ToggleButton>
				</ToggleButtonGroup>
			</Stack>
			{isError ? (
				<Alert severity="error">Impossible de charger l'historique.</Alert>
			) : isPending || !chart ? (
				<CircularProgress />
			) : chart.series.length === 0 ? (
				<Typography sx={{ color: "text.secondary", py: 8, textAlign: "center" }}>Aucune lecture sur la période.</Typography>
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
					<LineChart
						height={300}
						skipAnimation
						hideLegend
						grid={{ horizontal: true }}
						xAxis={[
							{
								scaleType: "time",
								data: chart.times,
								valueFormatter: (date: Date) => (range === "24h" ? fmtHour(date) : `${fmtDayLabel(date, now)} ${fmtHour(date)}`),
							},
						]}
						yAxis={[{ min: 0, max: 100, valueFormatter: (value: number) => `${value} %` }]}
						series={visible.map((series) => ({ id: series.key, label: series.label, data: series.data, color: series.color, showMark: false, connectNulls: false }))}
					>
						{data!.triggerRuns.map((run) => (
							<ChartsReferenceLine
								key={run.id}
								x={new Date(run.startedAt)}
								lineStyle={{ stroke: run.status === "failed" ? "#d32f2f" : providerColor[run.provider], strokeDasharray: run.manual ? "4 4" : undefined }}
							/>
						))}
					</LineChart>
					<Typography variant="caption" sx={{ color: "text.secondary" }}>
						La dernière valeur connue est prolongée entre les lectures. Lignes verticales : déclenchements (pointillés : manuels, rouge : échec).
					</Typography>
				</>
			)}
		</Paper>
	);
};
