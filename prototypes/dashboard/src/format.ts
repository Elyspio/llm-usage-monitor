// PROTOTYPE jetable (#7) — formatage des dates et durées en français.

type When = string | number | Date;
const ms = (v: When) => (typeof v === 'number' ? v : typeof v === 'string' ? Date.parse(v) : v.getTime());

const hourFmt = new Intl.DateTimeFormat('fr-FR', { hour: '2-digit', minute: '2-digit' });
const dayFmt = new Intl.DateTimeFormat('fr-FR', { weekday: 'short', day: 'numeric', month: 'short' });
const pad = (n: number) => String(n).padStart(2, '0');

export const fmtHour = (v: When) => hourFmt.format(ms(v));

/** « aujourd'hui 17:52 », « hier 09:10 », « mar. 16 sept. 04:00 ». */
export function fmtWhen(v: When, now: number): string {
	const d = new Date(ms(v));
	return `${fmtDayLabel(d, now)} ${hourFmt.format(d)}`;
}

export function fmtDayLabel(v: When, now: number): string {
	const d = new Date(ms(v));
	const day = (x: Date) => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
	const diff = Math.round((day(d) - day(new Date(now))) / 86_400_000);
	if (diff === 0) return "aujourd'hui";
	if (diff === -1) return 'hier';
	if (diff === 1) return 'demain';
	return dayFmt.format(d);
}

/** 3 j 4 h · 2 h 10 min 05 s · 6 min 59 s */
export function fmtSpan(spanMs: number, withSeconds = false): string {
	const s = Math.floor(Math.abs(spanMs) / 1000);
	const d = Math.floor(s / 86_400);
	const h = Math.floor((s % 86_400) / 3600);
	const m = Math.floor((s % 3600) / 60);
	const sec = s % 60;
	if (d > 0) return `${d} j ${h} h`;
	if (h > 0) return `${h} h ${pad(m)} min${withSeconds ? ` ${pad(sec)} s` : ''}`;
	if (m > 0) return withSeconds ? `${m} min ${pad(sec)} s` : `${m} min`;
	return `${sec} s`;
}

export function fmtIn(v: When, now: number): string {
	const diff = ms(v) - now;
	return diff >= 0 ? `dans ${fmtSpan(diff, true)}` : `il y a ${fmtSpan(diff)}`;
}

export const fmtAgo = (v: When, now: number) => `il y a ${fmtSpan(now - ms(v))}`;

export function fmtDuration(min: number | null): string {
	if (min == null) return 'durée inconnue';
	if (min % 1440 === 0) return `${min / 1440} j`;
	if (min % 60 === 0) return `${min / 60} h`;
	return `${min} min`;
}
