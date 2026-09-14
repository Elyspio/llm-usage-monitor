import BoltIcon from "@mui/icons-material/Bolt";
import { Alert, AlertTitle, Box, Chip, Grid, Paper, Stack, Typography } from "@mui/material";
import type { ProviderDashboard } from "@/core/apis/generated/types.gen";
import { errorInfo, isDegraded, providerColor, providerLabel, sortWindows } from "@/core/dashboard";
import { fmtAgo, fmtIn, fmtWhen } from "@/core/format";
import { TriggerButton } from "./TriggerButton";
import { WindowCard } from "./WindowCard";

export const ProviderColumn = ({ provider, now }: { provider: ProviderDashboard; now: number }) => {
	const { health, lastReading } = provider;
	const degraded = isDegraded(health);
	const failure = degraded ? health.lastFailure : null;
	const error = failure ? errorInfo(failure.code, provider.provider) : null;
	const windows = sortWindows(lastReading?.windows ?? [], provider.triggerWindowId);

	return (
		<Paper
			component="section"
			aria-label={providerLabel[provider.provider]}
			variant="outlined"
			sx={{ p: 2.5, height: "100%", borderTop: 4, borderTopColor: providerColor[provider.provider] }}
		>
			<Stack direction="row" spacing={2} sx={{ alignItems: "flex-start", justifyContent: "space-between", mb: 2 }}>
				<Box sx={{ minWidth: 0 }}>
					<Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
						<Typography variant="h6" component="h2" sx={{ fontWeight: 700 }}>
							{providerLabel[provider.provider]}
						</Typography>
						{error ? <Chip size="small" color={error.severity} label={error.title} /> : <Chip size="small" color="success" label="OK" />}
					</Stack>
					<Typography variant="body2" sx={{ color: "text.secondary", mt: 0.5 }}>
						{readingLine(provider, now)}
					</Typography>
				</Box>
				<TriggerButton provider={provider.provider} runningTrigger={provider.runningTrigger ?? null} />
			</Stack>

			{error && failure && (
				<Alert severity={error.severity} sx={{ mb: 2 }}>
					<AlertTitle>{error.title}</AlertTitle>
					{error.action}
					<Box sx={{ mt: 0.5 }}>
						{lastReading
							? `Valeurs affichées : dernière lecture valide ${fmtWhen(lastReading.fetchedAt, now)} (${fmtAgo(lastReading.fetchedAt, now)}).`
							: "Aucune lecture valide pour l'instant."}
					</Box>
					<Box component="details" sx={{ mt: 0.5 }}>
						<summary>Détail technique</summary>
						<code>
							{failure.code} · {failure.message}
						</code>
					</Box>
				</Alert>
			)}

			<Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 2, color: "text.secondary" }}>
				<BoltIcon fontSize="small" />
				<Typography variant="body2">{autoLine(provider, now)}</Typography>
			</Stack>

			{lastReading && windows.length > 0 ? (
				<Grid container spacing={2}>
					{windows.map((window) => (
						<Grid key={window.id} size={window.id === provider.triggerWindowId ? 12 : { xs: 12, sm: 6 }}>
							<WindowCard window={window} now={now} isTrigger={window.id === provider.triggerWindowId} stale={degraded} fetchedAt={lastReading.fetchedAt} />
						</Grid>
					))}
				</Grid>
			) : (
				<Box sx={{ border: "1px dashed", borderColor: "divider", borderRadius: 2, p: 2, color: "text.secondary" }}>
					<Typography variant="subtitle2" sx={{ fontWeight: 600, color: "text.primary" }}>
						Aucune donnée
					</Typography>
					<Typography variant="body2">Le service n'a encore obtenu aucune lecture valide pour ce provider.</Typography>
				</Box>
			)}
		</Paper>
	);
};

function readingLine(provider: ProviderDashboard, now: number): string {
	const { health, lastReading, pollIntervalMinutes } = provider;
	const read = lastReading ? `Lu ${fmtAgo(lastReading.fetchedAt, now)}` : "Jamais lu";
	if (health.backoffUntil) return `${read} · limité (429) : nouvel essai ${fmtIn(health.backoffUntil, now)}`;
	return `${read} · lecture toutes les ${pollIntervalMinutes} min`;
}

function autoLine(provider: ProviderDashboard, now: number): string {
	if (!provider.autoTriggerEnabled) return "Déclenchement automatique désactivé dans les réglages.";
	if (!provider.triggerWindowId) return "Déclenchement automatique en attente d'une première lecture.";
	if (provider.nextAutoTriggerAt) {
		return `Déclenchement automatique vérifié au prochain reset : ${fmtWhen(provider.nextAutoTriggerAt, now)} (${fmtIn(provider.nextAutoTriggerAt, now)}).`;
	}
	return "Déclenchement automatique dès que la fenêtre déclencheuse revient à 0 %.";
}
