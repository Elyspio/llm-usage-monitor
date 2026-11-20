import { Box, Paper, Stack, Typography } from "@mui/material";
import type { ProviderDashboard } from "@/core/apis/generated/types.gen";
import { fableWindowId, isDegraded, nestedWindowLabel, providerColor, providerLabel, windowColor, windowLabel } from "@/core/dashboard";
import { fmtAgo, fmtIn, fmtPercent, fmtWhen, locale } from "@/core/format";
import { toTimeline } from "@/core/timeline";
import { ProviderLogo } from "./ProviderLogo";

const columns = "minmax(210px, 28%) minmax(0, 1fr) 100px";
const mono = { fontFamily: "IBM Plex Mono, monospace" };
const dateLabel = (value: number) => new Date(value).toLocaleDateString(locale, { day: "numeric", month: "short" });

export const UsageTimeline = ({ providers, now }: { providers: ProviderDashboard[]; now: number }) => {
	const { groups, ticks, mondays, position, start, end } = toTimeline(providers, now);
	return (
		<Paper component="section" aria-label="Quota timeline" variant="outlined" sx={{ p: 2, overflow: "hidden" }}>
			<Typography variant="overline" sx={{ display: { md: "none" }, color: "text.secondary" }}>
				Timeline · {dateLabel(start)} → {dateLabel(end - 1)}
			</Typography>
			<Box sx={{ display: { xs: "none", md: "grid" }, gridTemplateColumns: columns, gap: 2, alignItems: "end", mb: 0.5, pt: 2.25 }}>
				<Typography variant="overline" sx={{ color: "text.secondary" }}>
					Window
				</Typography>
				<Box sx={{ position: "relative", height: 28, borderBottom: 1, borderColor: "divider" }}>
					{mondays.map((monday) => (
						<Typography
							key={monday}
							variant="overline"
							title="Monday"
							sx={{ position: "absolute", left: `${position(monday)}%`, top: -18, transform: "translateX(-50%)", lineHeight: 1.4, color: "surface.monday" }}
						>
							M
						</Typography>
					))}
					{/* Drawn after the Monday labels, on the paper colour: "Now" stays readable on a Monday. */}
					<Typography
						variant="overline"
						sx={{
							position: "absolute",
							left: `${position(now)}%`,
							top: -18,
							transform: "translateX(-50%)",
							lineHeight: 1.4,
							px: 0.5,
							bgcolor: "background.paper",
							color: "primary.main",
						}}
					>
						Now
					</Typography>
					{ticks.map((tick) => (
						<Box key={tick} sx={{ position: "absolute", left: `${position(tick)}%`, bottom: 0, height: 6, borderLeft: 1, borderColor: "divider" }}>
							<Typography variant="caption" sx={{ ...mono, position: "absolute", bottom: 8, color: "text.secondary" }}>
								{new Date(tick).getDate()}
							</Typography>
						</Box>
					))}
				</Box>
				<Typography variant="overline" align="right" sx={{ color: "text.secondary" }}>
					Remaining
				</Typography>
			</Box>
			{groups.length === 0 && <Typography sx={{ py: 3, color: "text.secondary" }}>No data. Waiting for a first reading.</Typography>}
			{groups.map(({ provider, rows: windows }, groupIndex) => {
				const stale = isDegraded(provider.health);
				return (
					<Box key={provider.provider} component="section" aria-label={`${providerLabel[provider.provider]} windows`}>
						<Stack direction="row" spacing={1} sx={{ alignItems: "center", pt: groupIndex === 0 ? 1.5 : 2 }}>
							<ProviderLogo provider={provider.provider} sx={{ fontSize: 18, color: stale ? "surface.muted" : providerColor[provider.provider] }} />
							<Typography component="h3" sx={{ fontWeight: 700, fontSize: "1rem" }}>
								{providerLabel[provider.provider]}
							</Typography>
							{stale && (
								<Typography variant="caption" sx={{ color: "warning.main" }}>
									stale · read {fmtAgo(provider.lastReading!.fetchedAt, now)}
								</Typography>
							)}
						</Stack>
						{windows.map(({ window, timing, nested }, index) => {
							const expired = window.resetsAt != null && Date.parse(window.resetsAt) < now;
							const remaining = Math.max(0, Math.min(100, 100 - window.usedPercent));
							const status = stale || expired ? "surface.muted" : remaining >= 50 ? "success.main" : remaining >= 20 ? "warning.main" : "error.main";
							const color = stale || expired ? "surface.muted" : windowColor(provider.provider, window.id);
							const validTiming = timing && Number.isFinite(timing.start) && Number.isFinite(timing.end) ? timing : null;
							const left = validTiming ? position(validTiming.start) : 0;
							const width = validTiming ? position(validTiming.end) - left : 0;
							// A nested window is thinner and stays attached to its parent: no rule between them.
							const bar = nested ? 8 : 14;
							const hasChild = windows[index + 1]?.nested === true;
							return (
								<Box
									key={window.id}
									sx={{
										display: "grid",
										gridTemplateColumns: { xs: "minmax(0, 1fr) auto", md: columns },
										columnGap: 2,
										rowGap: 1,
										alignItems: "center",
										pt: nested ? 0 : index === 0 ? 1 : 1.5,
										pb: hasChild ? 1 : 1.5,
										borderBottom: hasChild ? 0 : 1,
										borderColor: "divider",
									}}
								>
									<Box sx={{ display: "flex", pl: 3.25 }}>
										{nested && (
											<Box
												aria-hidden="true"
												sx={{
													flex: "none",
													width: 12,
													height: 12,
													mr: 1.25,
													borderLeft: 1,
													borderBottom: 1,
													borderColor: "text.secondary",
													borderBottomLeftRadius: 4,
												}}
											/>
										)}
										<Box sx={{ minWidth: 0 }}>
											<Typography sx={{ fontWeight: nested ? 500 : 600, fontSize: { xs: "0.9rem", lg: nested ? "0.9rem" : "0.95rem" } }}>
												{(nested && nestedWindowLabel[window.id]) || windowLabel(window)}
											</Typography>
											{nested ? (
												<Typography variant="body2" sx={{ color: "text.secondary", mt: 0.25 }}>
													Counts within the weekly limit
												</Typography>
											) : (
												<Typography variant="body2" sx={{ ...mono, color: "text.secondary", mt: 0.25 }}>
													{validTiming
														? `${fmtWhen(validTiming.start, now)} → ${fmtWhen(validTiming.end, now)}`
														: window.resetsAt
															? `Reset ${fmtWhen(window.resetsAt, now)}`
															: "Window not started yet"}
												</Typography>
											)}
											{expired && (
												<Typography variant="caption" sx={{ color: "warning.main", display: "block" }}>
													Reset passed · waiting for a reading
												</Typography>
											)}
										</Box>
									</Box>
									<Box sx={{ position: "relative", height: 28, gridColumn: { xs: "1 / -1", md: "auto" }, gridRow: { xs: 2, md: "auto" } }}>
										<Box sx={{ position: "absolute", top: 10, height: 8, width: "100%", bgcolor: "background.default", borderRadius: 2 }} />
										{validTiming ? (
											<Box
												component="progress"
												max={100}
												value={Math.max(0, Math.min(100, window.usedPercent))}
												aria-label={`${providerLabel[provider.provider]} ${windowLabel(window)} used`}
												aria-valuemin={0}
												aria-valuemax={100}
												aria-valuenow={Math.max(0, Math.min(100, window.usedPercent))}
												sx={{
													position: "absolute",
													top: 14 - bar / 2,
													left: `${left}%`,
													width: `${width}%`,
													minWidth: 2,
													height: bar,
													bgcolor: "surface.track",
													appearance: "none",
													border: 0,
													"&::-webkit-progress-bar": { backgroundColor: "surface.track" },
													"&::-webkit-progress-value": { backgroundColor: color },
													"&::-moz-progress-bar": { backgroundColor: color },
													borderRadius: "4px",
													overflow: "hidden",
													outlineWidth: 1,
													outlineStyle: width < 4 ? "solid" : "none",
													outlineColor: color,
												}}
											>
												{fmtPercent(window.usedPercent)}
											</Box>
										) : (
											<Typography variant="caption" sx={{ position: "relative", bgcolor: "background.paper", color: "text.secondary", pr: 1 }}>
												Duration unavailable
											</Typography>
										)}
										{mondays.map((monday) => (
											<Box
												key={monday}
												aria-hidden="true"
												sx={{ position: "absolute", top: 2, height: 24, width: 2, bgcolor: "surface.monday", left: `${position(monday)}%` }}
											/>
										))}
										<Box aria-hidden="true" sx={{ position: "absolute", top: 2, height: 24, width: 2, bgcolor: "surface.now", left: `${position(now)}%` }} />
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
											fontSize: nested ? "1.05rem" : "1.3rem",
											gridColumn: { xs: 2, md: "auto" },
											gridRow: { xs: 1, md: "auto" },
										}}
									>
										{fmtPercent(remaining)}
									</Typography>
								</Box>
							);
						})}
					</Box>
				);
			})}
			<Stack direction="row" useFlexGap spacing={2} sx={{ flexWrap: "wrap", pt: 1.5, color: "text.secondary" }}>
				<Typography variant="body2">
					<Box
						component="span"
						sx={{
							display: "inline-block",
							width: 20,
							height: 10,
							background: `linear-gradient(90deg, ${windowColor("claude", "five_hour")} 0 25%, ${providerColor.claude} 25% 50%, ${windowColor("claude", fableWindowId)} 50% 75%, ${providerColor.codex} 75%)`,
							borderRadius: "3px",
							mr: 1,
						}}
					/>
					quota used
				</Typography>
				<Typography variant="body2">
					<Box component="span" sx={{ display: "inline-block", width: 20, height: 10, bgcolor: "surface.track", borderRadius: "3px", mr: 1 }} />
					open window
				</Typography>
				<Typography variant="body2">
					<Box component="span" sx={{ display: "inline-block", width: 2, height: 14, bgcolor: "surface.now", mr: 1, verticalAlign: "middle" }} />
					now
				</Typography>
				<Typography variant="body2">
					<Box component="span" sx={{ display: "inline-block", width: 2, height: 14, bgcolor: "surface.monday", mr: 1, verticalAlign: "middle" }} />
					Monday
				</Typography>
			</Stack>
		</Paper>
	);
};
