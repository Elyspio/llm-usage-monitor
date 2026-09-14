import PlayArrowIcon from "@mui/icons-material/PlayArrow";
import { Button, Stack, Typography } from "@mui/material";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { getDashboardQueryKey, getHistoryQueryKey, getTriggerRunOptions, triggerProviderMutation } from "@/core/apis/generated/@tanstack/react-query.gen";
import type { Provider, TriggerRun } from "@/core/apis/generated/types.gen";

/**
 * Direct manual trigger, without confirmation. Disabled while a CLI process runs for the provider; the queued run is
 * followed every 2 s until it ends, then the dashboard and the history are refreshed.
 */
export const TriggerButton = ({ provider, runningTrigger }: { provider: Provider; runningTrigger: TriggerRun | null }) => {
	const queryClient = useQueryClient();
	const trigger = useMutation(triggerProviderMutation());
	const runId = trigger.data?.id ?? null;

	const followed = useQuery({
		...getTriggerRunOptions({ path: { id: runId ?? "" } }),
		enabled: runId !== null,
		refetchInterval: (query) => (query.state.data?.status === "running" ? 2000 : false),
	});
	const status = runId === null ? null : (followed.data?.status ?? "running");
	const finished = status !== null && status !== "running";

	useEffect(() => {
		if (!finished) return;
		void queryClient.invalidateQueries({ queryKey: getDashboardQueryKey() });
		for (const range of ["24h", "7d"]) void queryClient.invalidateQueries({ queryKey: getHistoryQueryKey({ query: { range } }) });
	}, [finished, runId, queryClient]);

	const busy = runningTrigger !== null || trigger.isPending || status === "running";

	return (
		<Stack sx={{ alignItems: "flex-end" }}>
			<Button
				variant="contained"
				startIcon={<PlayArrowIcon />}
				loading={busy}
				loadingPosition="start"
				disabled={busy}
				onClick={() => trigger.mutate({ path: { provider } })}
				sx={{ whiteSpace: "nowrap" }}
			>
				Déclencher maintenant
			</Button>
			{trigger.isError && (
				<Typography variant="caption" sx={{ color: "error.main", mt: 0.5 }}>
					Déclenchement refusé : un process CLI tourne peut-être déjà.
				</Typography>
			)}
		</Stack>
	);
};
