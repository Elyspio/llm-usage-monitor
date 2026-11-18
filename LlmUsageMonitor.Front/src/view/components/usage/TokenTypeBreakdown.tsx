import { Box, Stack, Typography } from "@mui/material";
import { fmtMetric, fmtUsd, tokenTypeLabel, tokenTypes, type TokenType, type UsageMetric } from "@/core/usage";

/** A grey ramp, kept apart from the provider colours: the brightest type is the output, the costliest per token. */
const tokenTypeColor: Record<TokenType, string> = { input: "#71717a", cacheRead: "#52525b", cacheWrite: "#a1a1aa", output: "#f4f4f5" };

/** The cost or the tokens of the period split by token type: a segmented bar and its legend. */
export const TokenTypeBreakdown = ({ values, metric, savings }: { values: Record<TokenType, number>; metric: UsageMetric; savings: number }) => {
	const title = metric === "cost" ? "Cost by type" : "Tokens by type";
	const shown = tokenTypes.filter((type) => values[type] > 0);

	return (
		<Box component="section" aria-label={title}>
			<Stack direction="row" spacing={1} sx={{ justifyContent: "space-between", alignItems: "baseline", mb: 1 }}>
				<Typography component="h2" sx={{ fontWeight: 700, fontSize: "0.875rem" }}>
					{title}
				</Typography>
				{metric === "cost" && savings > 0 && (
					<Typography variant="caption" sx={{ color: "text.secondary" }}>
						Cache saved{" "}
						<Box component="span" sx={{ color: "primary.main", fontWeight: 700 }}>
							{fmtUsd(savings)}
						</Box>
					</Typography>
				)}
			</Stack>
			<Stack direction="row" aria-hidden="true" sx={{ gap: "3px", height: 8 }}>
				{shown.map((type) => (
					<Box key={type} sx={{ flex: `${values[type]} 1 0`, minWidth: 4, borderRadius: "4px", bgcolor: tokenTypeColor[type] }} />
				))}
			</Stack>
			<Box component="ul" sx={{ listStyle: "none", m: 0, p: 0, mt: 1, display: "grid", gridTemplateColumns: "repeat(2, minmax(0, 1fr))", columnGap: 3 }}>
				{tokenTypes.map((type) => (
					<Box component="li" key={type} sx={{ display: "flex", alignItems: "center", gap: 0.75 }}>
						<Box aria-hidden="true" sx={{ width: 8, height: 8, borderRadius: "2px", bgcolor: tokenTypeColor[type] }} />
						<Typography variant="body2" sx={{ color: "text.secondary", flex: 1 }}>
							{tokenTypeLabel[type]}
						</Typography>
						<Typography variant="body2" sx={{ fontWeight: 700, fontVariantNumeric: "tabular-nums" }}>
							{fmtMetric(values[type], metric)}
						</Typography>
					</Box>
				))}
			</Box>
		</Box>
	);
};
