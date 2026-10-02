import { Alert, CircularProgress, Grid, Stack, Typography } from "@mui/material";
import { useQuery } from "@tanstack/react-query";
import { getNotificationSettingsOptions, getPollingSettingsOptions, getTriggerSettingsOptions } from "@/core/apis/generated/@tanstack/react-query.gen";
import { NotificationSection } from "@components/settings/NotificationSection";
import { PollingSection } from "@components/settings/PollingSection";
import { TriggerSection } from "@components/settings/TriggerSection";

export const SettingsPage = () => {
	const polling = useQuery(getPollingSettingsOptions());
	const triggers = useQuery(getTriggerSettingsOptions());
	const notifications = useQuery(getNotificationSettingsOptions());

	if (polling.isPending || triggers.isPending || notifications.isPending) return <CircularProgress />;
	if (polling.isError || triggers.isError || notifications.isError) return <Alert severity="error">Could not load the settings.</Alert>;

	return (
		<Stack spacing={3}>
			<Typography variant="overline" sx={{ color: "text.secondary" }}>
				04 / Configuration
			</Typography>
			<Typography variant="h5" component="h1" sx={{ fontWeight: 700 }}>
				Settings
			</Typography>
			<Grid container spacing={3}>
				<Grid size={{ xs: 12, lg: 6 }}>
					<PollingSection initial={polling.data} />
				</Grid>
				<Grid size={{ xs: 12, lg: 6 }}>
					<TriggerSection initial={triggers.data} />
				</Grid>
				<Grid size={12}>
					<NotificationSection initial={notifications.data} />
				</Grid>
			</Grid>
		</Stack>
	);
};
