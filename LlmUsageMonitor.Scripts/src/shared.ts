export type Provider = "codex" | "claude";

export interface UsageWindow {
	/** Provider's bucket and window identifiers, e.g. codex/primary or seven_day. */
	id: string;
	remainingPercent: number;
	usedPercent: number;
	/** UTC ISO timestamp, or null when the provider supplies no reset time. */
	resetsAt: string | null;
	windowDurationMinutes: number | null;
}

export type UsageResult =
	| {
			ok: true;
			provider: Provider;
			fetchedAt: string;
			/** Lowest remaining allowance across returned windows. 100 = unused. */
			remainingPercent: number;
			windows: UsageWindow[];
	  }
	| {
			ok: false;
			provider: Provider;
			fetchedAt: string;
			error: { code: string; message: string };
	  };

export interface UsageOptions {
	timeoutMs?: number;
	/** Native executable path if codex is not on PATH (on Windows, use codex.exe). */
	codexExecutable?: string;
	/** Defaults to CLAUDE_CONFIG_DIR/.credentials.json or ~/.claude/.credentials.json. */
	claudeCredentialsPath?: string;
}

export class UsageError extends Error {
	constructor(
		readonly code: string,
		message: string,
	) {
		super(message);
	}
}

export function record(value: unknown): Record<string, unknown> {
	if (!value || typeof value !== "object" || Array.isArray(value)) {
		throw new UsageError("INVALID_RESPONSE", "Expected a usage object.");
	}
	return value as Record<string, unknown>;
}

export function windowOf(
	id: string,
	used: unknown,
	reset: unknown,
	duration: unknown,
): UsageWindow {
	if (typeof used !== "number" || !Number.isFinite(used) || used < 0) {
		throw new UsageError("INVALID_RESPONSE", `Invalid usage percentage for ${id}.`);
	}
	let resetsAt: string | null = null;
	if (reset !== null && reset !== undefined) {
		if (typeof reset !== "number" && typeof reset !== "string") {
			throw new UsageError("INVALID_RESPONSE", `Invalid reset time for ${id}.`);
		}
		const date = new Date(typeof reset === "number" ? reset * 1000 : reset);
		if (!Number.isFinite(date.getTime())) {
			throw new UsageError("INVALID_RESPONSE", `Invalid reset time for ${id}.`);
		}
		resetsAt = date.toISOString();
	}
	if (
		duration != null &&
		(typeof duration !== "number" || !Number.isFinite(duration) || duration <= 0)
	) {
		throw new UsageError("INVALID_RESPONSE", `Invalid window duration for ${id}.`);
	}
	return {
		id,
		usedPercent: used,
		remainingPercent: Math.max(0, 100 - used),
		resetsAt,
		windowDurationMinutes: typeof duration === "number" ? duration : null,
	};
}

export async function capture(
	provider: Provider,
	work: () => Promise<UsageWindow[]>,
): Promise<UsageResult> {
	try {
		const windows = await work();
		if (!windows.length) {
			throw new UsageError(
				"NO_USAGE_DATA",
				"The provider returned no percentage-based usage windows.",
			);
		}
		return {
			ok: true,
			provider,
			fetchedAt: new Date().toISOString(),
			remainingPercent: Math.min(...windows.map((w) => w.remainingPercent)),
			windows,
		};
	} catch (error) {
		return {
			ok: false,
			provider,
			fetchedAt: new Date().toISOString(),
			error:
				error instanceof UsageError
					? { code: error.code, message: error.message }
					: {
							code: "FETCH_FAILED",
							message: "Unable to read usage. Check the CLI login and network connection.",
						},
		};
	}
}

export function timeout(options: UsageOptions): number {
	const value = options.timeoutMs ?? 20_000;
	if (!Number.isInteger(value) || value <= 0 || value > 2_147_483_647) {
		throw new UsageError(
			"INVALID_OPTIONS",
			"timeoutMs must be a positive integer no larger than 2147483647.",
		);
	}
	return value;
}
