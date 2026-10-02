import { Box, Paper, Typography } from "@mui/material";
import { LineChart } from "@mui/x-charts/LineChart";
import type { TokenUsageStep } from "@/core/apis/generated/types.gen";
import { providerColor, providerLabel, providers } from "@/core/dashboard";
import { fmtHour, locale } from "@/core/format";
import { type ChartPoint, fmtMetric, type UsageMetric } from "@/core/usage";
import { visuallyHidden } from "@/view/theme";

const dayFormat = new Intl.DateTimeFormat(locale, { day: "numeric", month: "short" });

/** Text alternative of the chart: per provider, the total over the period and its busiest step. */
export function describeUsageChart(points: ChartPoint[], step: TokenUsageStep, metric: UsageMetric): string[] {
	const stepLabel = (start: number) => (step === "hour" ? fmtHour(start) : dayFormat.format(start));
	return providers.map((provider) => {
		const total = points.reduce((sum, point) => sum + point[provider], 0);
		const peak = points.reduce<ChartPoint | null>((best, point) => (point[provider] > (best?.[provider] ?? 0) ? point : best), null);
		const busiest = peak ? `, at most ${fmtMetric(peak[provider], metric)} (${stepLabel(peak.start)})` : "";
		return `${providerLabel[provider]}: ${fmtMetric(total, metric)} over the period${busiest}.`;
	});
}

/** One smoothed area per provider, by hour over 24 hours and by day otherwise. */
export const UsageChart = ({ points, step, metric }: { points: ChartPoint[]; step: TokenUsageStep; metric: UsageMetric }) => {
	const title = `${metric === "cost" ? "Cost" : "Tokens"} per ${step === "hour" ? "hour" : "day"}`;
	return (
		<Paper component="section" aria-label={title} variant="outlined" sx={{ p: 2.5, height: "100%" }}>
			<Typography variant="h6" component="h2">
				{title}
			</Typography>
			<Box component="ul" aria-label="Chart summary" sx={visuallyHidden}>
				{describeUsageChart(points, step, metric).map((line) => (
					<li key={line}>{line}</li>
				))}
			</Box>
			<LineChart
				height={280}
				skipAnimation
				grid={{ horizontal: true }}
				slotProps={{ legend: { position: { vertical: "top", horizontal: "end" } } }}
				xAxis={[
					{
						scaleType: "time",
						data: points.map((point) => new Date(point.start)),
						valueFormatter: (date: Date) => (step === "hour" ? fmtHour(date) : dayFormat.format(date)),
					},
				]}
				yAxis={[{ min: 0, width: 72, valueFormatter: (value: number) => fmtMetric(value, metric) }]}
				series={providers.map((provider) => ({
					id: provider,
					label: providerLabel[provider],
					data: points.map((point) => point[provider]),
					color: providerColor[provider],
					area: true,
					curve: "monotoneX",
					showMark: false,
					valueFormatter: (value: number | null) => (value == null ? "" : fmtMetric(value, metric)),
				}))}
				sx={{ "& .MuiAreaElement-root": { fillOpacity: 0.14 } }}
			/>
		</Paper>
	);
};
