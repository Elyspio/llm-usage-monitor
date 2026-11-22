import { Box, LinearProgress, Stack, Typography } from "@mui/material";
import type { MonthlyQuota, UsageWindow } from "@/core/apis/generated/types.gen";
import { monthPace } from "@/core/dashboard";
import { fmtIn, fmtPercent, fmtWhen, locale } from "@/core/format";

const mono = { fontFamily: "IBM Plex Mono, monospace" };
const count = (value: number) => Math.round(value).toLocaleString(locale);

/**
 * The monthly allowance of a monitored-only provider: share left, credits left, reset date and the pace of the month
 * (the share an even use would have consumed by now, drawn as a marker on the bar).
 */
export const MonthlyQuotaGauge = ({ quota, window, color, now }: { quota: MonthlyQuota; window: UsageWindow | undefined; color: string; now: number }) => {
	if (quota.unlimited) {
		return <Typography variant="body2">Unlimited AI credits on this plan.</Typography>;
	}

	const used = Math.max(0, Math.min(100, window?.usedPercent ?? (quota.entitlement > 0 ? 100 - (quota.remaining / quota.entitlement) * 100 : 100)));
	const remaining = 100 - used;
	const pace = monthPace(quota.resetsAt ?? window?.resetsAt ?? null, now);
	const status = remaining >= 50 ? "success.main" : remaining >= 20 ? "warning.main" : "error.main";
	const ahead = pace !== null && used > pace;

	return (
		<Stack spacing={0.75}>
			<Stack direction="row" spacing={1} sx={{ alignItems: "baseline", justifyContent: "space-between" }}>
				<Typography sx={{ fontWeight: 600 }}>AI credits this month</Typography>
				<Typography sx={{ ...mono, fontSize: "1.3rem", color: remaining < 50 ? status : "text.primary" }}>{fmtPercent(remaining)}</Typography>
			</Stack>
			<Box sx={{ position: "relative" }}>
				<LinearProgress
					variant="determinate"
					value={used}
					aria-label="AI credits used this month"
					sx={{ height: 10, borderRadius: 2, bgcolor: "surface.track", "& .MuiLinearProgress-bar": { bgcolor: color, borderRadius: 2 } }}
				/>
				{pace !== null && (
					<Box
						aria-hidden="true"
						title="Even use of the month"
						sx={{ position: "absolute", top: -3, height: 16, width: 2, bgcolor: "surface.now", left: `calc(${pace}% - 1px)` }}
					/>
				)}
			</Box>
			<Typography variant="body2" sx={{ ...mono, color: "text.secondary" }}>
				{count(quota.remaining)} / {count(quota.entitlement)} credits left
				{quota.plan ? ` · ${quota.plan} plan` : ""}
			</Typography>
			{quota.resetsAt && (
				<Typography variant="body2" sx={{ color: "text.secondary" }}>
					Resets {fmtWhen(quota.resetsAt, now)} ({fmtIn(quota.resetsAt, now)})
					{pace !== null && (
						<Box component="span" sx={{ color: ahead ? "warning.main" : "text.secondary" }}>
							{" "}
							· {ahead ? `${fmtPercent(used - pace)} ahead of an even pace` : "within an even pace"}
						</Box>
					)}
				</Typography>
			)}
			{quota.overageCount > 0 && (
				<Typography variant="body2" sx={{ color: "warning.main" }}>
					{count(quota.overageCount)} credits beyond the allowance{quota.overagePermitted ? " (billed)" : ""}.
				</Typography>
			)}
		</Stack>
	);
};
