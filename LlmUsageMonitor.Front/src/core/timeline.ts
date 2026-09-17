import type { ProviderDashboard } from "./apis/generated/types.gen";
import { sortWindows, windowTiming } from "./dashboard";

export function toTimeline(providers: ProviderDashboard[], now: number) {
	const rows = providers.flatMap((provider) =>
		sortWindows(provider.lastReading?.windows ?? [], provider.triggerWindowId).map((window) => ({ provider, window, timing: windowTiming(window, now) }))
	);
	const valid = rows.flatMap((row) => (row.timing && Number.isFinite(row.timing.start) && Number.isFinite(row.timing.end) ? [row.timing.start, row.timing.end] : []));
	const start = new Date(Math.min(now, ...valid));
	start.setHours(0, 0, 0, 0);
	const end = new Date(Math.max(now, ...valid));
	end.setHours(0, 0, 0, 0);
	end.setDate(end.getDate() + 1);
	const span = end.getTime() - start.getTime();
	const position = (time: number) => Math.max(0, Math.min(100, ((time - start.getTime()) / span) * 100));
	const ticks: number[] = [];
	const step = Math.max(1, Math.ceil(span / 86400000 / 9));
	const cursor = new Date(start);
	while (cursor < end) {
		ticks.push(cursor.getTime());
		cursor.setDate(cursor.getDate() + step);
	}
	return { rows, start: start.getTime(), end: end.getTime(), ticks, position };
}
