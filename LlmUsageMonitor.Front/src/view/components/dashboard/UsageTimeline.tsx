import { Box, Paper, Stack, Typography } from "@mui/material";
import type { ProviderDashboard } from "@/core/apis/generated/types.gen";
import { isDegraded, providerColor, providerLabel, windowColor, windowLabel } from "@/core/dashboard";
import { fmtAgo, fmtIn, fmtPercent, fmtWhen } from "@/core/format";
import { toTimeline } from "@/core/timeline";

const columns = "minmax(210px, 28%) minmax(0, 1fr) 100px";
const mono = { fontFamily: "IBM Plex Mono, monospace" };
const mondayColor = "#7dd3fc";
const dateLabel = (value: number) => new Date(value).toLocaleDateString("fr-FR", { day: "numeric", month: "short" });

export const UsageTimeline = ({ providers, now }: { providers: ProviderDashboard[]; now: number }) => {
	const { rows, ticks, mondays, position, start, end } = toTimeline(providers, now);
	return (
		<Paper component="section" aria-label="Chronologie des quotas" variant="outlined" sx={{ p: { xs: 2, md: 3.5 }, overflow: "hidden" }}>
			<Typography variant="overline" sx={{ display: { md: "none" }, color: "text.secondary" }}>
				Chronologie · {dateLabel(start)} → {dateLabel(end - 1)}
			</Typography>
			<Box sx={{ display: { xs: "none", md: "grid" }, gridTemplateColumns: columns, gap: 3, alignItems: "end", mb: 0.5, pt: 3 }}>
				<Typography variant="overline" color="text.secondary">
					Fenêtre
				</Typography>
				<Box sx={{ position: "relative", height: 34, borderBottom: 1, borderColor: "divider" }}>
					{mondays.map((monday) => (
						<Typography
							key={monday}
							variant="overline"
							title="Lundi"
							sx={{ position: "absolute", left: `${position(monday)}%`, top: -25, transform: "translateX(-50%)", color: mondayColor }}
						>
							L
						</Typography>
					))}
					{/* Drawn after the Monday labels, on the paper colour: « Maintenant » stays readable on a Monday. */}
					<Typography
						variant="overline"
						sx={{
							position: "absolute",
							left: `${position(now)}%`,
							top: -25,
							transform: "translateX(-50%)",
							px: 0.5,
							bgcolor: "background.paper",
							color: "primary.main",
						}}
					>
						Maintenant
					</Typography>
					{ticks.map((tick) => (
						<Box key={tick} sx={{ position: "absolute", left: `${position(tick)}%`, bottom: 0, height: 8, borderLeft: 1, borderColor: "divider" }}>
							<Typography variant="caption" sx={{ ...mono, position: "absolute", bottom: 13, color: "text.secondary" }}>
								{new Date(tick).getDate()}
							</Typography>
						</Box>
					))}
				</Box>
				<Typography variant="overline" color="text.secondary" align="right">
					Restant
				</Typography>
			</Box>
			{rows.length === 0 && <Typography sx={{ py: 5, color: "text.secondary" }}>Aucune donnée. En attente d'une première lecture.</Typography>}
			{rows.map(({ provider, window, timing }) => {
				const stale = isDegraded(provider.health);
				const expired = window.resetsAt != null && Date.parse(window.resetsAt) < now;
				const remaining = Math.max(0, Math.min(100, 100 - window.usedPercent));
				const status = stale || expired ? "#85858e" : remaining >= 50 ? "#10b981" : remaining >= 20 ? "#fbbf24" : "#fb7185";
				const color = stale || expired ? "#85858e" : windowColor(provider.provider, window.id);
				const validTiming = timing && Number.isFinite(timing.start) && Number.isFinite(timing.end) ? timing : null;
				const left = validTiming ? position(validTiming.start) : 0;
				const width = validTiming ? position(validTiming.end) - left : 0;
				return (
					<Box
						key={`${provider.provider}:${window.id}`}
						sx={{
							display: "grid",
							gridTemplateColumns: { xs: "minmax(0, 1fr) auto", md: columns },
							columnGap: 3,
							rowGap: 2,
							alignItems: "center",
							py: { xs: 3, md: 3.5 },
							borderBottom: 1,
							borderColor: "divider",
						}}
					>
						<Box>
							<Typography sx={{ fontWeight: 600, fontSize: { xs: "0.9rem", lg: "1rem" } }}>
								{providerLabel[provider.provider]} · {windowLabel(window)}
							</Typography>
							<Typography variant="body2" sx={{ ...mono, color: "text.secondary", mt: 0.75 }}>
								{validTiming
									? `${fmtWhen(validTiming.start, now)} → ${fmtWhen(validTiming.end, now)}`
									: window.resetsAt
										? `Reset ${fmtWhen(window.resetsAt, now)}`
										: "Fenêtre pas encore démarrée"}
							</Typography>
							{stale && (
								<Typography variant="caption" color="warning.main">
									périmé · lu {fmtAgo(provider.lastReading!.fetchedAt, now)}
								</Typography>
							)}
							{expired && (
								<Typography variant="caption" color="warning.main" sx={{ display: "block" }}>
									Reset passé · en attente de lecture
								</Typography>
							)}
						</Box>
						<Box sx={{ position: "relative", height: 38, gridColumn: { xs: "1 / -1", md: "auto" }, gridRow: { xs: 2, md: "auto" } }}>
							<Box sx={{ position: "absolute", top: 14, height: 10, width: "100%", bgcolor: "background.default", borderRadius: 2 }} />
							{validTiming ? (
								<Box
									component="progress"
									max={100}
									value={Math.max(0, Math.min(100, window.usedPercent))}
									aria-label={`${providerLabel[provider.provider]} ${windowLabel(window)} consommé`}
									aria-valuemin={0}
									aria-valuemax={100}
									aria-valuenow={Math.max(0, Math.min(100, window.usedPercent))}
									sx={{
										position: "absolute",
										top: 11,
										left: `${left}%`,
										width: `${width}%`,
										minWidth: 2,
										height: 16,
										bgcolor: "#28282b",
										appearance: "none",
										border: 0,
										"&::-webkit-progress-bar": { backgroundColor: "#28282b" },
										"&::-webkit-progress-value": { backgroundColor: color },
										"&::-moz-progress-bar": { backgroundColor: color },
										borderRadius: "4px",
										overflow: "hidden",
										outline: width < 4 ? `1px solid ${color}` : undefined,
									}}
								>
									{fmtPercent(window.usedPercent)}
								</Box>
							) : (
								<Typography variant="caption" sx={{ position: "relative", bgcolor: "background.paper", color: "text.secondary", pr: 1 }}>
									Durée non disponible
								</Typography>
							)}
							{mondays.map((monday) => (
								<Box
									key={monday}
									aria-hidden="true"
									sx={{ position: "absolute", top: 4, height: 30, width: 2, bgcolor: mondayColor, left: `${position(monday)}%` }}
								/>
							))}
							<Box aria-label="Maintenant" sx={{ position: "absolute", top: 4, height: 30, width: 2, bgcolor: "#d4d4d8", left: `${position(now)}%` }} />
							{validTiming && width < 5 && !expired && (
								<Typography variant="caption" sx={{ ...mono, position: "absolute", top: -13, right: 0, color: "text.secondary" }}>
									reset {fmtIn(window.resetsAt!, now)}
								</Typography>
							)}
						</Box>
						<Typography
							sx={{
								...mono,
								textAlign: "right",
								color: remaining < 50 || stale || expired ? status : "text.primary",
								fontSize: "1.45rem",
								gridColumn: { xs: 2, md: "auto" },
								gridRow: { xs: 1, md: "auto" },
							}}
						>
							{fmtPercent(remaining)}
						</Typography>
					</Box>
				);
			})}
			<Stack direction="row" useFlexGap spacing={3} sx={{ flexWrap: "wrap", pt: 2, color: "text.secondary" }}>
				<Typography variant="body2">
					<Box
						component="span"
						sx={{
							display: "inline-block",
							width: 20,
							height: 10,
							background: `linear-gradient(90deg, ${windowColor("claude", "five_hour")} 0 33%, ${providerColor.claude} 33% 66%, ${providerColor.codex} 66%)`,
							borderRadius: "3px",
							mr: 1,
						}}
					/>
					quota consommé
				</Typography>
				<Typography variant="body2">
					<Box component="span" sx={{ display: "inline-block", width: 20, height: 10, bgcolor: "#28282b", borderRadius: "3px", mr: 1 }} />
					fenêtre ouverte
				</Typography>
				<Typography variant="body2">
					<Box component="span" sx={{ display: "inline-block", width: 2, height: 14, bgcolor: "#d4d4d8", mr: 1, verticalAlign: "middle" }} />
					maintenant
				</Typography>
				<Typography variant="body2">
					<Box component="span" sx={{ display: "inline-block", width: 2, height: 14, bgcolor: mondayColor, mr: 1, verticalAlign: "middle" }} />
					lundi
				</Typography>
			</Stack>
		</Paper>
	);
};
