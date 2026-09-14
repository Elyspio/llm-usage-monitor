import { Alert, Card, CardContent, CircularProgress, Container, Stack, Typography } from "@mui/material";
import { useQuery } from "@tanstack/react-query";
import { getDashboardOptions } from "@/core/apis/generated/@tanstack/react-query.gen";
import type { Provider } from "@/core/apis/generated/types.gen";

const providerLabels: Record<Provider, string> = {
	claude: "Claude",
	codex: "Codex",
};

export const DashboardPage = () => {
	const { data, isPending, isError } = useQuery(getDashboardOptions());

	return (
		<Container sx={{ py: 4 }}>
			<Typography variant="h4" component="h1" gutterBottom>
				LLM Usage Monitor
			</Typography>
			{isPending && <CircularProgress />}
			{isError && <Alert severity="error">Impossible de charger le tableau de bord.</Alert>}
			{data && (
				<Stack direction="row" spacing={2}>
					{data.providers.map(({ provider }) => (
						<Card key={provider} sx={{ minWidth: 240 }}>
							<CardContent>
								<Typography variant="h6">{providerLabels[provider]}</Typography>
								<Typography sx={{ color: "text.secondary" }}>Aucune lecture pour l'instant.</Typography>
							</CardContent>
						</Card>
					))}
				</Stack>
			)}
		</Container>
	);
};
