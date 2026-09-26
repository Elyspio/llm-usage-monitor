import { Box, Button, CircularProgress, Stack, Typography } from "@mui/material";
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
			<Stack sx={{ minHeight: "100vh", alignItems: "center", justifyContent: "center", p: 3 }}>
				<Stack spacing={3} sx={{ width: "100%", maxWidth: 540, p: { xs: 3, sm: 6 }, border: 1, borderColor: "divider", borderRadius: 2, bgcolor: "background.paper" }}>
					<Box
						aria-hidden="true"
						sx={{ width: 56, height: 56, display: "grid", placeItems: "center", bgcolor: "primary.main", color: "primary.contrastText", borderRadius: 3, fontSize: 32 }}
					>
						↗
					</Box>
					<Typography variant="overline" color="primary">
						LLM Usage Monitor
					</Typography>
					<Typography variant="h1" sx={{ fontSize: { xs: "2rem", sm: "2.75rem" } }}>
						LLM Monitor
					</Typography>
					<Typography sx={{ color: "text.secondary" }}>See your Claude and Codex quotas, follow the resets and drive the restarts.</Typography>
					<Button variant="contained" onClick={signIn} size="large">
						Sign in
					</Button>
					<Typography variant="caption" color="text.secondary">
						Sign in required to access the dashboard.
					</Typography>
				</Stack>
			</Stack>
		);
	}

	return children;
};
