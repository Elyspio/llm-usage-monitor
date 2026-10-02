import { CircularProgress, Grid, Stack } from "@mui/material";
import { ProviderColumn } from "@components/dashboard/ProviderColumn";
import { UsageTimeline } from "@components/dashboard/UsageTimeline";
import { PageHeader } from "@components/PageHeader";
import { QueryError } from "@components/QueryError";
import { useDashboard } from "@hooks/useDashboard";
import { useNow } from "@hooks/useNow";

export const DashboardPage = () => {
	const now = useNow();
	const dashboard = useDashboard();
	const { data } = dashboard;

	return (
		<Stack spacing={4}>
			<PageHeader overline="01 / Dashboard" title="Dashboard" />
			{dashboard.isPending ? (
				<CircularProgress />
			) : !data ? (
				<QueryError query={dashboard} subject="the dashboard" />
			) : (
				<>
					<QueryError query={dashboard} subject="the dashboard" />
					<UsageTimeline providers={data.providers} now={now} />
					<Grid container spacing={3}>
						{data.providers.map((provider) => (
							<Grid key={provider.provider} size={{ xs: 12, md: 6 }}>
								<ProviderColumn provider={provider} now={now} />
							</Grid>
						))}
					</Grid>
				</>
			)}
		</Stack>
	);
};
