import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { configureApiClient } from "@/core/apis/api.client";
import { App } from "@/view/App";

configureApiClient();

createRoot(document.getElementById("root")!).render(
	<StrictMode>
		<App />
	</StrictMode>
);
