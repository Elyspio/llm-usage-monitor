import NotificationsOutlinedIcon from "@mui/icons-material/NotificationsOutlined";
import { Alert, Box, Button, FormControlLabel, Grid, Stack, Switch, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from "@mui/material";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { type FormEvent, useState } from "react";
import { getNotificationSettingsQueryKey, sendTestNotificationMutation, updateNotificationSettingsMutation } from "@/core/apis/generated/@tanstack/react-query.gen";
import type { NotificationEvents, NotificationSettingsView } from "@/core/apis/generated/types.gen";
import { providerLabel, providers } from "@/core/dashboard";
import { fmtWhen } from "@/core/format";
import {
	collect,
	type FieldErrors,
	serverFieldErrors,
	validateAlertDays,
	validateNtfyUrl,
	validateThreshold,
	validateTokenForServer,
	validateTopic,
} from "@/core/settings.validation";
import { useNow } from "@hooks/useNow";
import { SaveBar, SettingsSection, useDraft } from "./SettingsSection";

/** The editable fields; the token is apart, never read back from the server. */
type NotificationForm = Pick<NotificationSettingsView, "url" | "events" | "readFailureThreshold" | "credentialExpiryAlertDays"> & { topic: string };

const eventLabels: Record<keyof NotificationEvents, string> = {
	triggerFailed: "Automatic trigger failed",
	authExpired: "Login expired",
	readFailed: "Readings failing",
	reset: "Reset detected",
	triggerSucceeded: "Automatic trigger succeeded",
	recovered: "Back to normal",
};

export function NotificationSection({ settings }: { settings: NotificationSettingsView }) {
	const queryClient = useQueryClient();
	const { values, edit, discard } = useDraft<NotificationForm>({
		url: settings.url,
		topic: settings.topic ?? "",
		events: settings.events,
		readFailureThreshold: settings.readFailureThreshold,
		credentialExpiryAlertDays: settings.credentialExpiryAlertDays,
	});
	const [token, setToken] = useState("");
	const [removeToken, setRemoveToken] = useState(false);
	const [errors, setErrors] = useState<FieldErrors>({});
	const refresh = () => void queryClient.invalidateQueries({ queryKey: getNotificationSettingsQueryKey() });
	const save = useMutation({
		...updateNotificationSettingsMutation(),
		onSuccess: (saved) => {
			queryClient.setQueryData(getNotificationSettingsQueryKey(), saved);
			discard();
			setToken("");
			setRemoveToken(false);
		},
		onError: (error) => setErrors(serverFieldErrors(error)),
	});
	const test = useMutation({ ...sendTestNotificationMutation(), onSettled: refresh });
	const now = useNow(60_000);
	// A new edit hides the "Saved" message of the previous save.
	const touched = () => {
		if (save.isSuccess) save.reset();
	};
	const setValues = (next: NotificationForm) => {
		edit(next);
		touched();
	};

	const submit = (event: FormEvent) => {
		event.preventDefault();
		const local = collect({
			url: validateNtfyUrl(values.url),
			topic: validateTopic(values.topic),
			token: validateTokenForServer({ savedUrl: settings.url, url: values.url, tokenDefined: settings.tokenDefined, token, removeToken }),
			readFailureThreshold: validateThreshold(values.readFailureThreshold),
			credentialExpiryAlertDays: validateAlertDays(values.credentialExpiryAlertDays),
		});
		setErrors(local);
		if (Object.keys(local).length > 0) return;
		save.mutate({ body: { ...values, topic: values.topic.trim() || null, token: removeToken ? "" : token || null } });
	};

	return (
		<SettingsSection
			title="Notifications ntfy"
			subtitle="Where the alerts go and which events are notified per provider."
			icon={<NotificationsOutlinedIcon fontSize="small" />}
			onSubmit={submit}
			actions={
				<SaveBar pending={save.isPending} saved={save.isSuccess} error={save.error}>
					<Button variant="outlined" loading={test.isPending} disabled={!settings.topic} onClick={() => test.mutate({})}>
						Send a test
					</Button>
				</SaveBar>
			}
		>
			<Grid container spacing={{ xs: 2.5, lg: 4 }}>
				<Grid size={{ xs: 12, lg: 5 }}>
					<Stack spacing={2.5}>
						<Typography variant="overline" sx={{ color: "text.secondary" }}>
							Destination
						</Typography>
						<TextField
							label="ntfy server"
							value={values.url}
							onChange={(event) => setValues({ ...values, url: event.target.value })}
							error={Boolean(errors.url)}
							helperText={errors.url}
						/>
						<TextField
							label="Topic"
							value={values.topic}
							onChange={(event) => setValues({ ...values, topic: event.target.value })}
							error={Boolean(errors.topic)}
							helperText={errors.topic ?? "Empty: notifications disabled. Pick a long, random topic."}
						/>
						<TextField
							type="password"
							label="Access token"
							value={token}
							disabled={removeToken}
							autoComplete="new-password"
							onChange={(event) => {
								setToken(event.target.value);
								touched();
							}}
							error={Boolean(errors.token)}
							helperText={errors.token ?? (settings.tokenDefined ? "A token is set: leave empty to keep it on the same server." : "Optional.")}
						/>
						{settings.tokenDefined && (
							<FormControlLabel
								control={
									<Switch
										checked={removeToken}
										onChange={(event) => {
											setRemoveToken(event.target.checked);
											touched();
										}}
									/>
								}
								label="Remove the token"
							/>
						)}
						<TextField
							type="number"
							label="Read failures before alert"
							value={values.readFailureThreshold}
							onChange={(event) => setValues({ ...values, readFailureThreshold: Number(event.target.value) })}
							error={Boolean(errors.readFailureThreshold)}
							helperText={errors.readFailureThreshold ?? "Between 1 and 20; repeated 429s are counted apart, with the same threshold."}
							slotProps={{ htmlInput: { min: 1, max: 20 } }}
						/>
						<TextField
							type="number"
							label="Login expiry warning (days before)"
							value={values.credentialExpiryAlertDays}
							onChange={(event) => setValues({ ...values, credentialExpiryAlertDays: Number(event.target.value) })}
							error={Boolean(errors.credentialExpiryAlertDays)}
							helperText={errors.credentialExpiryAlertDays ?? 'Between 1 and 60; sent with the "Login expired" event, before the CLI login runs out.'}
							slotProps={{ htmlInput: { min: 1, max: 60 } }}
						/>
					</Stack>
				</Grid>
				<Grid size={{ xs: 12, lg: 7 }}>
					<Stack spacing={1.5}>
						<Typography variant="overline" sx={{ color: "text.secondary" }}>
							Events
						</Typography>
						<Box sx={{ overflowX: "auto" }}>
							<Table size="small" aria-label="Notified events per provider" sx={{ minWidth: 380 }}>
								<TableHead>
									<TableRow>
										<TableCell sx={{ pl: 0 }}>Event</TableCell>
										{providers.map((provider) => (
											<TableCell key={provider} align="center" sx={{ width: 96 }}>
												{providerLabel[provider]}
											</TableCell>
										))}
									</TableRow>
								</TableHead>
								<TableBody>
									{(Object.keys(eventLabels) as (keyof NotificationEvents)[]).map((event) => (
										<TableRow key={event}>
											<TableCell component="th" scope="row" sx={{ pl: 0, fontSize: "0.82rem" }}>
												{eventLabels[event]}
											</TableCell>
											{providers.map((provider) => (
												<TableCell key={provider} align="center">
													<Switch
														checked={values.events[provider][event]}
														onChange={(change) =>
															setValues({
																...values,
																events: { ...values.events, [provider]: { ...values.events[provider], [event]: change.target.checked } },
															})
														}
														slotProps={{ input: { "aria-label": providerLabel[provider] } }}
													/>
												</TableCell>
											))}
										</TableRow>
									))}
								</TableBody>
							</Table>
						</Box>
					</Stack>
				</Grid>
			</Grid>
			{settings.lastSendFailure && (
				<Alert severity="warning">
					Last send failure {fmtWhen(settings.lastSendFailure.at, now)}: {settings.lastSendFailure.message}
				</Alert>
			)}
			{test.isSuccess && <Alert severity="success">Test notification sent.</Alert>}
			{test.isError && <Alert severity="error">The test send failed.</Alert>}
		</SettingsSection>
	);
}
