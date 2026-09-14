import { Button, CircularProgress, Stack, Typography } from "@mui/material";
import type { ReactNode } from "react";
import { useAuth } from "@/view/context/auth.context";

export const ProtectedRoute = ({ children }: { children: ReactNode }) => {
	const { user, loading, signIn } = useAuth();

	if (loading) {
		return (
			<Stack sx={{ minHeight: "100vh", alignItems: "center", justifyContent: "center" }}>
				<CircularProgress />
			</Stack>
		);
	}

	if (!user) {
		return (
			<Stack spacing={2} sx={{ minHeight: "100vh", alignItems: "center", justifyContent: "center", px: 3 }}>
				<Typography variant="h4" component="h1">
					LLM Usage Monitor
				</Typography>
				<Typography sx={{ color: "text.secondary" }}>Connexion requise pour accéder au tableau de bord.</Typography>
				<Button variant="contained" onClick={signIn}>
					Se connecter
				</Button>
			</Stack>
		);
	}

	return children;
};
