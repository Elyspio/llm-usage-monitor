import { createBrowserRouter } from "react-router";
import { routes } from "@/config/routes";
import { ProtectedRoute } from "@components/auth/ProtectedRoute";
import { AuthCallback } from "@pages/AuthCallback";
import { DashboardPage } from "@pages/DashboardPage";

export const router = createBrowserRouter([
	{ path: routes.authCallback, element: <AuthCallback /> },
	{
		path: routes.dashboard,
		element: (
			<ProtectedRoute>
				<DashboardPage />
			</ProtectedRoute>
		),
	},
]);
