import type { User } from "oidc-client-ts";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { afterAll, beforeAll, describe, expect, it, vi } from "vite-plus/test";
import { ApiError, apiErrorMessage, onAuthFailure, retryQuery, toApiError } from "@/core/apis/api-error";
import { configureApiClient } from "@/core/apis/api.client";
import { client } from "@/core/apis/generated/client.gen";
import { getDashboard } from "@/core/apis/generated/sdk.gen";
import { apiUrl, dashboard } from "./fixtures";

const getUser = vi.hoisted(() => vi.fn());
vi.mock("@/core/auth/user-manager", () => ({ userManager: { getUser } }));

const authorizations: (string | null)[] = [];
const server = setupServer(
	http.get(`${apiUrl}/api/dashboard`, ({ request }) => {
		authorizations.push(request.headers.get("Authorization"));
		return HttpResponse.json(dashboard);
	})
);

beforeAll(() => {
	configureApiClient();
	client.setConfig({ baseUrl: apiUrl });
	server.listen({ onUnhandledRequest: "error" });
});
afterAll(() => server.close());

describe("request interceptor", () => {
	it("sends the access token of a valid session only", async () => {
		getUser.mockResolvedValueOnce({ expired: false, access_token: "tk_valid" } as User);
		await getDashboard();
		getUser.mockResolvedValueOnce({ expired: true, access_token: "tk_old" } as User);
		await getDashboard();
		getUser.mockResolvedValueOnce(null);
		await getDashboard();

		expect(authorizations).toEqual(["Bearer tk_valid", null, null]);
	});
});

describe("error interceptor", () => {
	it("types every failure with its HTTP status and ProblemDetails fields", () => {
		const error = toApiError({ title: "invalid", errors: { url: ["bad"] } }, new Response(null, { status: 400 }));

		expect(error).toBeInstanceOf(ApiError);
		expect(error).toMatchObject({ status: 400, title: "invalid", errors: { url: ["bad"] } });
		expect(toApiError(new TypeError("Failed to fetch"), undefined)).toMatchObject({ status: null });
		const abort = new DOMException("aborted", "AbortError");
		expect(toApiError(abort, undefined)).toBe(abort);
	});

	it("reports the 401 and 403 to the session, not the other failures", () => {
		const reported: number[] = [];
		const stop = onAuthFailure((status) => reported.push(status));

		for (const status of [401, 403, 404, 500]) toApiError({}, new Response(null, { status }));
		stop();
		toApiError({}, new Response(null, { status: 401 }));

		expect(reported).toEqual([401, 403]);
	});

	it("never retries a 401 or a 403, retries the other failures once", () => {
		const failure = (status: number | null) => new ApiError(status, {});

		expect(retryQuery(0, failure(401))).toBe(false);
		expect(retryQuery(0, failure(403))).toBe(false);
		expect(retryQuery(0, failure(500))).toBe(true);
		expect(retryQuery(0, failure(null))).toBe(true);
		expect(retryQuery(1, failure(500))).toBe(false);
	});

	it("words the failure by status", () => {
		expect(apiErrorMessage(new ApiError(null, {}))).toBe("The API could not be reached: check the connection.");
		expect(apiErrorMessage(new ApiError(403, {}))).toBe("Access denied: the account lacks the admin role.");
		expect(apiErrorMessage(new ApiError(502, {}))).toBe("Server error (502): see the service logs.");
		expect(apiErrorMessage(new ApiError(409, {}), { 409: "Busy." })).toBe("Busy.");
	});
});
