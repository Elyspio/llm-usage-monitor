import { Box, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TableSortLabel, ToggleButton, ToggleButtonGroup, Typography } from "@mui/material";
import { useState, type ReactNode } from "react";
import { locale } from "@/core/format";
import {
	firstDirection,
	fmtMetric,
	fmtShare,
	fmtTokens,
	fmtUsd,
	sortLines,
	tokenTypeLabel,
	tokenTypes,
	type DayLine,
	type LineSort,
	type LineSortKey,
	type ModelLine,
	type UsageMetric,
} from "@/core/usage";
import { providerColor } from "@/core/dashboard";
import { ProviderLogo } from "@components/dashboard/ProviderLogo";
import { TokenTypeBar, tokenTypeColor } from "@components/usage/TokenTypeBreakdown";

type Breakdown = "model" | "day";

const dayFormat = new Intl.DateTimeFormat(locale, { weekday: "short", day: "numeric", month: "short" });

const numeric = { textAlign: "right", fontVariantNumeric: "tabular-nums", whiteSpace: "nowrap" } as const;

/** A header cell that sorts the table on its column: a first click uses the column's natural direction, the next ones flip it. */
const SortableCell = ({
	column,
	label,
	marker,
	sort,
	onSort,
}: {
	column: LineSortKey;
	label: string;
	marker?: ReactNode;
	sort: LineSort | null;
	onSort: (sort: LineSort) => void;
}) => {
	const active = sort?.key === column;
	const direction = active ? sort.direction : firstDirection(column);
	return (
		<TableCell sx={column === "label" ? undefined : numeric} sortDirection={active ? direction : false}>
			<TableSortLabel
				active={active}
				direction={direction}
				onClick={() => onSort({ key: column, direction: active ? (direction === "asc" ? "desc" : "asc") : direction })}
				aria-label={`Sort by ${label}`}
				sx={column === "label" ? undefined : { flexDirection: "row-reverse" }}
			>
				{/* One box, so the reversed row moves only the arrow to the left and the marker stays before its label. */}
				<Box component="span" sx={{ display: "inline-flex", alignItems: "center" }}>
					{marker}
					{label}
				</Box>
			</TableSortLabel>
		</TableCell>
	);
};

/**
 * Cost, share and tokens by model or by day, each split by token type in the chosen metric; a model without price shows
 * "Unpriced" and no cost split. Every column but the split sorts the table; unsorted, it keeps the order of the summary.
 */
export const UsageBreakdown = ({ models, days, metric }: { models: ModelLine[]; days: DayLine[]; metric: UsageMetric }) => {
	const [breakdown, setBreakdown] = useState<Breakdown>("model");
	const [sort, setSort] = useState<LineSort | null>(null);
	const unsorted: (ModelLine | DayLine)[] = breakdown === "model" ? models : days;
	const lines = sort ? sortLines(unsorted, sort, metric) : unsorted;

	return (
		<Paper component="section" aria-label="Usage by model" variant="outlined" sx={{ p: 2 }}>
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
				<Table size="small" sx={{ "& .MuiTableCell-root": { py: 0.75 } }}>
					<TableHead>
						<TableRow>
							<SortableCell column="label" label={breakdown === "model" ? "Model" : "Day"} sort={sort} onSort={setSort} />
							<SortableCell column="cost" label="Cost" sort={sort} onSort={setSort} />
							<SortableCell column="share" label={metric === "cost" ? "Cost share" : "Token share"} sort={sort} onSort={setSort} />
							<TableCell sx={{ width: 140 }}>{metric === "cost" ? "Cost split" : "Token split"}</TableCell>
							{tokenTypes.map((type) => (
								<SortableCell
									key={type}
									column={type}
									label={tokenTypeLabel[type]}
									marker={
										<Box
											aria-hidden="true"
											component="span"
											sx={{ display: "inline-block", flexShrink: 0, width: 8, height: 8, borderRadius: "2px", bgcolor: tokenTypeColor[type], mr: 0.75 }}
										/>
									}
									sort={sort}
									onSort={setSort}
								/>
							))}
							<SortableCell column="tokens" label="Tokens" sort={sort} onSort={setSort} />
						</TableRow>
					</TableHead>
					<TableBody>
						{lines.map((line) => {
							const split = metric === "cost" ? (line.priced ? line.costByType : null) : line.tokensByType;
							return (
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
									<TableCell>{split && <TokenTypeBar values={split} height={6} />}</TableCell>
									{tokenTypes.map((type) => (
										<TableCell key={type} sx={{ ...numeric, color: "text.secondary" }}>
											{split ? fmtMetric(split[type], metric) : "—"}
										</TableCell>
									))}
									<TableCell sx={numeric}>{fmtTokens(line.tokens)}</TableCell>
								</TableRow>
							);
						})}
					</TableBody>
				</Table>
			</Box>
		</Paper>
	);
};
