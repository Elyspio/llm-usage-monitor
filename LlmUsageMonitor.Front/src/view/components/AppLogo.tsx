import { Box } from "@mui/material";

/** The "↗" mark of the application: small and tinted in the sidebar, large and filled on the sign-in screens. */
export const AppLogo = ({ size = 37, filled = false }: { size?: number; filled?: boolean }) => (
	<Box
		aria-hidden="true"
		sx={{
			width: size,
			height: size,
			flexShrink: 0,
			display: "grid",
			placeItems: "center",
			bgcolor: filled ? "primary.main" : "surface.accent",
			color: filled ? "primary.contrastText" : "primary.main",
			borderRadius: filled ? 3 : "10px",
			fontFamily: filled ? undefined : "IBM Plex Mono",
			fontWeight: filled ? undefined : 500,
			fontSize: filled ? 32 : undefined,
		}}
	>
		↗
	</Box>
);
