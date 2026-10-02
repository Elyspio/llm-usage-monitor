import { CircularProgress, Grid, Stack, Typography } from "@mui/material";
import { useQuery } from "@tanstack/react-query";
import { getNotificationSettingsOptions, getPollingSettingsOptions, getTriggerSettingsOptions } from "@/core/apis/generated/@tanstack/react-query.gen";
import { QueryError } from "@components/QueryError";
import { NotificationSection } from "@components/settings/NotificationSection";
import { PollingSection } from "@components/settings/PollingSection";
import { TriggerSection } from "@components/settings/TriggerSection";

export const SettingsPage = () => {
	const polling = useQuery(getPollingSettingsOptions());
	const triggers = useQuery(getTriggerSettingsOptions());
	const notifications = useQuery(getNotificationSettingsOptions());

	if (polling.isPending || triggers.isPending || notifications.isPending) return <CircularProgress />;
	if (!polling.data || !triggers.data || !notifications.data) {
		const failed = [polling, triggers, notifications].find((query) => !query.data)!;
		return <QueryError query={failed} subject="the settings" />;
	}

	return (
		<Stack spacing={3}>
			<Typography variant="overline" sx={{ color: "text.secondary" }}>
				04 / Configuration
			</Typography>
			<Typography variant="h5" component="h1" sx={{ fontWeight: 700 }}>
				Settings
			</Typography>
			<QueryError query={polling} subject="the reading settings" />
			<QueryError query={triggers} subject="the trigger settings" />
			<QueryError query={notifications} subject="the notification settings" />
			<Grid container spacing={3}>
				<Grid size={{ xs: 12, lg: 6 }}>
					<PollingSection settings={polling.data} />
				</Grid>
				<Grid size={{ xs: 12, lg: 6 }}>
					<TriggerSection settings={triggers.data} />
				</Grid>
				<Grid size={12}>
					<NotificationSection settings={notifications.data} />
				</Grid>
			</Grid>
		</Stack>
	);
};
