import { Box, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, ToggleButton, ToggleButtonGroup, Typography } from "@mui/material";
import { useState } from "react";
import { locale } from "@/core/format";
import { fmtShare, fmtTokens, fmtUsd, type DayLine, type ModelLine, type UsageMetric } from "@/core/usage";
import { providerColor } from "@/core/dashboard";
import { ProviderLogo } from "@components/dashboard/ProviderLogo";

type Breakdown = "model" | "day";

const dayFormat = new Intl.DateTimeFormat(locale, { weekday: "short", day: "numeric", month: "short" });

const numeric = { textAlign: "right", fontVariantNumeric: "tabular-nums", whiteSpace: "nowrap" } as const;

/** Cost, share and tokens by model or by day; a model without price shows "Unpriced". */
export const UsageBreakdown = ({ models, days, metric }: { models: ModelLine[]; days: DayLine[]; metric: UsageMetric }) => {
	const [breakdown, setBreakdown] = useState<Breakdown>("model");
	const lines = breakdown === "model" ? models : days;

	return (
		<Paper component="section" aria-label="Usage by model" variant="outlined" sx={{ p: 2.5 }}>
			<Stack direction="row" sx={{ justifyContent: "space-between", alignItems: "center", mb: 1 }}>
				<Typography variant="h6" component="h2">
					Usage by model
				</Typography>
				<ToggleButtonGroup size="small" exclusive value={breakdown} onChange={(_, value: Breakdown | null) => value && setBreakdown(value)} aria-label="Group by">
					<ToggleButton value="model">Model</ToggleButton>
					<ToggleButton value="day">Day</ToggleButton>
				</ToggleButtonGroup>
			</Stack>
			<Box sx={{ overflowX: "auto" }}>
				<Table size="small">
					<TableHead>
						<TableRow>
							<TableCell>{breakdown === "model" ? "Model" : "Day"}</TableCell>
							<TableCell sx={numeric}>Cost</TableCell>
							<TableCell sx={numeric}>{metric === "cost" ? "Cost share" : "Token share"}</TableCell>
							<TableCell sx={numeric}>Tokens</TableCell>
						</TableRow>
					</TableHead>
					<TableBody>
						{lines.map((line) => (
							<TableRow key={"model" in line ? `${line.provider}:${line.model}` : line.start}>
								<TableCell>
									{"model" in line ? (
										<Stack direction="row" spacing={1.25} sx={{ alignItems: "center" }}>
											<ProviderLogo provider={line.provider} sx={{ fontSize: 16, color: providerColor[line.provider] }} />
											<Typography variant="body2" sx={{ fontFamily: "IBM Plex Mono", fontSize: "0.8rem" }}>
												{line.model}
											</Typography>
										</Stack>
									) : (
										dayFormat.format(line.start)
									)}
								</TableCell>
								<TableCell sx={{ ...numeric, fontWeight: line.priced ? 700 : 400, color: line.priced ? "text.primary" : "text.secondary" }}>
									{line.priced ? fmtUsd(line.cost) : "Unpriced"}
								</TableCell>
								<TableCell sx={{ ...numeric, color: "text.secondary" }}>{line.priced || metric === "tokens" ? fmtShare(line.share) : "—"}</TableCell>
								<TableCell sx={numeric}>{fmtTokens(line.tokens)}</TableCell>
							</TableRow>
						))}
					</TableBody>
				</Table>
			</Box>
		</Paper>
	);
};
