import { CircularProgress, Stack } from "@mui/material";
import { createBrowserRouter, type RouteObject } from "react-router";
import { routes } from "@/config/routes";
import { AppLayout } from "@components/AppLayout";
import { ProtectedRoute } from "@components/auth/ProtectedRoute";

const PageLoader = () => (
	<Stack sx={{ minHeight: "50vh", alignItems: "center", justifyContent: "center" }}>
		<CircularProgress aria-label="Loading the page" />
	</Stack>
);

// Each page is its own chunk: the charts (@mui/x-charts, d3) only load with History and Usage.
export const appRoutes: RouteObject[] = [
	{ path: routes.authCallback, lazy: async () => ({ Component: (await import("@pages/AuthCallback")).AuthCallback }), HydrateFallback: PageLoader },
	{
		path: routes.dashboard,
		element: (
			<ProtectedRoute>
				<AppLayout />
			</ProtectedRoute>
		),
		HydrateFallback: PageLoader,
		children: [
			{ index: true, lazy: async () => ({ Component: (await import("@pages/DashboardPage")).DashboardPage }) },
			{ path: routes.history.slice(1), lazy: async () => ({ Component: (await import("@pages/HistoryPage")).HistoryPage }) },
			{ path: routes.usage.slice(1), lazy: async () => ({ Component: (await import("@pages/UsagePage")).UsagePage }) },
			{ path: routes.settings.slice(1), lazy: async () => ({ Component: (await import("@pages/SettingsPage")).SettingsPage }) },
		],
	},
];

export const router = createBrowserRouter(appRoutes);
