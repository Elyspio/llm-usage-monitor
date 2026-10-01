// Same rules as the API; the server stays the reference and its field errors are shown as well.

export type FieldErrors = Record<string, string>;

export const intervalBounds = { min: 1, max: 60 };
export const thresholdBounds = { min: 1, max: 20 };

export function validateInterval(value: number): string | null {
	if (!Number.isInteger(value) || value < intervalBounds.min || value > intervalBounds.max) {
		return `Between ${intervalBounds.min} and ${intervalBounds.max} minutes.`;
	}
	// The poll cron restarts every hour: only divisors of 60 keep the gaps even.
	if (60 % value !== 0) {
		return "A divisor of 60 is expected: 1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30 or 60 minutes.";
	}
	return null;
}

export function validateModel(value: string): string | null {
	const model = value.trim();
	return model.length === 0 || model.length > 100 ? "Model required, 100 characters at most." : null;
}

export function validateNtfyUrl(value: string): string | null {
	try {
		const url = new URL(value);
		return url.protocol === "http:" || url.protocol === "https:" ? null : "Absolute http(s) URL expected.";
	} catch {
		return "Absolute http(s) URL expected.";
	}
}

export function validateTopic(value: string): string | null {
	return value.trim() === "" || /^[A-Za-z0-9_-]{1,64}$/.test(value.trim()) ? null : "Letters, digits, _ and - only, 64 characters at most.";
}

export function validateThreshold(value: number): string | null {
	return Number.isInteger(value) && value >= thresholdBounds.min && value <= thresholdBounds.max ? null : `Between ${thresholdBounds.min} and ${thresholdBounds.max}.`;
}

/** Keeps the fields that have an error. */
export function collect(errors: Record<string, string | null>): FieldErrors {
	return Object.fromEntries(Object.entries(errors).filter((entry): entry is [string, string] => entry[1] !== null));
}

/** Field errors of an ASP.NET ValidationProblemDetails, first message per field. */
export function serverFieldErrors(error: unknown): FieldErrors {
	if (typeof error !== "object" || error === null || !("errors" in error)) return {};
	const errors = (error as { errors?: Record<string, string[]> }).errors ?? {};
	return Object.fromEntries(Object.entries(errors).map(([field, messages]) => [field, messages[0] ?? ""]));
}
