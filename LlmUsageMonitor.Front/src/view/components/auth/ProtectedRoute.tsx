import { Alert, Button, CircularProgress, Stack, Typography } from "@mui/material";
import type { ReactNode } from "react";
import { useAuth } from "@/view/context/auth.context";
import { AppLogo } from "@components/AppLogo";

/** Centred card of the sign-in and access-denied screens. */
const AuthCard = ({ children }: { children: ReactNode }) => (
	<Stack component="main" sx={{ minHeight: "100vh", alignItems: "center", justifyContent: "center", p: 3 }}>
		<Stack spacing={3} sx={{ width: "100%", maxWidth: 540, p: { xs: 3, sm: 6 }, border: 1, borderColor: "divider", borderRadius: 2, bgcolor: "background.paper" }}>
			<AppLogo size={56} filled />
			<Typography variant="overline" color="primary">
				LLM Usage Monitor
			</Typography>
			{children}
		</Stack>
	</Stack>
);

export const ProtectedRoute = ({ children }: { children: ReactNode }) => {
	const { user, loading, sessionExpired, accessDenied, signIn, signOut } = useAuth();

	if (loading) {
		return (
			<Stack sx={{ minHeight: "100vh", alignItems: "center", justifyContent: "center" }}>
				<CircularProgress aria-label="Loading the session" />
			</Stack>
		);
	}

	if (!user) {
		return (
			<AuthCard>
				<Typography variant="h1" sx={{ fontSize: { xs: "2rem", sm: "2.75rem" } }}>
					LLM Monitor
				</Typography>
				{sessionExpired && <Alert severity="info">Your session has expired. Sign in again to go back to the page you were on.</Alert>}
				<Typography sx={{ color: "text.secondary" }}>See your Claude and Codex quotas, follow the resets and drive the restarts.</Typography>
				<Button variant="contained" onClick={signIn} size="large">
					Sign in
				</Button>
				<Typography variant="caption" sx={{ color: "text.secondary" }}>
					Sign in required to access the dashboard.
				</Typography>
			</AuthCard>
		);
	}

	if (accessDenied) {
		return (
			<AuthCard>
				<Typography variant="h1" sx={{ fontSize: { xs: "2rem", sm: "2.75rem" } }}>
					Access denied
				</Typography>
				<Typography sx={{ color: "text.secondary" }}>
					The account <strong>{user.profile.preferred_username ?? user.profile.sub}</strong> is signed in but does not have the <code>llm-usage-monitor:admin</code> role.
					Ask for it, then sign in again, or sign in with another account.
				</Typography>
				<Button variant="contained" onClick={signOut} size="large">
					Sign out
				</Button>
			</AuthCard>
		);
	}

	return children;
};
