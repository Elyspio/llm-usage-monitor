import { Alert, CircularProgress, Grid, Stack, Typography } from "@mui/material";
import { useQuery } from "@tanstack/react-query";
import { useMemo } from "react";
import { getDashboardOptions } from "@/core/apis/generated/@tanstack/react-query.gen";
import { HistoryCard } from "@components/dashboard/HistoryCard";
import { TriggerJournal } from "@components/dashboard/TriggerJournal";
import { useNow } from "@hooks/useNow";

export const HistoryPage = () => {
	const now = useNow();
	const { data, isPending, isError } = useQuery({ ...getDashboardOptions(), refetchInterval: 30_000, refetchOnWindowFocus: true });
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
	if (isError) return <Alert severity="error">Impossible de charger l'historique.</Alert>;

	return (
		<Stack spacing={3}>
			<Typography variant="overline" sx={{ color: "text.secondary" }}>
				02 / Historique
			</Typography>
			<Typography variant="h5" component="h1" sx={{ fontWeight: 700 }}>
				Historique
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
