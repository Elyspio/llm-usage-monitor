import { Alert, Button, CircularProgress, FormControlLabel, Paper, Stack, Switch, TextField, Typography } from "@mui/material";
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
		<Stack spacing={3} sx={{ maxWidth: 760 }}>
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
	<Paper component="form" aria-label={title} variant="outlined" noValidate onSubmit={onSubmit} sx={{ p: 2.5 }}>
		<Typography variant="h6" component="h2" sx={{ fontWeight: 700, mb: 2 }}>
			{title}
		</Typography>
		<Stack spacing={2}>{children}</Stack>
	</Paper>
);

const SaveBar = ({ pending, saved, failed }: { pending: boolean; saved: boolean; failed: boolean }) => (
	<Stack direction="row" spacing={2} sx={{ alignItems: "center" }}>
		<Button type="submit" variant="contained" loading={pending}>
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
			{providers.map((provider) => {
				const field = provider === "claude" ? "claudeIntervalMinutes" : "codexIntervalMinutes";
				return (
					<TextField
						key={provider}
						type="number"
						label={`Intervalle ${providerLabel[provider]} (minutes)`}
						value={values[field]}
						onChange={(event) => setValues({ ...values, [field]: Number(event.target.value) })}
						error={Boolean(errors[field])}
						helperText={errors[field] ?? "Entre 1 et 60 minutes. Un intervalle court augmente le risque de 429."}
						slotProps={{ htmlInput: { min: 1, max: 60 } }}
					/>
				);
			})}
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
			{providers.map((provider) => (
				<Stack key={provider} direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ alignItems: { sm: "center" } }}>
					<FormControlLabel
						sx={{ minWidth: 260 }}
						control={
							<Switch
								checked={values[provider].autoEnabled}
								onChange={(event) => setValues({ ...values, [provider]: { ...values[provider], autoEnabled: event.target.checked } })}
							/>
						}
						label={`${providerLabel[provider]} : automatique après reset`}
					/>
					<TextField
						fullWidth
						label={`Modèle ${providerLabel[provider]}`}
						value={values[provider].model}
						onChange={(event) => setValues({ ...values, [provider]: { ...values[provider], model: event.target.value } })}
						error={Boolean(errors[`${provider}.model`])}
						helperText={errors[`${provider}.model`] ?? "Modèle du prompt « 1+1=? »."}
					/>
				</Stack>
			))}
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
			<Stack>
				{(Object.keys(eventLabels) as (keyof NotificationEvents)[]).map((event) => (
					<FormControlLabel
						key={event}
						control={
							<Switch checked={values.events[event]} onChange={(change) => setValues({ ...values, events: { ...values.events, [event]: change.target.checked } })} />
						}
						label={eventLabels[event]}
					/>
				))}
			</Stack>
			{current.lastSendFailure && (
				<Alert severity="warning">
					Dernier échec d'envoi {fmtWhen(current.lastSendFailure.at, now)} : {current.lastSendFailure.message}
				</Alert>
			)}
			{test.isSuccess && <Alert severity="success">Notification de test envoyée.</Alert>}
			{test.isError && <Alert severity="error">L'envoi de test a échoué.</Alert>}
			<Stack direction="row" spacing={2} sx={{ alignItems: "center" }}>
				<SaveBar pending={save.isPending} saved={save.isSuccess} failed={save.isError} />
				<Button variant="outlined" loading={test.isPending} disabled={!current.topic} onClick={() => test.mutate({})}>
					Envoyer un test
				</Button>
			</Stack>
		</Section>
	);
}
