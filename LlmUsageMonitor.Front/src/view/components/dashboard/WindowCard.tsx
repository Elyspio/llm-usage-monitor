import BoltIcon from "@mui/icons-material/Bolt";
import { Box, Card, CardContent, Chip, CircularProgress, LinearProgress, Stack, Typography } from "@mui/material";
import type { ReactNode } from "react";
import type { UsageWindow } from "@/core/apis/generated/types.gen";
import { remainingColor, windowLabel, windowTiming } from "@/core/dashboard";
import { fmtAgo, fmtDuration, fmtIn, fmtPercent, fmtWhen } from "@/core/format";

type WindowCardProps = {
	window: UsageWindow;
	now: number;
	isTrigger: boolean;
	/** The provider reading failed since: the values are the last valid ones. */
	stale: boolean;
	fetchedAt: string;
};

export const WindowCard = ({ window, now, isTrigger, stale, fetchedAt }: WindowCardProps) => {
	const timing = windowTiming(window, now);
	const expired = window.resetsAt != null && Date.parse(window.resetsAt) < now;
	const remaining = Math.max(0, 100 - window.usedPercent);

	return (
		<Card
			variant="outlined"
			aria-label={windowLabel(window)}
			sx={{ height: "100%", opacity: stale ? 0.6 : 1, borderStyle: stale ? "dashed" : "solid", borderColor: isTrigger ? "primary.main" : undefined }}
		>
			<CardContent>
				<Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: "center", mb: 1.5, flexWrap: "wrap" }}>
					<Typography variant={isTrigger ? "h6" : "subtitle1"} sx={{ fontWeight: 600 }}>
						{windowLabel(window)}
					</Typography>
					<Chip size="small" label={fmtDuration(window.windowDurationMinutes)} />
					{isTrigger && <Chip size="small" color="primary" variant="outlined" icon={<BoltIcon />} label="déclencheuse" />}
					{stale && <Chip size="small" color="warning" variant="outlined" label={`périmé · lu ${fmtAgo(fetchedAt, now)}`} />}
					{expired && <Chip size="small" color="warning" label="valeur caduque" />}
				</Stack>
				<Stack direction="row" spacing={2} sx={{ alignItems: "center" }}>
					<Gauge value={remaining} size={isTrigger ? 104 : 80} />
					<Box sx={{ minWidth: 0, flex: 1 }}>
						<Field label="Fin de fenêtre">
							{window.resetsAt == null ? (
								<Box sx={{ color: "text.secondary" }}>Non annoncée : fenêtre pas encore démarrée</Box>
							) : expired ? (
								<Box sx={{ color: "warning.main" }}>{fmtWhen(window.resetsAt, now)} · reset passé depuis la dernière lecture</Box>
							) : (
								<>
									{fmtWhen(window.resetsAt, now)}
									<Box sx={{ fontWeight: 700, fontVariantNumeric: "tabular-nums" }}>{fmtIn(window.resetsAt, now)}</Box>
								</>
							)}
						</Field>
						<Field label="Début de fenêtre">{timing ? fmtWhen(timing.start, now) : "—"}</Field>
						{timing && (
							<Box>
								<Typography variant="caption" sx={{ color: "text.secondary", display: "block" }}>
									Rythme : {fmtPercent(window.usedPercent)} consommé pour {fmtPercent(timing.elapsedPercent)} du temps écoulé
								</Typography>
								<LinearProgress
									variant="buffer"
									value={Math.min(100, window.usedPercent)}
									valueBuffer={timing.elapsedPercent}
									color={window.usedPercent > timing.elapsedPercent ? "warning" : "primary"}
									aria-label="temps écoulé"
									sx={{ mt: 0.5, height: 6, borderRadius: 3 }}
								/>
							</Box>
						)}
					</Box>
				</Stack>
			</CardContent>
		</Card>
	);
};

const Gauge = ({ value, size }: { value: number; size: number }) => (
	<Box sx={{ position: "relative", display: "inline-flex", flexShrink: 0 }}>
		<CircularProgress variant="determinate" value={100} size={size} thickness={5} sx={{ color: "action.hover", position: "absolute" }} />
		<CircularProgress variant="determinate" value={value} size={size} thickness={5} color={remainingColor(value)} aria-label="restant" />
		<Box sx={{ position: "absolute", inset: 0, display: "flex", alignItems: "center", justifyContent: "center", flexDirection: "column" }}>
			<Typography variant="h6" sx={{ fontWeight: 700, lineHeight: 1 }}>
				{fmtPercent(value)}
			</Typography>
			<Typography variant="caption" sx={{ color: "text.secondary" }}>
				restant
			</Typography>
		</Box>
	</Box>
);

const Field = ({ label, children }: { label: string; children: ReactNode }) => (
	<Box sx={{ mb: 1 }}>
		<Typography variant="caption" sx={{ color: "text.secondary", display: "block" }}>
			{label}
		</Typography>
		<Typography variant="body2" component="div">
			{children}
		</Typography>
	</Box>
);
