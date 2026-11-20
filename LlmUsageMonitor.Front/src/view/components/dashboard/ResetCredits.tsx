import ArrowDropDownIcon from "@mui/icons-material/ArrowDropDown";
import RestartAltIcon from "@mui/icons-material/RestartAlt";
import {
	Accordion,
	AccordionDetails,
	AccordionSummary,
	Alert,
	Box,
	Button,
	Chip,
	Dialog,
	DialogActions,
	DialogContent,
	DialogTitle,
	Menu,
	MenuItem,
	Stack,
	Typography,
} from "@mui/material";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { apiErrorMessage } from "@/core/apis/api-error";
import { consumeResetCreditMutation, getDashboardQueryKey, getHistoryQueryKey } from "@/core/apis/generated/@tanstack/react-query.gen";
import type { Provider, ProviderDashboard, ResetCredit } from "@/core/apis/generated/types.gen";
import { providerLabel, windowColor, windowLabel } from "@/core/dashboard";
import { fmtIn, fmtWhen } from "@/core/format";

/** A reset is used from the end of its line, by a button no taller than the text beside it. */
const action = { flex: "none", px: 1.5, py: 0.5, fontSize: "0.8125rem", whiteSpace: "nowrap" };
const line = { alignItems: "center", justifyContent: "space-between" };

/** The quotas a reset restores, each marked with the colour its window has on the timeline and in the history. */
const RestoredWindows = ({ provider, windowIds }: { provider: Provider; windowIds: string[] }) =>
	windowIds.length === 0 ? null : (
		<Stack component="ul" aria-label="Restores" direction="row" useFlexGap sx={{ flexWrap: "wrap", columnGap: 1.5, rowGap: 0.25, m: 0, p: 0, listStyle: "none" }}>
			{windowIds.map((id) => (
				<Typography key={id} component="li" variant="caption" sx={{ display: "flex", alignItems: "center", gap: 0.75, whiteSpace: "nowrap" }}>
					<Box aria-hidden="true" sx={{ width: 8, height: 8, borderRadius: "2px", bgcolor: windowColor(provider, id) }} />
					{windowLabel({ id, windowDurationMinutes: null })}
				</Typography>
			))}
		</Stack>
	);

export function ResetCredits({ provider, now }: { provider: ProviderDashboard; now: number }) {
	const data = provider.resetCredits;
	const queryClient = useQueryClient();
	const [selected, setSelected] = useState<ResetCredit | null>(null);
	const [requestKey, setRequestKey] = useState<string | null>(null);
	const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);
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
	// Several usable resets restore different quotas (a full reset, a Fable-only one): the button asks which one.
	const usable = credits.filter(available);
	const restores = (credit: ResetCredit) => credit.windowIds.map((id) => windowLabel({ id, windowDurationMinutes: null })).join(", ");
	const open = (credit: ResetCredit) => {
		setMenuAnchor(null);
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
		<Box sx={{ mt: 1.5, pt: 1.5, borderTop: 1, borderColor: "divider" }}>
			<Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 0.5 }}>
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
				<Stack direction="row" spacing={1.5} sx={{ ...line, mt: 1, px: 1.5, py: 1, bgcolor: "background.default", borderRadius: "8px" }}>
					<Box sx={{ minWidth: 0 }}>
						<Typography variant="body2" sx={{ fontWeight: 600 }}>
							{next.title ?? "Reset"}
						</Typography>
						<Typography variant="caption" component="p" color="text.secondary">
							Next selected, {next.expiresAt ? `expires ${fmtWhen(next.expiresAt, now)} (${fmtIn(next.expiresAt, now)})` : "expiry unknown, manual use only"}.
						</Typography>
					</Box>
					<Button
						variant="outlined"
						color="inherit"
						size="small"
						disabled={busy || usable.length === 0}
						onClick={(event) => (usable.length > 1 ? setMenuAnchor(event.currentTarget) : open(usable[0]))}
						endIcon={usable.length > 1 ? <ArrowDropDownIcon /> : undefined}
						aria-haspopup={usable.length > 1 ? "menu" : undefined}
						aria-expanded={usable.length > 1 ? menuAnchor !== null : undefined}
						sx={action}
					>
						Use reset
					</Button>
					<Menu
						anchorEl={menuAnchor}
						open={menuAnchor !== null}
						onClose={() => setMenuAnchor(null)}
						anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
						transformOrigin={{ vertical: "top", horizontal: "right" }}
						slotProps={{ list: { "aria-label": "Reset to use" } }}
					>
						{usable.map((credit, index) => (
							<MenuItem key={credit.id} onClick={() => open(credit)} sx={{ maxWidth: 440, whiteSpace: "normal" }}>
								<Stack spacing={0.75} sx={{ py: 0.5 }}>
									<Typography variant="body2" sx={{ fontWeight: 600 }}>
										{credit.title ?? `Reset ${index + 1}`}
									</Typography>
									<RestoredWindows provider={provider.provider} windowIds={credit.windowIds} />
									<Typography variant="caption" color="text.secondary">
										{credit.expiresAt ? `Expires ${fmtWhen(credit.expiresAt, now)} (${fmtIn(credit.expiresAt, now)})` : "No known expiration"}
									</Typography>
								</Stack>
							</MenuItem>
						))}
					</Menu>
				</Stack>
			)}
			{data?.nextAttemptAt && (
				<Typography variant="caption" component="p" color="text.secondary" sx={{ mt: 1 }}>
					Next attempt: {fmtWhen(data.nextAttemptAt, now)}
					{next && !next.isUsable ? " · waiting for provider eligibility" : ""}.
				</Typography>
			)}
			{data?.balance?.unavailableReason && (
				<Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
					The provider marks these resets as unavailable (reason: {data.balance.unavailableReason}).
				</Typography>
			)}
			{(data?.balance?.availableCount ?? 0) > credits.reduce((sum, credit) => sum + credit.remainingUses, 0) && (
				<Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
					Only some credit details are available. Automatic selection requires a known expiration.
				</Typography>
			)}
			<Accordion disableGutters elevation={0} sx={{ mt: 1, bgcolor: "transparent", "&:before": { display: "none" } }}>
				<AccordionSummary sx={{ px: 0, minHeight: 32, "& .MuiAccordionSummary-content": { my: 0.75 } }}>
					<Typography variant="body2">Reset details and history</Typography>
				</AccordionSummary>
				<AccordionDetails sx={{ px: 0, pt: 0.5, pb: 0 }}>
					<Stack spacing={1.25}>
						{credits.map((credit, index) => (
							<Stack key={credit.id} direction="row" spacing={1.5} sx={line}>
								<Box sx={{ minWidth: 0 }}>
									<Typography variant="body2" sx={{ fontWeight: 600 }}>
										{credit.title ?? `Reset ${index + 1}`} · {credit.remainingUses} remaining{credit.id === data?.nextCreditId ? " · next selected" : ""}
									</Typography>
									<Typography variant="caption" color="text.secondary">
										{credit.expiresAt ? `Expires ${fmtWhen(credit.expiresAt, now)}` : "No known expiration"}
										{credit.requiresLimit ? " · requires an exhausted limit" : ""}
										{!credit.isUsable ? " · currently unavailable" : ""}
									</Typography>
									<Box sx={{ mt: 0.5 }}>
										<RestoredWindows provider={provider.provider} windowIds={credit.windowIds} />
									</Box>
								</Box>
								<Button
									variant="outlined"
									color="inherit"
									size="small"
									disabled={busy || !available(credit)}
									onClick={() => open(credit)}
									aria-label={`Use reset ${index + 1}`}
									sx={action}
								>
									Use reset
								</Button>
							</Stack>
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
							Restores {selected?.windowIds.length ? restores(selected) : "the provider's eligible quotas"}. This consumes one earned reset and cannot be undone. No
							prompt will be sent.
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
