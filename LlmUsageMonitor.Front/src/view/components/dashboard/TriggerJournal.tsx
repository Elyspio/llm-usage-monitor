import AutorenewIcon from "@mui/icons-material/Autorenew";
import TouchAppIcon from "@mui/icons-material/TouchApp";
import { Box, Chip, CircularProgress, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography } from "@mui/material";
import type { TriggerRun } from "@/core/apis/generated/types.gen";
import { providerColor, providerLabel } from "@/core/dashboard";
import { fmtAgo, fmtWhen } from "@/core/format";

export const TriggerJournal = ({ runs, now }: { runs: TriggerRun[]; now: number }) => (
	<Paper component="section" aria-label="Trigger log" variant="outlined" sx={{ p: 2.5, height: "100%" }}>
		<Stack direction="row" sx={{ justifyContent: "space-between", alignItems: "center", mb: 1 }}>
			<Typography variant="h6" component="h2" sx={{ fontWeight: 700 }}>
				Trigger log
			</Typography>
			<Typography variant="caption" sx={{ color: "text.secondary" }}>
				Last 10
			</Typography>
		</Stack>
		{runs.length === 0 ? (
			<Typography sx={{ color: "text.secondary", py: 4, textAlign: "center" }}>No trigger yet.</Typography>
		) : (
			<Box sx={{ overflowX: "auto" }}>
				<Table size="small">
					<TableHead>
						<TableRow>
							<TableCell>When</TableCell>
							<TableCell>Provider</TableCell>
							<TableCell>Mode</TableCell>
							<TableCell>Result</TableCell>
						</TableRow>
					</TableHead>
					<TableBody>
						{runs.map((run) => (
							<TableRow key={run.id}>
								<TableCell sx={{ whiteSpace: "nowrap", verticalAlign: "top" }}>
									{fmtWhen(run.startedAt, now)}
									<Typography variant="caption" sx={{ color: "text.secondary", display: "block" }}>
										{fmtAgo(run.startedAt, now)}
									</Typography>
								</TableCell>
								<TableCell sx={{ verticalAlign: "top", color: providerColor[run.provider], fontWeight: 600 }}>{providerLabel[run.provider]}</TableCell>
								<TableCell sx={{ verticalAlign: "top" }}>
									<Stack direction="row" spacing={0.5} sx={{ alignItems: "center" }}>
										{run.manual ? <TouchAppIcon fontSize="small" /> : <AutorenewIcon fontSize="small" />}
										<span>{run.manual ? "manual" : "auto"}</span>
									</Stack>
								</TableCell>
								<TableCell sx={{ verticalAlign: "top" }}>
									<Outcome run={run} />
								</TableCell>
							</TableRow>
						))}
					</TableBody>
				</Table>
			</Box>
		)}
	</Paper>
);

const Outcome = ({ run }: { run: TriggerRun }) => {
	if (run.status === "running") return <Chip size="small" icon={<CircularProgress size={12} />} label="running" />;
	const duration = run.durationMs != null ? `${(run.durationMs / 1000).toFixed(1)} s` : null;
	return (
		<>
			<Chip size="small" color={run.status === "succeeded" ? "success" : "error"} variant="outlined" label={run.status === "succeeded" ? "succeeded" : "failed"} />
			<Typography variant="caption" sx={{ color: "text.secondary", display: "block", mt: 0.5 }}>
				{[run.model, duration].filter(Boolean).join(" · ")}
			</Typography>
			{run.error && (
				<Box component="details" sx={{ typography: "caption", mt: 0.5 }}>
					<summary>{run.errorCode ?? "error"}</summary>
					<code>{run.error}</code>
				</Box>
			)}
		</>
	);
};
