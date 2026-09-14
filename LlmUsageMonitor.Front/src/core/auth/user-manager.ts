import { UserManager, WebStorageStateStore } from "oidc-client-ts";
import { runtimeConfig } from "@/config/runtime.config";

const { oauth } = runtimeConfig;

/** Authorization code flow with PKCE against the public client. */
export const userManager = new UserManager({
	authority: oauth.authority,
	client_id: oauth.clientId,
	redirect_uri: oauth.callbackUrl,
	post_logout_redirect_uri: `${window.location.origin}/`,
	response_type: "code",
	scope: "openid",
	automaticSilentRenew: true,
	userStore: new WebStorageStateStore({ store: window.localStorage }),
});
