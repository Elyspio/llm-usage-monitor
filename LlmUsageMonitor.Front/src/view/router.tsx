import { createBrowserRouter } from "react-router";
import { routes } from "@/config/routes";
import { AppLayout } from "@components/AppLayout";
import { ProtectedRoute } from "@components/auth/ProtectedRoute";
import { AuthCallback } from "@pages/AuthCallback";
import { DashboardPage } from "@pages/DashboardPage";
import { HistoryPage } from "@pages/HistoryPage";
import { SettingsPage } from "@pages/SettingsPage";
import { UsagePage } from "@pages/UsagePage";

export const router = createBrowserRouter([
	{ path: routes.authCallback, element: <AuthCallback /> },
	{
		path: routes.dashboard,
		element: (
			<ProtectedRoute>
				<AppLayout />
			</ProtectedRoute>
		),
		children: [
			{ index: true, element: <DashboardPage /> },
			{ path: routes.history.slice(1), element: <HistoryPage /> },
			{ path: routes.usage.slice(1), element: <UsagePage /> },
			{ path: routes.settings.slice(1), element: <SettingsPage /> },
		],
	},
]);
