import { runtimeConfig } from "@/config/runtime.config";
import { toApiError } from "@/core/apis/api-error";
import { client } from "@/core/apis/generated/client.gen";
import { userManager } from "@/core/auth/user-manager";

/** Points the generated client at the API, attaches the access token of the signed-in user and types the failures. */
export function configureApiClient() {
	client.setConfig({ baseUrl: runtimeConfig.endpoints.apiUrl });
	client.interceptors.request.use(async (request) => {
		const user = await userManager.getUser();
		if (user && !user.expired) {
			request.headers.set("Authorization", `Bearer ${user.access_token}`);
		}
		return request;
	});
	client.interceptors.error.use(toApiError);
}
