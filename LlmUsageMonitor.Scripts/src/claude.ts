import { readFile } from "node:fs/promises";
import { homedir } from "node:os";
import { join } from "node:path";
import {
	capture,
	record,
	timeout,
	UsageError,
	type UsageOptions,
	type UsageResult,
	type UsageWindow,
	windowOf,
} from "./shared.ts";

/** Uses Claude's undocumented usage endpoint with the existing CLI OAuth login. */
export function getClaudeUsage(options: UsageOptions = {}): Promise<UsageResult> {
	return capture("claude", async () => {
		const timeoutMs = timeout(options);
		const path =
			options.claudeCredentialsPath ??
			join(process.env.CLAUDE_CONFIG_DIR || join(homedir(), ".claude"), ".credentials.json");
		let credentials: Record<string, unknown>;
		try {
			credentials = record(JSON.parse(await readFile(path, "utf8")));
		} catch {
			throw new UsageError(
				"CREDENTIALS_UNAVAILABLE",
				"Cannot read Claude CLI credentials. Sign in with the Claude CLI or set claudeCredentialsPath.",
			);
		}
		const oauth = record(credentials.claudeAiOauth ?? credentials);
		if (typeof oauth.accessToken !== "string" || !oauth.accessToken) {
			throw new UsageError(
				"AUTH_REQUIRED",
				"Claude CLI credentials contain no OAuth access token. Sign in with the Claude CLI.",
			);
		}
		if (typeof oauth.expiresAt === "number" && oauth.expiresAt <= Date.now()) {
			throw new UsageError(
				"AUTH_EXPIRED",
				"Claude CLI login has expired. Refresh it using the Claude CLI.",
			);
		}
		let response: Response;
		let body: unknown;
		try {
			response = await fetch("https://api.anthropic.com/api/oauth/usage", {
				method: "GET",
				headers: {
					Authorization: `Bearer ${oauth.accessToken}`,
					"anthropic-beta": "oauth-2025-04-20",
					Accept: "application/json",
				},
				redirect: "error",
				signal: AbortSignal.timeout(timeoutMs),
			});
			if (!response.ok) {
				const code =
					response.status === 401
						? "AUTH_EXPIRED"
						: response.status === 403
							? "ACCESS_DENIED"
							: response.status === 429
								? "RATE_LIMITED"
								: "HTTP_ERROR";
				await response.body?.cancel();
				throw new UsageError(
					code,
					`Claude usage endpoint returned HTTP ${response.status}. Check your CLI login; for 429, wait before retrying.`,
				);
			}
			body = await response.json();
		} catch (error) {
			if (
				error instanceof Error &&
				(error.name === "TimeoutError" || error.name === "AbortError")
			) {
				throw new UsageError("TIMEOUT", "Claude usage request timed out.");
			}
			throw error;
		}
		const windows: UsageWindow[] = [];
		for (const [id, value] of Object.entries(record(body))) {
			// Extra usage is monetary billing, not a subscription time window.
			if (id === "extra_usage" || value == null) continue;
			if (typeof value !== "object" || Array.isArray(value)) continue;
			const window = record(value);
			if (!("utilization" in window)) continue;
			windows.push(
				windowOf(
					id,
					window.utilization,
					window.resets_at,
					id === "five_hour" ? 300 : id.startsWith("seven_day") ? 10_080 : null,
				),
			);
		}
		return windows;
	});
}
