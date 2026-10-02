import { Alert, Box, CircularProgress, Grid, Stack, Typography } from "@mui/material";
import { ProviderColumn } from "@components/dashboard/ProviderColumn";
import { UsageTimeline } from "@components/dashboard/UsageTimeline";
import { useDashboard } from "@hooks/useDashboard";
import { useNow } from "@hooks/useNow";

export const DashboardPage = () => {
	const now = useNow();
	const { data, isPending, isError } = useDashboard();

	if (isPending) return <CircularProgress />;
	if (isError) return <Alert severity="error">Could not load the dashboard.</Alert>;

	return (
		<Stack spacing={4}>
			<Box>
				<Typography variant="overline" sx={{ color: "text.secondary" }}>
					01 / Dashboard
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
