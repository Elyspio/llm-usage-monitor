import { UserManager } from "oidc-client-ts";
import { runtimeConfig } from "@/config/runtime.config";

const { oauth } = runtimeConfig;

/**
 * Authorization code flow with PKCE against the public client. The tokens stay in the sessionStorage of the tab (default
 * store): gone with the tab, never shared with the others; a new tab signs in again through the Keycloak session.
 */
export const userManager = new UserManager({
	authority: oauth.authority,
	client_id: oauth.clientId,
	redirect_uri: oauth.callbackUrl,
	post_logout_redirect_uri: `${window.location.origin}/`,
	response_type: "code",
	scope: "openid",
	automaticSilentRenew: true,
});

// The user stored in localStorage by the previous versions would stay readable there forever. Only the user entries go:
// the sign-in state of a pending redirect also lives in localStorage ("oidc.<state>").
for (const key of Object.keys(window.localStorage)) {
	if (key.startsWith("oidc.user:")) window.localStorage.removeItem(key);
}
