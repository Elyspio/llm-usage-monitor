import BoltIcon from "@mui/icons-material/Bolt";
import { Alert, AlertTitle, Box, Chip, Paper, Stack, Typography } from "@mui/material";
import type { ProviderDashboard } from "@/core/apis/generated/types.gen";
import { errorInfo, isDegraded, providerColor, providerLabel } from "@/core/dashboard";
import { fmtAgo, fmtIn, fmtWhen } from "@/core/format";
import { ProviderLogo } from "./ProviderLogo";
import { TriggerButton } from "./TriggerButton";

export const ProviderColumn = ({ provider, now }: { provider: ProviderDashboard; now: number }) => {
	const { health, lastReading } = provider;
	const degraded = isDegraded(health);
	const failure = degraded ? health.lastFailure : null;
	const error = failure ? errorInfo(failure.code, provider.provider) : null;

	return (
		<Paper component="section" aria-label={providerLabel[provider.provider]} variant="outlined" sx={{ p: { xs: 2, lg: 3 }, height: "100%" }}>
			<Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ alignItems: "flex-start", justifyContent: "space-between", mb: 3 }}>
				<Box sx={{ minWidth: 0 }}>
					<Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
						<ProviderLogo provider={provider.provider} sx={{ color: providerColor[provider.provider], fontSize: 22, mr: 0.5 }} />
						<Typography variant="h6" component="h2" sx={{ fontWeight: 700 }}>
							{providerLabel[provider.provider]}
						</Typography>
						{error ? (
							<Chip size="small" color={error.severity} label={error.title} />
						) : (
							<Chip size="small" variant="outlined" color={lastReading ? "success" : "default"} label={lastReading ? "OK" : "En attente"} />
						)}
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

			<Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 0, p: 0, borderRadius: 2, bgcolor: "transparent", color: "text.secondary" }}>
				<BoltIcon fontSize="small" />
				<Typography variant="body2">{autoLine(provider, now)}</Typography>
			</Stack>
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
