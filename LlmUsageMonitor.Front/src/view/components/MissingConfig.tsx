import { Alert, AlertTitle, Box, Stack, Typography } from "@mui/material";

/** Shown instead of the application when `/conf.js` (or Aspire in development) leaves an entry empty. */
export const MissingConfig = ({ missing }: { missing: string[] }) => (
	<Stack component="main" sx={{ minHeight: "100vh", alignItems: "center", justifyContent: "center", p: 3 }}>
		<Alert severity="error" sx={{ maxWidth: 640 }}>
			<AlertTitle>
				<Typography component="h1" sx={{ fontWeight: 700 }}>
					Configuration missing
				</Typography>
			</AlertTitle>
			The application cannot start: these entries of the runtime configuration are empty.
			<Box component="ul" sx={{ my: 1, pl: 3 }}>
				{missing.map((entry) => (
					<li key={entry}>
						<code>{entry}</code>
					</li>
				))}
			</Box>
			In development, start the front with <code>aspire run</code>, which injects the Keycloak realm. In production, the API serves <code>/conf.js</code> from its{" "}
			<code>Oidc</code> settings.
		</Alert>
	</Stack>
);
