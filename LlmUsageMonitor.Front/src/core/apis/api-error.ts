import type { ValidationProblemDetails } from "@/core/apis/generated/types.gen";

/**
 * A failed API call: its HTTP status (null when the API could not be reached) and the ProblemDetails fields of the body.
 * It stays assignable to the error types of the generated client, which are ProblemDetails.
 */
export class ApiError extends Error implements ValidationProblemDetails {
	readonly status: number | null;
	readonly title?: string | null;
	readonly detail?: string | null;
	readonly errors?: Record<string, string[]>;

	constructor(status: number | null, body: unknown) {
		const problem: ValidationProblemDetails = typeof body === "object" && body !== null ? body : {};
		super(problem.detail ?? problem.title ?? (status === null ? "API unreachable" : `HTTP ${status}`), { cause: body });
		this.name = "ApiError";
		this.status = status;
		this.title = problem.title;
		this.detail = problem.detail;
		this.errors = problem.errors;
	}
}

/** Error interceptor of the generated client: every failure becomes an ApiError, a cancelled request stays as it is. */
export function toApiError(error: unknown, response: Response | undefined): unknown {
	if (error instanceof ApiError || (error instanceof DOMException && error.name === "AbortError")) return error;
	return new ApiError(response?.status ?? null, error);
}

export const errorStatus = (error: unknown): number | null => (error instanceof ApiError ? error.status : null);

/** The session is gone (401) or the account lacks the role (403): retrying cannot help. */
export const isAuthError = (error: unknown) => errorStatus(error) === 401 || errorStatus(error) === 403;

/** A short message for the user, by HTTP status; `byStatus` overrides the message of a given status. */
export function apiErrorMessage(error: unknown, byStatus: Partial<Record<number, string>> = {}): string {
	const status = errorStatus(error);
	if (status !== null && byStatus[status]) return byStatus[status];
	if (status === null) return "The API could not be reached: check the connection.";
	if (status === 401) return "Session expired: sign in again.";
	if (status === 403) return "Access denied: the account lacks the admin role.";
	if (status === 404) return "Not found on the server.";
	if (status === 400) return "Request refused by the server.";
	if (status >= 500) return `Server error (${status}): see the service logs.`;
	return `Request failed (HTTP ${status}).`;
}
