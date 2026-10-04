import RestartAltIcon from "@mui/icons-material/RestartAlt";
import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Stack, Typography } from "@mui/material";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { apiErrorMessage } from "@/core/apis/api-error";
import { consumeResetCreditMutation, getDashboardQueryKey, getHistoryQueryKey } from "@/core/apis/generated/@tanstack/react-query.gen";
import type { ProviderDashboard, ResetCredit } from "@/core/apis/generated/types.gen";
import { providerLabel, windowLabel } from "@/core/dashboard";
import { fmtIn, fmtWhen } from "@/core/format";

export function ResetCredits({ provider, now }: { provider: ProviderDashboard; now: number }) {
	const data = provider.resetCredits;
	const queryClient = useQueryClient();
	const [selected, setSelected] = useState<ResetCredit | null>(null);
	const [requestKey, setRequestKey] = useState<string | null>(null);
	const consume = useMutation({
		...consumeResetCreditMutation(),
		onSuccess: () => setSelected(null),
		onSettled: () => {
			void queryClient.invalidateQueries({ queryKey: getDashboardQueryKey() });
			void queryClient.invalidateQueries({ queryKey: getHistoryQueryKey() });
		},
	});
	const credits = (data?.balance?.credits ?? []).toSorted((a, b) => (a.expiresAt ? Date.parse(a.expiresAt) : Infinity) - (b.expiresAt ? Date.parse(b.expiresAt) : Infinity));
	const next = credits.find((credit) => credit.id === data?.nextCreditId);
	const busy = consume.isPending || Boolean(provider.runningTrigger);
	const available = (credit: ResetCredit) =>
		credit.isUsable && credit.remainingUses > 0 && (!credit.expiresAt || Date.parse(credit.expiresAt) > now) && (!credit.startsAt || Date.parse(credit.startsAt) <= now);
	const open = (credit: ResetCredit) => {
		setSelected(credit);
		setRequestKey(data?.recentRuns.find((run) => run.creditId === credit.id && run.status === "running")?.id ?? null);
		consume.reset();
	};
	const confirm = () => {
		if (!selected || !available(selected)) return;
		const key = requestKey ?? crypto.randomUUID();
		setRequestKey(key);
		consume.mutate({ path: { provider: provider.provider }, body: { creditId: selected.id, idempotencyKey: key } });
	};
	return (
		<Box sx={{ mt: 3, pt: 2.5, borderTop: 1, borderColor: "divider" }}>
			<Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 1 }}>
				<RestartAltIcon fontSize="small" />
				<Typography component="h3" variant="subtitle2">
					Earned resets
				</Typography>
				<Chip size="small" label={data?.balance?.availableCount == null ? "Unknown availability" : `${data.balance.availableCount} available`} />
			</Stack>
			<Typography variant="body2" color="text.secondary">
				{data?.settings.autoEnabled ? `Automatic use ${data.settings.beforeExpiryMinutes} min before expiry.` : "Automatic use disabled."}
			</Typography>
			{next && (
				<Typography variant="body2" sx={{ mt: 1 }}>
					Next selected: {next.title ?? "Reset"} ·{" "}
					{next.expiresAt ? `expires ${fmtWhen(next.expiresAt, now)} (${fmtIn(next.expiresAt, now)})` : "expiry unknown, manual use only"}.
				</Typography>
			)}
			{data?.nextAttemptAt && (
				<Typography variant="caption" color="text.secondary">
					Next attempt: {fmtWhen(data.nextAttemptAt, now)}
					{next && !next.isUsable ? " · waiting for provider eligibility" : ""}.
				</Typography>
			)}
			{data?.balance?.unavailableReason && (
				<Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
					The provider currently marks these resets as unavailable.
				</Typography>
			)}
			{(data?.balance?.availableCount ?? 0) > credits.reduce((sum, credit) => sum + credit.remainingUses, 0) && (
				<Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
					Only some credit details are available. Automatic selection requires a known expiration.
				</Typography>
			)}
			{next && (
				<Button variant="outlined" color="inherit" size="small" disabled={busy || !available(next)} onClick={() => open(next)} sx={{ mt: 1.5 }}>
					Use selected reset
				</Button>
			)}
			<Accordion disableGutters elevation={0} sx={{ mt: 1.5, bgcolor: "transparent", "&:before": { display: "none" } }}>
				<AccordionSummary sx={{ px: 0, minHeight: 36 }}>
					<Typography variant="body2">Reset details and history</Typography>
				</AccordionSummary>
				<AccordionDetails sx={{ px: 0 }}>
					<Stack spacing={2}>
						{credits.map((credit, index) => (
							<Box key={credit.id}>
								<Typography variant="body2" sx={{ fontWeight: 600 }}>
									{credit.title ?? `Reset ${index + 1}`} · {credit.remainingUses} remaining{credit.id === data?.nextCreditId ? " · next selected" : ""}
								</Typography>
								<Typography variant="caption" color="text.secondary">
									{credit.expiresAt ? `Expires ${fmtWhen(credit.expiresAt, now)}` : "No known expiration"}
									{credit.requiresLimit ? " · requires an exhausted limit" : ""}
									{!credit.isUsable ? " · currently unavailable" : ""}
								</Typography>
								{credit.windowIds.length > 0 && (
									<Typography variant="caption" component="p">
										Restores: {credit.windowIds.map((id) => windowLabel({ id, windowDurationMinutes: null })).join(", ")}
									</Typography>
								)}
								<Button size="small" disabled={busy || !available(credit)} onClick={() => open(credit)} aria-label={`Use reset ${index + 1}`}>
									Use this reset
								</Button>
							</Box>
						))}
						{credits.length === 0 && (
							<Typography variant="body2" color="text.secondary">
								{data?.balance?.credits == null ? "Credit details are not available." : "No reset credits available."}
							</Typography>
						)}
						{data?.recentRuns.map((run) => (
							<Box key={run.id}>
								<Typography variant="body2">
									{run.manual ? "Manual" : "Before expiry"} · {run.status} · {fmtWhen(run.startedAt, now)}
								</Typography>
								<Typography variant="caption" color="text.secondary">
									{outcomeLabel(run.outcome)}
									{run.nextRetryAt ? ` · retry ${fmtWhen(run.nextRetryAt, now)}` : ""}
								</Typography>
								{run.before?.map((before) => (
									<Typography key={before.id} variant="caption" component="p">
										{windowLabel(before)}: {before.usedPercent}% → {run.after?.find((after) => after.id === before.id)?.usedPercent ?? "?"}% used
									</Typography>
								))}
							</Box>
						))}
					</Stack>
				</AccordionDetails>
			</Accordion>
			{consume.isSuccess && (
				<Alert severity={consume.data.status === "succeeded" ? "success" : consume.data.status === "running" ? "info" : "warning"} sx={{ mt: 1 }}>
					Reset request {consume.data.status}.{consume.data.status === "running" ? " The same request will be retried automatically." : ""}
				</Alert>
			)}
			<Dialog
				open={selected !== null}
				onClose={() => {
					if (!consume.isPending) setSelected(null);
				}}
				fullWidth
				maxWidth="xs"
			>
				<DialogTitle>Use {providerLabel[provider.provider]} reset?</DialogTitle>
				<DialogContent>
					<Stack spacing={1.5}>
						<Typography>{selected?.title ?? "Selected reset"}</Typography>
						<Typography variant="body2">{selected?.expiresAt ? `Expires ${fmtWhen(selected.expiresAt, now)}.` : "No known expiration."}</Typography>
						<Typography variant="body2">
							Restores{" "}
							{selected?.windowIds.length
								? selected.windowIds.map((id) => windowLabel({ id, windowDurationMinutes: null })).join(", ")
								: "the provider's eligible quotas"}
							. This consumes one earned reset and cannot be undone. No prompt will be sent.
						</Typography>
						{consume.isError && <Alert severity="error">{apiErrorMessage(consume.error)}</Alert>}
					</Stack>
				</DialogContent>
				<DialogActions>
					<Button disabled={consume.isPending} onClick={() => setSelected(null)}>
						Cancel
					</Button>
					<Button variant="contained" loading={consume.isPending} disabled={!selected || !available(selected)} onClick={confirm}>
						{consume.isError ? "Retry same request" : "Confirm reset"}
					</Button>
				</DialogActions>
			</Dialog>
		</Box>
	);
}

function outcomeLabel(outcome?: string | null): string {
	switch (outcome) {
		case "reset":
			return "Quota restored";
		case "alreadyRedeemed":
		case "already_used":
			return "Reset already applied";
		case "nothingToReset":
		case "not_limited":
			return "No eligible quota to restore";
		case "noCredit":
			return "No credit available";
		case "ineligible":
			return "Not currently eligible";
		case "cooldown":
			return "Waiting for the provider cooldown";
		case "expiredOrUnconfirmed":
			return "Expired; consumption could not be confirmed";
		case null:
		case undefined:
			return "Request in progress";
		default:
			return "Provider request failed; see the service logs";
	}
}
