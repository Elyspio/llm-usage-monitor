import { CircularProgress, Grid, Stack } from "@mui/material";
import { useQuery } from "@tanstack/react-query";
import {
	getNotificationSettingsOptions,
	getPollingSettingsOptions,
	getTriggerSettingsOptions,
	getResetCreditSettingsOptions,
} from "@/core/apis/generated/@tanstack/react-query.gen";
import { PageHeader } from "@components/PageHeader";
import { QueryError } from "@components/QueryError";
import { NotificationSection } from "@components/settings/NotificationSection";
import { PollingSection } from "@components/settings/PollingSection";
import { TriggerSection } from "@components/settings/TriggerSection";
import { ResetCreditSection } from "@components/settings/ResetCreditSection";

export const SettingsPage = () => {
	const polling = useQuery(getPollingSettingsOptions());
	const triggers = useQuery(getTriggerSettingsOptions());
	const notifications = useQuery(getNotificationSettingsOptions());
	const resetCredits = useQuery(getResetCreditSettingsOptions());
	const queries = [polling, triggers, notifications, resetCredits];

	return (
		<Stack spacing={3}>
			<PageHeader overline="04 / Configuration" title="Settings" />
			{queries.some((query) => query.isPending) ? (
				<CircularProgress />
			) : !polling.data || !triggers.data || !notifications.data || !resetCredits.data ? (
				<QueryError query={queries.find((query) => !query.data)!} subject="the settings" />
			) : (
				<>
					<QueryError query={polling} subject="the reading settings" />
					<QueryError query={triggers} subject="the trigger settings" />
					<QueryError query={notifications} subject="the notification settings" />
					<QueryError query={resetCredits} subject="the earned reset settings" />
					<Grid container spacing={3}>
						<Grid size={{ xs: 12, lg: 6 }}>
							<PollingSection settings={polling.data} />
						</Grid>
						<Grid size={{ xs: 12, lg: 6 }}>
							<TriggerSection settings={triggers.data} />
						</Grid>
						<Grid size={12}>
							<ResetCreditSection settings={resetCredits.data} />
						</Grid>
						<Grid size={12}>
							<NotificationSection settings={notifications.data} />
						</Grid>
					</Grid>
				</>
			)}
		</Stack>
	);
};
