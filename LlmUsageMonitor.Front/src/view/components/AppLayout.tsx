import DashboardOutlinedIcon from "@mui/icons-material/DashboardOutlined";
import HistoryOutlinedIcon from "@mui/icons-material/HistoryOutlined";
import InsightsOutlinedIcon from "@mui/icons-material/InsightsOutlined";
import TuneIcon from "@mui/icons-material/Tune";
import LogoutIcon from "@mui/icons-material/Logout";
import { Avatar, Box, Button, IconButton, Stack, Tooltip, Typography } from "@mui/material";
import { NavLink, Outlet } from "react-router";
import { routes } from "@/config/routes";
import { useAuth } from "@/view/context/auth.context";

export const AppLayout = () => {
	const { user, signOut } = useAuth();
	const username = user?.profile.preferred_username;
	return (
		<Box sx={{ display: { md: "flex" }, minHeight: "100vh" }}>
			<Box
				component="a"
				href="#main"
				sx={{ position: "fixed", left: 16, top: -100, zIndex: 10, bgcolor: "primary.main", color: "primary.contrastText", p: 2, "&:focus": { top: 16 } }}
			>
				Skip to content
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
							Usage & automation
						</Typography>
					</Box>
				</Stack>
				<Typography variant="overline" sx={{ color: "text.secondary", mb: 1, display: { xs: "none", md: "block" } }}>
					Control space
				</Typography>
				<Stack
					component="nav"
					aria-label="Main navigation"
					direction={{ xs: "row", md: "column" }}
					spacing={1}
					sx={{
						"& .MuiButton-root": {
							justifyContent: "flex-start",
							color: "text.secondary",
							fontSize: { xs: "0.8rem", sm: "0.95rem" },
							py: 1.5,
							px: { xs: 1, md: 2 },
							minWidth: 0,
							flex: { xs: 1, md: "initial" },
						},
						"& .MuiButton-startIcon": { ml: 0, mr: { xs: 0.75, md: 1 } },
						"& .active": { bgcolor: "#15352d", color: "text.primary" },
					}}
				>
					<Button component={NavLink} to={routes.dashboard} end startIcon={<DashboardOutlinedIcon />}>
						<Box component="span" sx={{ display: { xs: "none", sm: "inline" } }}>
							Dashboard
						</Box>
						<Box component="span" sx={{ display: { xs: "inline", sm: "none" } }}>
							Home
						</Box>
					</Button>
					<Button component={NavLink} to={routes.history} startIcon={<HistoryOutlinedIcon />}>
						History
					</Button>
					<Button component={NavLink} to={routes.usage} startIcon={<InsightsOutlinedIcon />}>
						Usage
					</Button>
					<Button component={NavLink} to={routes.settings} startIcon={<TuneIcon />}>
						Settings
					</Button>
				</Stack>
				<Box sx={{ mt: "auto", pt: { xs: 2, md: 4 } }}>
					<Stack direction="row" spacing={1.5} sx={{ alignItems: "center", borderTop: 1, borderColor: "divider", pt: 2 }}>
						<Avatar
							aria-hidden="true"
							variant="rounded"
							sx={{ width: 32, height: 32, borderRadius: "10px", bgcolor: "#12352b", color: "primary.main", fontFamily: "IBM Plex Mono", fontSize: "0.85rem" }}
						>
							{username?.[0]?.toUpperCase() ?? "?"}
						</Avatar>
						<Typography
							variant="body2"
							title={username}
							sx={{ flex: 1, minWidth: 0, fontWeight: 600, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}
						>
							{username}
						</Typography>
						<Tooltip title="Sign out">
							<IconButton aria-label="Sign out" size="small" onClick={signOut} sx={{ color: "text.secondary", "&:hover": { color: "text.primary" } }}>
								<LogoutIcon fontSize="small" />
							</IconButton>
						</Tooltip>
					</Stack>
				</Box>
			</Box>
			<Box component="main" id="main" tabIndex={-1} sx={{ p: { xs: 2, sm: 3, xl: 5 }, width: "100%", minWidth: 0, maxWidth: 1900, mx: "auto" }}>
				<Outlet />
			</Box>
		</Box>
	);
};
