import { CssBaseline, ThemeProvider } from "@mui/material";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "react-router/dom";
import { AuthProvider } from "@/view/context/auth.context";
import { router } from "@/view/router";
import { theme } from "@/view/theme";

const queryClient = new QueryClient({
	defaultOptions: { queries: { retry: 1 } },
});

export const App = () => (
	<QueryClientProvider client={queryClient}>
		<AuthProvider>
			<ThemeProvider theme={theme}>
				<CssBaseline />
				<RouterProvider router={router} />
			</ThemeProvider>
		</AuthProvider>
	</QueryClientProvider>
);
