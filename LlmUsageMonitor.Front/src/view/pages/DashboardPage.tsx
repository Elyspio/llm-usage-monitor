import { Box, CircularProgress, Grid, Stack, Typography } from "@mui/material";
import { ProviderColumn } from "@components/dashboard/ProviderColumn";
import { UsageTimeline } from "@components/dashboard/UsageTimeline";
import { QueryError } from "@components/QueryError";
import { useDashboard } from "@hooks/useDashboard";
import { useNow } from "@hooks/useNow";

export const DashboardPage = () => {
	const now = useNow();
	const dashboard = useDashboard();
	const { data } = dashboard;

	if (dashboard.isPending) return <CircularProgress />;
	if (!data) return <QueryError query={dashboard} subject="the dashboard" />;

	return (
		<Stack spacing={4}>
			<Box>
				<Typography variant="overline" sx={{ color: "text.secondary" }}>
					01 / Dashboard
				</Typography>
			</Box>
			<QueryError query={dashboard} subject="the dashboard" />
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
