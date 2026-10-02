import { CircularProgress, Grid, Stack } from "@mui/material";
import { useMemo } from "react";
import { HistoryCard } from "@components/dashboard/HistoryCard";
import { TriggerJournal } from "@components/dashboard/TriggerJournal";
import { PageHeader } from "@components/PageHeader";
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

	return (
		<Stack spacing={3}>
			<PageHeader overline="02 / History" title="History" />
			{dashboard.isPending ? (
				<CircularProgress />
			) : !data ? (
				<QueryError query={dashboard} subject="the history" />
			) : (
				<>
					<QueryError query={dashboard} subject="the trigger log" />
					<Grid container spacing={3}>
						<Grid size={{ xs: 12, lg: 7 }}>
							<HistoryCard durations={durations} />
						</Grid>
						<Grid size={{ xs: 12, lg: 5 }}>
							<TriggerJournal runs={data.recentTriggerRuns} />
						</Grid>
					</Grid>
				</>
			)}
		</Stack>
	);
};
