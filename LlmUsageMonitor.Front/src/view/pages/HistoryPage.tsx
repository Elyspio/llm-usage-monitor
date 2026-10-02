import { Alert, CircularProgress, Grid, Stack, Typography } from "@mui/material";
import { useMemo } from "react";
import { HistoryCard } from "@components/dashboard/HistoryCard";
import { TriggerJournal } from "@components/dashboard/TriggerJournal";
import { useDashboard } from "@hooks/useDashboard";
import { useNow } from "@hooks/useNow";

export const HistoryPage = () => {
	const now = useNow();
	const { data, isPending, isError } = useDashboard();
	const durations = useMemo(
		() =>
			Object.fromEntries(
				(data?.providers ?? []).flatMap((provider) =>
					(provider.lastReading?.windows ?? []).map((window) => [`${provider.provider}:${window.id}`, window.windowDurationMinutes ?? null])
				)
			),
		[data]
	);

	if (isPending) return <CircularProgress />;
	if (isError) return <Alert severity="error">Could not load the history.</Alert>;

	return (
		<Stack spacing={3}>
			<Typography variant="overline" sx={{ color: "text.secondary" }}>
				02 / History
			</Typography>
			<Typography variant="h5" component="h1" sx={{ fontWeight: 700 }}>
				History
			</Typography>
			<Grid container spacing={3}>
				<Grid size={{ xs: 12, lg: 7 }}>
					<HistoryCard durations={durations} now={now} />
				</Grid>
				<Grid size={{ xs: 12, lg: 5 }}>
					<TriggerJournal runs={data.recentTriggerRuns} now={now} />
				</Grid>
			</Grid>
		</Stack>
	);
};
