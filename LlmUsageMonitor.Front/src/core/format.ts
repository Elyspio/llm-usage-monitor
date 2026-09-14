// Dates and durations, in French.

type When = string | number | Date;

const toMs = (value: When) => (typeof value === "number" ? value : typeof value === "string" ? Date.parse(value) : value.getTime());

const hourFormat = new Intl.DateTimeFormat("fr-FR", { hour: "2-digit", minute: "2-digit" });
const dayFormat = new Intl.DateTimeFormat("fr-FR", { weekday: "short", day: "numeric", month: "short" });
const pad = (value: number) => String(value).padStart(2, "0");

export const fmtHour = (value: When) => hourFormat.format(toMs(value));

export function fmtDayLabel(value: When, now: number): string {
	const day = (date: Date) => new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
	const diff = Math.round((day(new Date(toMs(value))) - day(new Date(now))) / 86_400_000);
	if (diff === 0) return "aujourd'hui";
	if (diff === -1) return "hier";
	if (diff === 1) return "demain";
	return dayFormat.format(toMs(value));
}

/** « aujourd'hui 17:52 », « hier 09:10 », « mar. 16 sept. 04:00 ». */
export const fmtWhen = (value: When, now: number) => `${fmtDayLabel(value, now)} ${fmtHour(value)}`;

/** 3 j 4 h · 2 h 10 min · 6 min 59 s · 12 s */
export function fmtSpan(spanMs: number, withSeconds = false): string {
	const seconds = Math.floor(Math.abs(spanMs) / 1000);
	const days = Math.floor(seconds / 86_400);
	const hours = Math.floor((seconds % 86_400) / 3600);
	const minutes = Math.floor((seconds % 3600) / 60);
	const rest = seconds % 60;
	if (days > 0) return `${days} j ${hours} h`;
	if (hours > 0) return `${hours} h ${pad(minutes)} min`;
	if (minutes > 0) return withSeconds ? `${minutes} min ${pad(rest)} s` : `${minutes} min`;
	return `${rest} s`;
}

export function fmtIn(value: When, now: number): string {
	const diff = toMs(value) - now;
	return diff >= 0 ? `dans ${fmtSpan(diff, true)}` : `il y a ${fmtSpan(diff)}`;
}

export const fmtAgo = (value: When, now: number) => `il y a ${fmtSpan(now - toMs(value))}`;

export function fmtDuration(minutes: number | null | undefined): string {
	if (minutes == null) return "durée inconnue";
	if (minutes % 1440 === 0) return `${minutes / 1440} j`;
	if (minutes % 60 === 0) return `${minutes / 60} h`;
	return `${minutes} min`;
}

export const fmtPercent = (value: number) => `${Math.round(value)} %`;
