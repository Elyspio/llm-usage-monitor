import { Box, Typography } from "@mui/material";
import type { ReactNode } from "react";

/** The single h1 of a page, drawn as an overline and named by the title alone; shown in every state of the page (loading, error, data). */
export const PageHeader = ({ overline, title, children }: { overline: string; title: string; children?: ReactNode }) => (
	<Box>
		<Typography variant="overline" component="h1" aria-label={title} sx={{ color: "text.secondary" }}>
			{overline}
		</Typography>
		{children}
	</Box>
);
