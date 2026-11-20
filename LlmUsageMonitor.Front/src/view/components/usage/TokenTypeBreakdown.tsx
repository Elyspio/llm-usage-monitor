import { Box, Stack, Typography } from "@mui/material";
import { fmtMetric, fmtUsd, tokenTypeLabel, tokenTypes, type TokenType, type UsageMetric } from "@/core/usage";

/** Cool pastel hues, kept apart from the provider, primary and warning colours; the output stays the brightest, the costliest per token. */
export const tokenTypeColor: Record<TokenType, string> = { input: "#7dd3fc", cacheRead: "#818cf8", cacheWrite: "#e879f9", output: "#f4f4f5" };

/** A segmented bar of the token types; a type at zero takes no segment. */
export const TokenTypeBar = ({ values, height = 8 }: { values: Record<TokenType, number>; height?: number }) => (
	<Stack direction="row" aria-hidden="true" sx={{ gap: height > 4 ? "3px" : "2px", height }}>
		{tokenTypes
			.filter((type) => values[type] > 0)
			.map((type) => (
				<Box key={type} sx={{ flex: `${values[type]} 1 0`, minWidth: height / 2, borderRadius: `${height / 2}px`, bgcolor: tokenTypeColor[type] }} />
			))}
	</Stack>
);

/** The cost or the tokens of the period split by token type: a segmented bar and its legend. */
export const TokenTypeBreakdown = ({ values, metric, savings }: { values: Record<TokenType, number>; metric: UsageMetric; savings: number }) => {
	const title = metric === "cost" ? "Cost by type" : "Tokens by type";

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
			<TokenTypeBar values={values} />
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
