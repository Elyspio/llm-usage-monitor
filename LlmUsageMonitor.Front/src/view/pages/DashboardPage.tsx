import { Alert, Box, CircularProgress, Grid, Stack, Typography } from "@mui/material";
import { useQuery } from "@tanstack/react-query";
import { useMemo } from "react";
import { getDashboardOptions } from "@/core/apis/generated/@tanstack/react-query.gen";
import { HistoryCard } from "@components/dashboard/HistoryCard";
import { ProviderColumn } from "@components/dashboard/ProviderColumn";
import { TriggerJournal } from "@components/dashboard/TriggerJournal";
import { UsageTimeline } from "@components/dashboard/UsageTimeline";
import { useNow } from "@hooks/useNow";

export const DashboardPage = () => {
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
	if (isError) return <Alert severity="error">Impossible de charger le tableau de bord.</Alert>;

	return (
		<Stack spacing={4}>
			<Box>
				<Typography variant="overline" color="text.secondary">
					01 / Tableau de board
				</Typography>
			</Box>
			<UsageTimeline providers={data.providers} now={now} />
			<Grid container spacing={3}>
				{data.providers.map((provider) => (
					<Grid key={provider.provider} size={{ xs: 12, md: 6 }}>
						<ProviderColumn provider={provider} now={now} />
					</Grid>
				))}
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
