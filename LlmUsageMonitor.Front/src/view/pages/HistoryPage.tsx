import { CircularProgress, Grid, Stack, Typography } from "@mui/material";
import { useMemo } from "react";
import { HistoryCard } from "@components/dashboard/HistoryCard";
import { TriggerJournal } from "@components/dashboard/TriggerJournal";
import { QueryError } from "@components/QueryError";
import { useDashboard } from "@hooks/useDashboard";

export const HistoryPage = () => {
	const dashboard = useDashboard();
	const { data } = dashboard;
	const durations = useMemo(
		() =>
			Object.fromEntries(
				(data?.providers ?? []).flatMap((provider) =>
					(provider.lastReading?.windows ?? []).map((window) => [`${provider.provider}:${window.id}`, window.windowDurationMinutes ?? null])
				)
			),
		[data]
	);

	if (dashboard.isPending) return <CircularProgress />;
	if (!data) return <QueryError query={dashboard} subject="the history" />;

	return (
		<Stack spacing={3}>
			<Typography variant="overline" sx={{ color: "text.secondary" }}>
				02 / History
			</Typography>
			<Typography variant="h5" component="h1" sx={{ fontWeight: 700 }}>
				History
			</Typography>
			<QueryError query={dashboard} subject="the trigger log" />
			<Grid container spacing={3}>
				<Grid size={{ xs: 12, lg: 7 }}>
					<HistoryCard durations={durations} />
				</Grid>
				<Grid size={{ xs: 12, lg: 5 }}>
					<TriggerJournal runs={data.recentTriggerRuns} />
				</Grid>
			</Grid>
		</Stack>
	);
};
