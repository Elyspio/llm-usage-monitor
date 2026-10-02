import { routes } from "@/config/routes";

/** State carried by the sign-in redirect, given back by Keycloak with the authorization code. */
export type SignInState = { returnTo: string };

/** The page to come back to after the sign-in: the current one, except the callback itself. */
export function currentReturnPath(location: Pick<Location, "pathname" | "search" | "hash">): string {
	return location.pathname.startsWith(routes.authCallback) ? routes.dashboard : `${location.pathname}${location.search}${location.hash}`;
}

/** The page asked before the sign-in, when it is a path of the application; the dashboard otherwise (never another origin). */
export function returnPathOf(state: unknown): string {
	const returnTo = typeof state === "object" && state !== null && "returnTo" in state ? state.returnTo : null;
	if (typeof returnTo !== "string" || !returnTo.startsWith("/") || returnTo.startsWith("//") || returnTo.startsWith("/\\")) return routes.dashboard;
	return returnTo.startsWith(routes.authCallback) ? routes.dashboard : returnTo;
}
