import { Box, Button, Paper, Stack, Typography } from "@mui/material";
import type { FormEvent, ReactNode } from "react";

/** One settings card: header with icon and intent, fields, then the actions pinned to the bottom edge. */
export const SettingsSection = ({
	title,
	subtitle,
	icon,
	actions,
	children,
	onSubmit,
}: {
	title: string;
	subtitle: string;
	icon: ReactNode;
	actions: ReactNode;
	children: ReactNode;
	onSubmit: (event: FormEvent) => void;
}) => (
	<Paper component="form" aria-label={title} variant="outlined" noValidate onSubmit={onSubmit} sx={{ p: 2.5, height: "100%", display: "flex", flexDirection: "column" }}>
		<Stack direction="row" spacing={1.5} sx={{ alignItems: "center", mb: 2.5 }}>
			<Box
				aria-hidden="true"
				sx={{ width: 34, height: 34, flexShrink: 0, bgcolor: "surface.accent", color: "primary.main", borderRadius: "10px", display: "grid", placeItems: "center" }}
			>
				{icon}
			</Box>
			<Box sx={{ minWidth: 0 }}>
				<Typography variant="h6" component="h2">
					{title}
				</Typography>
				<Typography variant="caption" sx={{ color: "text.secondary", display: "block" }}>
					{subtitle}
				</Typography>
			</Box>
		</Stack>
		<Stack spacing={2.5} sx={{ minWidth: 0, flex: 1 }}>
			{children}
		</Stack>
		<Box sx={{ mt: 2.5, pt: 2, borderTop: 1, borderColor: "divider" }}>{actions}</Box>
	</Paper>
);

export const SaveBar = ({ pending, saved, failed, children }: { pending: boolean; saved: boolean; failed: boolean; children?: ReactNode }) => (
	<Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ alignItems: { xs: "flex-start", sm: "center" } }}>
		<Button type="submit" variant="outlined" color="inherit" loading={pending}>
			Save
		</Button>
		{children}
		{saved && (
			<Typography variant="body2" sx={{ color: "success.main" }}>
				Saved, applied immediately.
			</Typography>
		)}
		{failed && (
			<Typography variant="body2" sx={{ color: "error.main" }}>
				Save refused: see the fields.
			</Typography>
		)}
	</Stack>
);
