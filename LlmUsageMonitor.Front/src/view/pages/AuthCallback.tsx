import { Button, CircularProgress, Stack, Typography } from "@mui/material";
import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { useAuth } from "@/view/context/auth.context";

export const AuthCallback = () => {
	const navigate = useNavigate();
	const { completeSignIn, signIn } = useAuth();
	const [failed, setFailed] = useState(false);
	// StrictMode runs effects twice; the authorization code can only be exchanged once.
	const completion = useRef<Promise<string> | null>(null);

	useEffect(() => {
		let active = true;
		const pending = (completion.current ??= completeSignIn());
		void pending.then(
			(returnTo) => {
				if (active) void navigate(returnTo, { replace: true });
			},
			() => {
				if (active) setFailed(true);
			}
		);

		return () => {
			active = false;
		};
	}, [completeSignIn, navigate]);

	return (
		<Stack spacing={2} sx={{ minHeight: "100vh", alignItems: "center", justifyContent: "center", px: 3 }}>
			{failed ? (
				<>
					<Typography variant="h6" component="h1">
						Sign in failed
					</Typography>
					<Button variant="contained" onClick={signIn}>
						Try again
					</Button>
				</>
			) : (
				<>
					<CircularProgress />
					<Typography component="h1" sx={{ color: "text.secondary" }}>
						Signing in…
					</Typography>
				</>
			)}
		</Stack>
	);
};
