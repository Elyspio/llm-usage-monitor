import DashboardOutlinedIcon from "@mui/icons-material/DashboardOutlined";
import TuneIcon from "@mui/icons-material/Tune";
import LogoutIcon from "@mui/icons-material/Logout";
import { Box, Button, Stack, Typography } from "@mui/material";
import { NavLink, Outlet } from "react-router";
import { routes } from "@/config/routes";
import { useAuth } from "@/view/context/auth.context";

export const AppLayout = () => {
	const { user, signOut } = useAuth();
	return (
		<Box sx={{ display: { md: "flex" }, minHeight: "100vh" }}>
			<Box
				component="a"
				href="#main"
				sx={{ position: "fixed", left: 16, top: -100, zIndex: 10, bgcolor: "primary.main", color: "primary.contrastText", p: 2, "&:focus": { top: 16 } }}
			>
				Aller au contenu
			</Box>
			<Box
				component="aside"
				sx={{
					width: { md: 264, xl: 300 },
					flexShrink: 0,
					borderRight: { md: "1px solid #29292d" },
					borderBottom: { xs: "1px solid #29292d", md: 0 },
					borderColor: "divider",
					bgcolor: "background.paper",
					p: { xs: 2, md: 3 },
					display: "flex",
					flexDirection: "column",
					position: { md: "sticky" },
					top: 0,
					height: { md: "100vh" },
				}}
			>
				<Stack direction="row" spacing={1.5} sx={{ alignItems: "center", mb: { xs: 2, md: 6 } }}>
					<Box
						aria-hidden="true"
						sx={{
							width: 37,
							height: 37,
							bgcolor: "#12352b",
							color: "primary.main",
							borderRadius: "10px",
							display: "grid",
							placeItems: "center",
							fontFamily: "IBM Plex Mono",
							fontWeight: 500,
						}}
					>
						↗
					</Box>
					<Box>
						<Typography sx={{ fontWeight: 700, letterSpacing: "-0.04em" }}>LLM Monitor</Typography>
						<Typography variant="overline" sx={{ color: "text.secondary", fontSize: "0.65rem" }}>
							Usage & automatisation
						</Typography>
					</Box>
				</Stack>
				<Typography variant="overline" sx={{ color: "text.secondary", mb: 1, display: { xs: "none", md: "block" } }}>
					Espace de contrôle
				</Typography>
				<Stack
					component="nav"
					aria-label="Navigation principale"
					direction={{ xs: "row", md: "column" }}
					spacing={1}
					sx={{
						"& .MuiButton-root": { justifyContent: "flex-start", color: "text.secondary", fontSize: "0.95rem", py: 1.5 },
						"& .active": { bgcolor: "#15352d", color: "text.primary" },
					}}
				>
					<Button component={NavLink} to={routes.dashboard} end startIcon={<DashboardOutlinedIcon />}>
						Tableau de bord
					</Button>
					<Button component={NavLink} to={routes.settings} startIcon={<TuneIcon />}>
						Réglages
					</Button>
				</Stack>
				<Box sx={{ mt: "auto", pt: { xs: 2, md: 4 } }}>
					<Box sx={{ borderTop: 1, borderColor: "divider", pt: 2, display: { xs: "flex", md: "block" }, alignItems: "center", justifyContent: "space-between" }}>
						<Typography variant="body2" sx={{ overflowWrap: "anywhere" }}>
							{user?.profile.preferred_username}
						</Typography>
						<Button size="small" color="inherit" startIcon={<LogoutIcon />} onClick={signOut} sx={{ color: "text.secondary", ml: -1 }}>
							Se déconnecter
						</Button>
					</Box>
				</Box>
			</Box>
			<Box component="main" id="main" tabIndex={-1} sx={{ p: { xs: 2, sm: 3, xl: 5 }, width: "100%", minWidth: 0, maxWidth: 1900, mx: "auto" }}>
				<Outlet />
			</Box>
		</Box>
	);
};
