import { Box, Typography } from "@mui/material";
import type { ReactNode } from "react";

/** Overline and the single h1 of a page, shown in every state of the page (loading, error, data). */
export const PageHeader = ({ overline, title, children }: { overline: string; title: string; children?: ReactNode }) => (
	<Box>
		<Typography variant="overline" sx={{ color: "text.secondary" }}>
			{overline}
		</Typography>
		<Typography variant="h5" component="h1" sx={{ fontWeight: 700 }}>
			{title}
		</Typography>
		{children}
	</Box>
);
