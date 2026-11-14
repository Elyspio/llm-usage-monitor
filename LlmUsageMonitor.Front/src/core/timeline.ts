import type { ProviderDashboard } from "./apis/generated/types.gen";
import { parentWindowId, sortWindows, windowTiming } from "./dashboard";

/** One group per provider with a reading; `nested` marks a window drawn under its parent. */
export function toTimeline(providers: ProviderDashboard[], now: number) {
	const groups = providers
		.map((provider) => {
			const windows = sortWindows(provider.lastReading?.windows ?? [], provider.triggerWindowId);
			return {
				provider,
				rows: windows.map((window) => ({
					provider,
					window,
					timing: windowTiming(window),
					nested: windows.some((parent) => parent.id === parentWindowId[window.id]),
				})),
			};
		})
		.filter((group) => group.rows.length > 0);
	const rows = groups.flatMap((group) => group.rows);
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
	const mondays: number[] = [];
	const monday = new Date(start);
	monday.setDate(monday.getDate() + ((8 - monday.getDay()) % 7));
	while (monday < end) {
		mondays.push(monday.getTime());
		monday.setDate(monday.getDate() + 7);
	}
	return { groups, rows, start: start.getTime(), end: end.getTime(), ticks, mondays, position };
}
