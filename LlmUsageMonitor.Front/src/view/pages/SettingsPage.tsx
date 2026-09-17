import BoltOutlinedIcon from "@mui/icons-material/BoltOutlined";
import NotificationsOutlinedIcon from "@mui/icons-material/NotificationsOutlined";
import SpeedOutlinedIcon from "@mui/icons-material/SpeedOutlined";
import {
	Alert,
	Autocomplete,
	Box,
	Button,
	CircularProgress,
	FormControlLabel,
	Grid,
	InputAdornment,
	Paper,
	Stack,
	Switch,
	Table,
	TableBody,
	TableCell,
	TableHead,
	TableRow,
	TextField,
	Typography,
} from "@mui/material";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { type FormEvent, type ReactNode, useState } from "react";
import {
	getDashboardQueryKey,
	getNotificationSettingsOptions,
	getNotificationSettingsQueryKey,
	getPollingSettingsOptions,
	getTriggerSettingsOptions,
	sendTestNotificationMutation,
	updateNotificationSettingsMutation,
	updatePollingSettingsMutation,
	updateTriggerSettingsMutation,
} from "@/core/apis/generated/@tanstack/react-query.gen";
import type { NotificationEvents, NotificationSettingsView, PollingSettings, Provider, TriggerSettings } from "@/core/apis/generated/types.gen";
import { modelSuggestions, providerLabel } from "@/core/dashboard";
import { fmtWhen } from "@/core/format";
import { useNow } from "@hooks/useNow";
import { collect, type FieldErrors, serverFieldErrors, validateInterval, validateModel, validateNtfyUrl, validateThreshold, validateTopic } from "@/core/settings.validation";

const providers: Provider[] = ["claude", "codex"];

export const SettingsPage = () => {
	const polling = useQuery(getPollingSettingsOptions());
	const triggers = useQuery(getTriggerSettingsOptions());
	const notifications = useQuery(getNotificationSettingsOptions());

	if (polling.isPending || triggers.isPending || notifications.isPending) return <CircularProgress />;
	if (polling.isError || triggers.isError || notifications.isError) return <Alert severity="error">Impossible de charger les réglages.</Alert>;

	return (
		<Stack spacing={3}>
			<Typography variant="overline" color="text.secondary">
				03 / Configuration
			</Typography>
			<Typography variant="h5" component="h1" sx={{ fontWeight: 700 }}>
				Réglages
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

/** One settings card: header with icon and intent, fields, then the actions pinned to the bottom edge. */
const Section = ({
	title,
	subtitle,
	icon,
	actions,
	children,
	onSubmit,
}: {
	title: string;
	subtitle: string;
	icon: ReactNode;
	actions: ReactNode;
	children: ReactNode;
	onSubmit: (event: FormEvent) => void;
}) => (
	<Paper component="form" aria-label={title} variant="outlined" noValidate onSubmit={onSubmit} sx={{ p: 2.5, height: "100%", display: "flex", flexDirection: "column" }}>
		<Stack direction="row" spacing={1.5} sx={{ alignItems: "center", mb: 2.5 }}>
			<Box
				aria-hidden="true"
				sx={{ width: 34, height: 34, flexShrink: 0, bgcolor: "#12352b", color: "primary.main", borderRadius: "10px", display: "grid", placeItems: "center" }}
			>
				{icon}
			</Box>
			<Box sx={{ minWidth: 0 }}>
				<Typography variant="h6" component="h2">
					{title}
				</Typography>
				<Typography variant="caption" sx={{ color: "text.secondary", display: "block" }}>
					{subtitle}
				</Typography>
			</Box>
		</Stack>
		<Stack spacing={2.5} sx={{ minWidth: 0, flex: 1 }}>
			{children}
		</Stack>
		<Box sx={{ mt: 2.5, pt: 2, borderTop: 1, borderColor: "divider" }}>{actions}</Box>
	</Paper>
);

const SaveBar = ({ pending, saved, failed, children }: { pending: boolean; saved: boolean; failed: boolean; children?: ReactNode }) => (
	<Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ alignItems: { xs: "flex-start", sm: "center" } }}>
		<Button type="submit" variant="outlined" color="inherit" loading={pending}>
			Enregistrer
		</Button>
		{children}
		{saved && (
			<Typography variant="body2" sx={{ color: "success.main" }}>
				Enregistré, appliqué immédiatement.
			</Typography>
		)}
		{failed && (
			<Typography variant="body2" sx={{ color: "error.main" }}>
				Enregistrement refusé : voir les champs.
			</Typography>
		)}
	</Stack>
);

function PollingSection({ initial }: { initial: PollingSettings }) {
	const queryClient = useQueryClient();
	const [values, setValues] = useState(initial);
	const [errors, setErrors] = useState<FieldErrors>({});
	const save = useMutation({
		...updatePollingSettingsMutation(),
		onSuccess: () => void queryClient.invalidateQueries({ queryKey: getDashboardQueryKey() }),
		onError: (error) => setErrors(serverFieldErrors(error)),
	});

	const submit = (event: FormEvent) => {
		event.preventDefault();
		const local = collect({ claudeIntervalMinutes: validateInterval(values.claudeIntervalMinutes), codexIntervalMinutes: validateInterval(values.codexIntervalMinutes) });
		setErrors(local);
		if (Object.keys(local).length === 0) save.mutate({ body: values });
	};

	return (
		<Section
			title="Lecture de l'usage"
			subtitle="Fréquence d'interrogation de chaque provider."
			icon={<SpeedOutlinedIcon fontSize="small" />}
			onSubmit={submit}
			actions={<SaveBar pending={save.isPending} saved={save.isSuccess} failed={save.isError} />}
		>
			<Table size="small" aria-label="Intervalles de lecture">
				<TableHead>
					<TableRow>
						<TableCell sx={{ pl: 0 }}>Provider</TableCell>
						<TableCell sx={{ pr: 0, width: { xs: 140, sm: 200 } }}>Valeur</TableCell>
					</TableRow>
				</TableHead>
				<TableBody>
					{providers.map((provider) => {
						const field = provider === "claude" ? "claudeIntervalMinutes" : "codexIntervalMinutes";
						return (
							<TableRow key={provider}>
								<TableCell component="th" scope="row" sx={{ pl: 0, fontWeight: 600 }}>
									{providerLabel[provider]}
								</TableCell>
								<TableCell sx={{ pr: 0 }}>
									<TextField
										fullWidth
										type="number"
										value={values[field]}
										onChange={(event) => setValues({ ...values, [field]: Number(event.target.value) })}
										error={Boolean(errors[field])}
										helperText={errors[field]}
										slotProps={{
											htmlInput: { min: 1, max: 60, "aria-label": `Intervalle ${providerLabel[provider]} (minutes)` },
											input: { endAdornment: <InputAdornment position="end">min</InputAdornment> },
										}}
									/>
								</TableCell>
							</TableRow>
						);
					})}
				</TableBody>
			</Table>
			<Typography variant="caption" color="text.secondary">
				Entre 1 et 60 minutes. Un intervalle court augmente le risque de 429.
			</Typography>
		</Section>
	);
}

function TriggerSection({ initial }: { initial: TriggerSettings }) {
	const queryClient = useQueryClient();
	const [values, setValues] = useState(initial);
	const [errors, setErrors] = useState<FieldErrors>({});
	const save = useMutation({
		...updateTriggerSettingsMutation(),
		onSuccess: () => void queryClient.invalidateQueries({ queryKey: getDashboardQueryKey() }),
		onError: (error) => setErrors(serverFieldErrors(error)),
	});

	const submit = (event: FormEvent) => {
		event.preventDefault();
		const local = collect({ "claude.model": validateModel(values.claude.model), "codex.model": validateModel(values.codex.model) });
		setErrors(local);
		if (Object.keys(local).length === 0) save.mutate({ body: values });
	};

	return (
		<Section
			title="Déclenchement"
			subtitle="Relance d'une fenêtre d'usage après un reset."
			icon={<BoltOutlinedIcon fontSize="small" />}
			onSubmit={submit}
			actions={<SaveBar pending={save.isPending} saved={save.isSuccess} failed={save.isError} />}
		>
			<Box sx={{ overflowX: "auto" }}>
				<Table size="small" aria-label="Déclenchement par provider" sx={{ minWidth: 440 }}>
					<TableHead>
						<TableRow>
							<TableCell sx={{ pl: 0 }}>Provider</TableCell>
							<TableCell align="center" sx={{ width: 130, whiteSpace: "normal" }}>
								Automatique après reset
							</TableCell>
							<TableCell sx={{ pr: 0 }}>Modèle</TableCell>
						</TableRow>
					</TableHead>
					<TableBody>
						{providers.map((provider) => (
							<TableRow key={provider}>
								<TableCell component="th" scope="row" sx={{ pl: 0, fontWeight: 600 }}>
									{providerLabel[provider]}
								</TableCell>
								<TableCell align="center">
									<Switch
										checked={values[provider].autoEnabled}
										onChange={(event) => setValues({ ...values, [provider]: { ...values[provider], autoEnabled: event.target.checked } })}
										slotProps={{ input: { "aria-label": `${providerLabel[provider]} automatique après reset` } }}
									/>
								</TableCell>
								<TableCell sx={{ pr: 0 }}>
									<Autocomplete
										freeSolo
										autoSelect
										size="small"
										fullWidth
										options={modelSuggestions[provider]}
										inputValue={values[provider].model}
										onInputChange={(_, model) => setValues({ ...values, [provider]: { ...values[provider], model } })}
										renderInput={(params) => (
											<TextField
												{...params}
												error={Boolean(errors[`${provider}.model`])}
												helperText={errors[`${provider}.model`]}
												slotProps={{
													...params.slotProps,
													htmlInput: { ...params.slotProps.htmlInput, "aria-label": `Modèle ${providerLabel[provider]}` },
												}}
											/>
										)}
									/>
								</TableCell>
							</TableRow>
						))}
					</TableBody>
				</Table>
			</Box>
			<Typography variant="caption" color="text.secondary">
				Modèle utilisé pour le prompt « 1+1=? ».
			</Typography>
		</Section>
	);
}

const eventLabels: Record<keyof NotificationEvents, string> = {
	triggerFailed: "Déclenchement automatique en échec",
	authExpired: "Connexion expirée",
	readFailed: "Lectures en échec",
	reset: "Reset détecté",
	triggerSucceeded: "Déclenchement automatique réussi",
	recovered: "Retour à la normale",
};

function NotificationSection({ initial }: { initial: NotificationSettingsView }) {
	const queryClient = useQueryClient();
	const [values, setValues] = useState({ url: initial.url, topic: initial.topic ?? "", events: initial.events, readFailureThreshold: initial.readFailureThreshold });
	const [token, setToken] = useState("");
	const [removeToken, setRemoveToken] = useState(false);
	const [errors, setErrors] = useState<FieldErrors>({});
	const refresh = () => void queryClient.invalidateQueries({ queryKey: getNotificationSettingsQueryKey() });
	const save = useMutation({
		...updateNotificationSettingsMutation(),
		onSuccess: () => {
			setToken("");
			setRemoveToken(false);
			refresh();
		},
		onError: (error) => setErrors(serverFieldErrors(error)),
	});
	const test = useMutation({ ...sendTestNotificationMutation(), onSettled: refresh });
	const now = useNow(60_000);
	const current = queryClient.getQueryData<NotificationSettingsView>(getNotificationSettingsQueryKey()) ?? initial;

	const submit = (event: FormEvent) => {
		event.preventDefault();
		const local = collect({ url: validateNtfyUrl(values.url), topic: validateTopic(values.topic), readFailureThreshold: validateThreshold(values.readFailureThreshold) });
		setErrors(local);
		if (Object.keys(local).length > 0) return;
		save.mutate({ body: { ...values, topic: values.topic.trim() || null, token: removeToken ? "" : token || null } });
	};

	return (
		<Section
			title="Notifications ntfy"
			subtitle="Destination des alertes et événements notifiés par provider."
			icon={<NotificationsOutlinedIcon fontSize="small" />}
			onSubmit={submit}
			actions={
				<SaveBar pending={save.isPending} saved={save.isSuccess} failed={save.isError}>
					<Button variant="outlined" loading={test.isPending} disabled={!current.topic} onClick={() => test.mutate({})}>
						Envoyer un test
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
							label="Serveur ntfy"
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
							helperText={errors.topic ?? "Vide : notifications désactivées. Choisir un topic long et aléatoire."}
						/>
						<TextField
							type="password"
							label="Token d'accès"
							value={token}
							disabled={removeToken}
							autoComplete="new-password"
							onChange={(event) => setToken(event.target.value)}
							helperText={current.tokenDefined ? "Un token est défini : laisser vide pour le conserver." : "Optionnel."}
						/>
						{current.tokenDefined && (
							<FormControlLabel control={<Switch checked={removeToken} onChange={(event) => setRemoveToken(event.target.checked)} />} label="Supprimer le token" />
						)}
						<TextField
							type="number"
							label="Échecs de lecture avant alerte"
							value={values.readFailureThreshold}
							onChange={(event) => setValues({ ...values, readFailureThreshold: Number(event.target.value) })}
							error={Boolean(errors.readFailureThreshold)}
							helperText={errors.readFailureThreshold ?? "Entre 1 et 20 ; les 429 ne comptent pas."}
							slotProps={{ htmlInput: { min: 1, max: 20 } }}
						/>
					</Stack>
				</Grid>
				<Grid size={{ xs: 12, lg: 7 }}>
					<Stack spacing={1.5}>
						<Typography variant="overline" sx={{ color: "text.secondary" }}>
							Événements
						</Typography>
						<Box sx={{ overflowX: "auto" }}>
							<Table size="small" aria-label="Événements notifiés par provider" sx={{ minWidth: 380 }}>
								<TableHead>
									<TableRow>
										<TableCell sx={{ pl: 0 }}>Événement</TableCell>
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
			{current.lastSendFailure && (
				<Alert severity="warning">
					Dernier échec d'envoi {fmtWhen(current.lastSendFailure.at, now)} : {current.lastSendFailure.message}
				</Alert>
			)}
			{test.isSuccess && <Alert severity="success">Notification de test envoyée.</Alert>}
			{test.isError && <Alert severity="error">L'envoi de test a échoué.</Alert>}
		</Section>
	);
}
