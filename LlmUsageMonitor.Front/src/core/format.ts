// Dates and durations, in English.

type When = string | number | Date;

const toMs = (value: When) => (typeof value === "number" ? value : typeof value === "string" ? Date.parse(value) : value.getTime());

/** English words, day before month and a 24-hour clock. */
export const locale = "en-GB";

const hourFormat = new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit" });
const dayFormat = new Intl.DateTimeFormat(locale, { weekday: "short", day: "numeric", month: "short" });
const pad = (value: number) => String(value).padStart(2, "0");

export const fmtHour = (value: When) => hourFormat.format(toMs(value));

export function fmtDayLabel(value: When, now: number): string {
	const day = (date: Date) => new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
	const diff = Math.round((day(new Date(toMs(value))) - day(new Date(now))) / 86_400_000);
	if (diff === 0) return "today";
	if (diff === -1) return "yesterday";
	if (diff === 1) return "tomorrow";
	return dayFormat.format(toMs(value));
}

/** "today 17:52", "yesterday 09:10", "Tue 16 Sept 04:00". */
export const fmtWhen = (value: When, now: number) => `${fmtDayLabel(value, now)} ${fmtHour(value)}`;

/** 3 d 4 h · 2 h 10 min · 6 min 59 s · 12 s */
export function fmtSpan(spanMs: number, withSeconds = false): string {
	const seconds = Math.floor(Math.abs(spanMs) / 1000);
	const days = Math.floor(seconds / 86_400);
	const hours = Math.floor((seconds % 86_400) / 3600);
	const minutes = Math.floor((seconds % 3600) / 60);
	const rest = seconds % 60;
	if (days > 0) return `${days} d ${hours} h`;
	if (hours > 0) return `${hours} h ${pad(minutes)} min`;
	if (minutes > 0) return withSeconds ? `${minutes} min ${pad(rest)} s` : `${minutes} min`;
	return `${rest} s`;
}

export function fmtIn(value: When, now: number): string {
	const diff = toMs(value) - now;
	return diff >= 0 ? `in ${fmtSpan(diff, true)}` : `${fmtSpan(diff)} ago`;
}

export const fmtAgo = (value: When, now: number) => `${fmtSpan(now - toMs(value))} ago`;

export function fmtDuration(minutes: number | null | undefined): string {
	if (minutes == null) return "unknown duration";
	if (minutes % 1440 === 0) return `${minutes / 1440} d`;
	if (minutes % 60 === 0) return `${minutes / 60} h`;
	return `${minutes} min`;
}

export const fmtPercent = (value: number) => `${Math.round(value)}%`;
