import { CssBaseline, ThemeProvider } from "@mui/material";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "react-router/dom";
import { missingRuntimeConfig } from "@/config/runtime.config";
import { MissingConfig } from "@components/MissingConfig";
import { AuthProvider } from "@/view/context/auth.context";
import { router } from "@/view/router";
import { theme } from "@/view/theme";

const queryClient = new QueryClient({
	defaultOptions: { queries: { retry: 1 } },
});

export const App = () => (
	<ThemeProvider theme={theme}>
		<CssBaseline />
		{missingRuntimeConfig.length > 0 ? (
			<MissingConfig missing={missingRuntimeConfig} />
		) : (
			<QueryClientProvider client={queryClient}>
				<AuthProvider>
					<RouterProvider router={router} />
				</AuthProvider>
			</QueryClientProvider>
		)}
	</ThemeProvider>
);
