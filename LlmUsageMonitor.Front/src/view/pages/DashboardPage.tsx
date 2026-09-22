import { Alert, Box, CircularProgress, Grid, Stack, Typography } from "@mui/material";
import { useQuery } from "@tanstack/react-query";
import { getDashboardOptions } from "@/core/apis/generated/@tanstack/react-query.gen";
import { ProviderColumn } from "@components/dashboard/ProviderColumn";
import { UsageTimeline } from "@components/dashboard/UsageTimeline";
import { useNow } from "@hooks/useNow";

export const DashboardPage = () => {
	const now = useNow();
	const { data, isPending, isError } = useQuery({ ...getDashboardOptions(), refetchInterval: 30_000, refetchOnWindowFocus: true });

	if (isPending) return <CircularProgress />;
	if (isError) return <Alert severity="error">Impossible de charger le tableau de bord.</Alert>;

	return (
		<Stack spacing={4}>
			<Box>
				<Typography variant="overline" sx={{ color: "text.secondary" }}>
					01 / Tableau de bord
				</Typography>
			</Box>
			<UsageTimeline providers={data.providers} now={now} />
			<Grid container spacing={3}>
				{data.providers.map((provider) => (
					<Grid key={provider.provider} size={{ xs: 12, md: 6 }}>
						<ProviderColumn provider={provider} now={now} />
					</Grid>
				))}
			</Grid>
		</Stack>
	);
};
