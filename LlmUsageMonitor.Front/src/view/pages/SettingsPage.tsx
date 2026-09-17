import {
	Alert,
	Box,
	Button,
	CircularProgress,
	FormControlLabel,
	InputAdornment,
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
import { providerLabel } from "@/core/dashboard";
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
		<Stack spacing={3} sx={{ maxWidth: 1100 }}>
			<Typography variant="overline" color="text.secondary">
				02 / Configuration
			</Typography>
			<Typography variant="h5" component="h1" sx={{ fontWeight: 700 }}>
				Réglages
			</Typography>
			<PollingSection initial={polling.data} />
			<TriggerSection initial={triggers.data} />
			<NotificationSection initial={notifications.data} />
		</Stack>
	);
};

const Section = ({ title, children, onSubmit }: { title: string; children: ReactNode; onSubmit: (event: FormEvent) => void }) => (
	<Box
		component="form"
		aria-label={title}
		noValidate
		onSubmit={onSubmit}
		sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "220px minmax(0, 1fr)" }, gap: 3, py: 3, borderTop: 1, borderColor: "divider" }}
	>
		<Typography variant="h6" component="h2" sx={{ fontSize: "0.95rem" }}>
			{title}
		</Typography>
		<Stack spacing={2.5} sx={{ minWidth: 0 }}>
			{children}
		</Stack>
	</Box>
);

const SaveBar = ({ pending, saved, failed }: { pending: boolean; saved: boolean; failed: boolean }) => (
	<Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ alignItems: { xs: "flex-start", sm: "center" } }}>
		<Button type="submit" variant="outlined" color="inherit" loading={pending}>
			Enregistrer
		</Button>
		{saved && <Typography sx={{ color: "success.main" }}>Enregistré, appliqué immédiatement.</Typography>}
		{failed && <Typography sx={{ color: "error.main" }}>Enregistrement refusé : voir les champs.</Typography>}
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
		<Section title="Lecture de l'usage" onSubmit={submit}>
			<Box>
				<Table size="small" aria-label="Intervalles de lecture">
					<TableHead>
						<TableRow>
							<TableCell sx={{ pl: 0 }}>Provider</TableCell>
							<TableCell sx={{ pr: 0, width: { xs: 140, sm: 220 } }}>Valeur</TableCell>
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
			</Box>
			<Typography variant="caption" color="text.secondary">
				Entre 1 et 60 minutes. Un intervalle court augmente le risque de 429.
			</Typography>
			<SaveBar pending={save.isPending} saved={save.isSuccess} failed={save.isError} />
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
		<Section title="Déclenchement" onSubmit={submit}>
			<Box sx={{ overflowX: "auto" }}>
				<Table size="small" aria-label="Déclenchement par provider" sx={{ minWidth: 520 }}>
					<TableHead>
						<TableRow>
							<TableCell sx={{ pl: 0 }}>Provider</TableCell>
							<TableCell align="center" sx={{ width: 180, whiteSpace: "nowrap", fontSize: "0.65rem" }}>
								Automatique après reset
							</TableCell>
							<TableCell sx={{ pr: 0, width: 260 }}>Modèle</TableCell>
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
									<TextField
										fullWidth
										value={values[provider].model}
										onChange={(event) => setValues({ ...values, [provider]: { ...values[provider], model: event.target.value } })}
										error={Boolean(errors[`${provider}.model`])}
										helperText={errors[`${provider}.model`]}
										slotProps={{ htmlInput: { "aria-label": `Modèle ${providerLabel[provider]}` } }}
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
			<SaveBar pending={save.isPending} saved={save.isSuccess} failed={save.isError} />
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
		<Section title="Notifications ntfy" onSubmit={submit}>
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
			<Box sx={{ overflowX: "auto" }}>
				<Table size="small" aria-label="Événements notifiés par provider" sx={{ minWidth: 430 }}>
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
			{current.lastSendFailure && (
				<Alert severity="warning">
					Dernier échec d'envoi {fmtWhen(current.lastSendFailure.at, now)} : {current.lastSendFailure.message}
				</Alert>
			)}
			{test.isSuccess && <Alert severity="success">Notification de test envoyée.</Alert>}
			{test.isError && <Alert severity="error">L'envoi de test a échoué.</Alert>}
			<Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ alignItems: { xs: "flex-start", sm: "center" } }}>
				<SaveBar pending={save.isPending} saved={save.isSuccess} failed={save.isError} />
				<Button variant="outlined" loading={test.isPending} disabled={!current.topic} onClick={() => test.mutate({})}>
					Envoyer un test
				</Button>
			</Stack>
		</Section>
	);
}
