import { AppBar, Box, Button, Stack, Toolbar, Typography } from "@mui/material";
import { NavLink, Outlet } from "react-router";
import { routes } from "@/config/routes";
import { useAuth } from "@/view/context/auth.context";

export const AppLayout = () => {
	const { user, signOut } = useAuth();

	return (
		<>
			<AppBar position="static" color="default" elevation={0} sx={{ borderBottom: 1, borderColor: "divider" }}>
				<Toolbar sx={{ gap: 1 }}>
					<Typography variant="h6" component="span" sx={{ fontWeight: 700, mr: 2 }}>
						LLM Usage Monitor
					</Typography>
					<Button component={NavLink} to={routes.dashboard} end color="inherit">
						Tableau de bord
					</Button>
					<Button component={NavLink} to={routes.settings} color="inherit">
						Réglages
					</Button>
					<Stack direction="row" spacing={1} sx={{ ml: "auto", alignItems: "center" }}>
						<Typography variant="body2" sx={{ color: "text.secondary" }}>
							{user?.profile.preferred_username}
						</Typography>
						<Button color="inherit" onClick={signOut}>
							Se déconnecter
						</Button>
					</Stack>
				</Toolbar>
			</AppBar>
			<Box component="main" sx={{ p: { xs: 2, md: 3 }, maxWidth: 1440, mx: "auto" }}>
				<Outlet />
			</Box>
		</>
	);
};
