import { Alert, Box, CircularProgress, Grid, MenuItem, Paper, Stack, TextField, ToggleButton, ToggleButtonGroup, Typography } from "@mui/material";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { getTokenUsageOptions } from "@/core/apis/generated/@tanstack/react-query.gen";
import { providerColor, providerLabel } from "@/core/dashboard";
import { locale } from "@/core/format";
import { browserTimeZone, fmtMetric, fmtShare, fmtTokens, fmtUsd, rangeLabel, summarize, type UsageMetric, type UsageRange } from "@/core/usage";
import { ProviderLogo } from "@components/dashboard/ProviderLogo";
import { UsageBreakdown } from "@components/usage/UsageBreakdown";
import { UsageChart } from "@components/usage/UsageChart";

const ALL_MACHINES = "all";
const periodFormat = new Intl.DateTimeFormat(locale, { day: "numeric", month: "short" });
const periodHourFormat = new Intl.DateTimeFormat(locale, { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });

export const UsagePage = () => {
	const [range, setRange] = useState<UsageRange>("7d");
	const [metric, setMetric] = useState<UsageMetric>("cost");
	const [machineId, setMachineId] = useState(ALL_MACHINES);
	const { data, isPending, isError, isFetching } = useQuery({
		...getTokenUsageOptions({ query: { range, timeZone: browserTimeZone(), machineId: machineId === ALL_MACHINES ? undefined : machineId } }),
		refetchInterval: 5 * 60_000,
		refetchOnWindowFocus: true,
		placeholderData: keepPreviousData,
	});
	const summary = useMemo(() => (data ? summarize(data, metric) : null), [data, metric]);

	const period = data
		? data.step === "hour"
			? `${periodHourFormat.format(Date.parse(data.from))} → now`
			: `${periodFormat.format(Date.parse(data.from))} → ${periodFormat.format(Date.parse(data.to))}`
		: "";

	return (
		<Stack spacing={3}>
			<Typography variant="overline" color="text.secondary">
				03 / Usage
			</Typography>
			<Stack direction={{ xs: "column", lg: "row" }} spacing={2} sx={{ justifyContent: "space-between", alignItems: { lg: "flex-end" } }}>
				<Box>
					<Typography variant="h5" component="h1" sx={{ fontWeight: 700 }}>
						Usage
					</Typography>
					<Typography variant="body2" sx={{ color: "text.secondary" }}>
						{period}
					</Typography>
				</Box>
				<Stack direction="row" spacing={1.5} useFlexGap sx={{ flexWrap: "wrap", alignItems: "center", opacity: isFetching && !isPending ? 0.7 : 1 }}>
					<TextField select label="Workstation" value={machineId} onChange={(event) => setMachineId(event.target.value)} sx={{ minWidth: 180 }}>
						<MenuItem value={ALL_MACHINES}>All workstations</MenuItem>
						{data?.machines.map((machine) => (
							<MenuItem key={machine.id} value={machine.id}>
								{machine.name}
							</MenuItem>
						))}
					</TextField>
					<ToggleButtonGroup size="small" exclusive value={metric} onChange={(_, value: UsageMetric | null) => value && setMetric(value)} aria-label="Metric">
						<ToggleButton value="cost">Cost</ToggleButton>
						<ToggleButton value="tokens">Tokens</ToggleButton>
					</ToggleButtonGroup>
					<ToggleButtonGroup size="small" exclusive value={range} onChange={(_, value: UsageRange | null) => value && setRange(value)} aria-label="Period">
						{(Object.keys(rangeLabel) as UsageRange[]).map((key) => (
							<ToggleButton key={key} value={key}>
								{rangeLabel[key]}
							</ToggleButton>
						))}
					</ToggleButtonGroup>
				</Stack>
			</Stack>

			{isPending ? (
				<CircularProgress />
			) : isError || !summary ? (
				<Alert severity="error">Could not load the usage.</Alert>
			) : summary.total.tokens === 0 ? (
				<Paper variant="outlined" sx={{ p: 4, textAlign: "center", color: "text.secondary" }}>
					No usage received over the period. Workstations upload their Claude Code and Codex logs through the collector of the media-tools app.
				</Paper>
			) : (
				<>
					<Grid container spacing={3}>
						<Grid size={{ xs: 12, lg: 4 }}>
							<Paper component="section" aria-label="Total" variant="outlined" sx={{ p: 2.5, height: "100%" }}>
								<Typography sx={{ fontSize: "2.75rem", fontWeight: 700, letterSpacing: "-0.04em", lineHeight: 1.1 }}>
									{metric === "cost" ? fmtUsd(summary.total.cost) : fmtTokens(summary.total.tokens)}
								</Typography>
								<Typography variant="body2" sx={{ color: "text.secondary", mt: 0.5 }}>
									{metric === "cost"
										? summary.unpricedShare > 0
											? `API equivalent · excluding ${fmtShare(summary.unpricedShare)} of unpriced tokens`
											: "API equivalent"
										: "Tokens processed"}
								</Typography>
								<Stack spacing={2.5} sx={{ mt: 3 }}>
									{summary.providers.map((provider) => (
										<Box key={provider.provider}>
											<Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
												<ProviderLogo provider={provider.provider} sx={{ fontSize: 18, color: providerColor[provider.provider] }} />
												<Typography sx={{ fontWeight: 600, flex: 1 }}>{providerLabel[provider.provider]}</Typography>
												<Typography sx={{ fontWeight: 700, fontVariantNumeric: "tabular-nums" }}>
													{fmtMetric(metric === "cost" ? provider.cost : provider.tokens, metric)}
												</Typography>
											</Stack>
											<Typography variant="body2" sx={{ color: "text.secondary", pl: 3.25 }}>
												{fmtShare(provider.share)} {metric === "cost" ? "of the cost" : "of the tokens"} ·{" "}
												{metric === "cost" ? `${fmtTokens(provider.tokens)} tokens` : fmtUsd(provider.cost)}
											</Typography>
										</Box>
									))}
								</Stack>
							</Paper>
						</Grid>
						<Grid size={{ xs: 12, lg: 8 }}>
							<UsageChart points={summary.chart} step={data!.step} metric={metric} />
						</Grid>
					</Grid>

					<Paper component="section" aria-label="Totals" variant="outlined" sx={{ p: 2.5 }}>
						<Typography variant="h6" component="h2" sx={{ mb: 2 }}>
							Totals
						</Typography>
						<Grid container spacing={2}>
							{[
								["Tokens processed", fmtTokens(summary.total.tokens)],
								["Cached input", fmtTokens(summary.tokens.cacheRead)],
								["Uncached input", fmtTokens(summary.tokens.input + summary.tokens.cacheWrite)],
								["Output", fmtTokens(summary.tokens.output)],
								["Cache savings", fmtUsd(summary.total.savings)],
							].map(([label, value]) => (
								<Grid key={label} size={{ xs: 6, sm: 4, md: "grow" }}>
									<Typography variant="body2" sx={{ color: "text.secondary" }}>
										{label}
									</Typography>
									<Typography sx={{ fontSize: "1.25rem", fontWeight: 700, fontVariantNumeric: "tabular-nums" }}>{value}</Typography>
								</Grid>
							))}
						</Grid>
					</Paper>

					<UsageBreakdown models={summary.models} days={summary.days} metric={metric} />
				</>
			)}
		</Stack>
	);
};
