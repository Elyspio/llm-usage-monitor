// PROTOTYPE jetable (#7) — données mock uniquement, aucun backend.
// Les types viennent du vrai contrat `src/shared.ts` ; `ProviderView` est une HYPOTHÈSE de ce que
// le backend pourrait exposer autour d'un `UsageResult` (dernière lecture valide, backoff, etc.).
import type { Provider, UsageResult, UsageWindow } from '../../../src/shared.ts';

export type OkResult = Extract<UsageResult, { ok: true }>;
export type ScenarioKey = 'nominal' | 'quasi-epuise' | 'erreurs' | 'degrade';

export interface ProviderView {
	provider: Provider;
	/** Hypothèse : libellé du plan si le backend sait le lire (Codex expose planType). */
	plan?: string;
	/** Dernier poll, réussi ou non. */
	latest: UsageResult;
	/** Dernière lecture réussie ; affichée « périmée » quand `latest` est en erreur (#5). */
	lastGood: OkResult | null;
	/** Reset précédent par fenêtre, déduit de l'historique côté backend (n'existe pas dans UsageWindow). */
	previousResets: Record<string, string | null>;
	pollEveryMin: number;
	nextPollAt: string;
	/** Backoff en cours après un 429 Claude (15 → 30 → 60 min, #5). */
	backoff: { stepMin: number; retryAt: string } | null;
	/** Fenêtre dont le reset déclenche le prompt auto (seul le 5 h compte, #4). null = pas de 5 h (Codex Pro). */
	triggerWindowId: string | null;
	nextAutoTriggerAt: string | null;
}

export interface TriggerEntry {
	id: string;
	at: string;
	provider: Provider;
	mode: 'auto' | 'manuel';
	outcome: 'succès' | 'échec' | 'en cours';
	detail: string;
	/** Reset 5 h confirmé par relecture après le prompt. */
	newResetAt?: string;
	durationMs?: number;
}

export interface HistorySeries {
	id: string;
	provider: Provider;
	windowId: string;
	/** % restant ; null = pas de lecture valide à cet instant. */
	data: (number | null)[];
}

export interface History {
	times: Date[];
	series: HistorySeries[];
}

export interface Scenario {
	key: ScenarioKey;
	label: string;
	providers: ProviderView[];
	triggers: TriggerEntry[];
	/** 7 jours, un point toutes les 10 min. */
	history: History;
}

export interface VariantProps {
	scenario: Scenario;
	triggers: TriggerEntry[];
	onTrigger: (provider: Provider) => void;
}

export const MIN = 60_000;
/** « Maintenant » figé au chargement : tous les comptes à rebours partent de là (recharger pour réinitialiser). */
export const T0 = Date.now();
const at = (offsetMin: number) => new Date(T0 + offsetMin * MIN).toISOString();

function win(id: string, used: number, resetInMin: number | null, duration: number | null): UsageWindow {
	return {
		id,
		usedPercent: used,
		remainingPercent: Math.max(0, 100 - used),
		resetsAt: resetInMin == null ? null : at(resetInMin),
		windowDurationMinutes: duration,
	};
}

function ok(provider: Provider, fetchedAgoMin: number, windows: UsageWindow[]): OkResult {
	return {
		ok: true,
		provider,
		fetchedAt: at(-fetchedAgoMin),
		remainingPercent: Math.min(...windows.map((w) => w.remainingPercent)),
		windows,
	};
}

function fail(provider: Provider, fetchedAgoMin: number, code: string, message: string): UsageResult {
	return { ok: false, provider, fetchedAt: at(-fetchedAgoMin), error: { code, message } };
}

function prevResets(windows: UsageWindow[], overrides: Record<string, number> = {}) {
	return Object.fromEntries(
		windows.map((w) => [
			w.id,
			w.id in overrides
				? at(overrides[w.id])
				: w.resetsAt && w.windowDurationMinutes
					? new Date(Date.parse(w.resetsAt) - w.windowDurationMinutes * MIN).toISOString()
					: null,
		]),
	);
}

// ---------------------------------------------------------------------------------------------
// Historique synthétique (dents de scie déterministes : la fenêtre se vide puis revient à 100 %).

const STEP_MIN = 10;
const POINTS = (7 * 24 * 60) / STEP_MIN + 1;
const TIMES = Array.from({ length: POINTS }, (_, i) => new Date(T0 - (POINTS - 1 - i) * STEP_MIN * MIN));

function rand(seed: string, n: number): number {
	let h = 2166136261;
	for (const ch of `${seed}:${n}`) {
		h ^= ch.charCodeAt(0);
		h = Math.imul(h, 16777619);
	}
	return ((h >>> 0) % 10_000) / 10_000;
}

const shape = (x: number) => 0.7 * Math.pow(Math.max(0, x), 0.85) + (0.3 * Math.floor(Math.max(0, x) * 8)) / 8;

function remainingAt(seriesId: string, w: UsageWindow, t: number): number {
	if (!w.resetsAt || !w.windowDurationMinutes) return 100 - w.usedPercent;
	const D = w.windowDurationMinutes * MIN;
	const R = Date.parse(w.resetsAt);
	const cycle = Math.floor((R - t) / D); // 0 = fenêtre courante
	const p = (t - (R - (cycle + 1) * D)) / D;
	let peak: number;
	if (cycle === 0) {
		const pNow = Math.max(0.05, (T0 - (R - D)) / D);
		peak = w.usedPercent / shape(Math.min(1, pNow));
	} else {
		peak = D <= 300 * MIN ? 15 + rand(seriesId, cycle) * 80 : 35 + rand(seriesId, cycle) * 55;
	}
	return Math.round(100 - Math.min(100, peak * shape(p)));
}

function buildHistory(basis: { provider: Provider; windows: UsageWindow[]; gapMin: number }[]): History {
	return {
		times: TIMES,
		series: basis.flatMap((b) =>
			b.windows.map((w) => {
				const id = `${b.provider}:${w.id}`;
				const cutoff = T0 - b.gapMin * MIN;
				return {
					id,
					provider: b.provider,
					windowId: w.id,
					data: TIMES.map((d) => (d.getTime() > cutoff ? null : remainingAt(id, w, d.getTime()))),
				};
			}),
		),
	};
}

// ---------------------------------------------------------------------------------------------
// Journal des déclenchements

let seq = 0;
function entry(
	offsetMin: number,
	provider: Provider,
	mode: TriggerEntry['mode'],
	outcome: TriggerEntry['outcome'],
	detail: string,
	extra: { started?: boolean; resetMin?: number; durationMs?: number } = {},
): TriggerEntry {
	return {
		id: `t${seq++}`,
		at: at(offsetMin),
		provider,
		mode,
		outcome,
		detail,
		newResetAt:
			extra.resetMin != null ? at(extra.resetMin) : extra.started ? at(offsetMin + 300) : undefined,
		durationMs: extra.durationMs,
	};
}

const OK_CLAUDE = 'Prompt « 1+1=? » OK (haiku) · fenêtre 5 h démarrée';
const OK_CODEX = 'Tour app-server OK (gpt-5.6-luna) · fenêtre 5 h démarrée';

function olderLog(): TriggerEntry[] {
	return [
		entry(-470, 'claude', 'auto', 'succès', OK_CLAUDE, { started: true, durationMs: 6100 }),
		entry(-440, 'codex', 'auto', 'succès', OK_CODEX, { started: true, durationMs: 7900 }),
		entry(-770, 'claude', 'auto', 'échec', 'TRANSIENT : 529 Overloaded, nouvel essai dans 2 min', { durationMs: 31000 }),
		entry(-768, 'claude', 'auto', 'succès', `${OK_CLAUDE} (2e essai)`, { started: true, durationMs: 5600 }),
		entry(-1210, 'codex', 'auto', 'succès', OK_CODEX, { started: true, durationMs: 8200 }),
		entry(-1380, 'claude', 'manuel', 'succès', 'Fenêtre 5 h déjà active : reset inchangé', { resetMin: -1250, durationMs: 5000 }),
		entry(-1550, 'claude', 'auto', 'succès', OK_CLAUDE, { started: true, durationMs: 5900 }),
		entry(-2890, 'claude', 'auto', 'échec', 'USAGE_LIMIT : limite hebdo atteinte, aucune fenêtre démarrée', { durationMs: 4200 }),
		entry(-3020, 'codex', 'auto', 'succès', OK_CODEX, { started: true, durationMs: 7400 }),
	];
}

function recentLog(claudeStartMin: number, codexStartMin: number, codexRecovered: boolean): TriggerEntry[] {
	const codex = codexRecovered
		? [
				entry(codexStartMin - 54, 'codex', 'auto', 'échec', 'TIMEOUT : aucune réponse du CLI en 120 s', { durationMs: 120000 }),
				entry(codexStartMin, 'codex', 'manuel', 'succès', OK_CODEX, { started: true, durationMs: 7400 }),
			]
		: [entry(codexStartMin, 'codex', 'auto', 'succès', OK_CODEX, { started: true, durationMs: 6900 })];
	return [entry(claudeStartMin, 'claude', 'auto', 'succès', OK_CLAUDE, { started: true, durationMs: 5200 }), ...codex];
}

const byDateDesc = (a: TriggerEntry, b: TriggerEntry) => b.at.localeCompare(a.at);
const olderThan = (min: number) => (t: TriggerEntry) => Date.parse(t.at) <= T0 - min * MIN;

// ---------------------------------------------------------------------------------------------
// Scénarios

const claudeNominal = () => [
	win('five_hour', 32, 130, 300),
	win('seven_day', 41, 4572, 10_080),
	win('seven_day_opus', 12, 4572, 10_080),
	win('seven_day_sonnet', 27, 4572, 10_080),
];
const codexNominal = () => [win('codex/primary', 18, 215, 300), win('codex/secondary', 55, 3420, 10_080)];

function nominal(): Scenario {
	const cw = claudeNominal();
	const xw = codexNominal();
	const claude = ok('claude', 3, cw);
	const codex = ok('codex', 1, xw);
	return {
		key: 'nominal',
		label: 'Nominal',
		providers: [
			{
				provider: 'claude',
				plan: 'Max',
				latest: claude,
				lastGood: claude,
				previousResets: prevResets(cw, { five_hour: -171 }),
				pollEveryMin: 10,
				nextPollAt: at(7),
				backoff: null,
				triggerWindowId: 'five_hour',
				nextAutoTriggerAt: at(131),
			},
			{
				provider: 'codex',
				plan: 'Plus',
				latest: codex,
				lastGood: codex,
				previousResets: prevResets(xw, { 'codex/primary': -140 }),
				pollEveryMin: 5,
				nextPollAt: at(4),
				backoff: null,
				triggerWindowId: 'codex/primary',
				nextAutoTriggerAt: at(216),
			},
		],
		triggers: [...recentLog(-170, -85, true), ...olderLog()].sort(byDateDesc),
		history: buildHistory([
			{ provider: 'claude', windows: cw, gapMin: 0 },
			{ provider: 'codex', windows: xw, gapMin: 0 },
		]),
	};
}

function nearExhaustion(): Scenario {
	const cw = [
		win('five_hour', 94, 38, 300),
		win('seven_day', 88, 840, 10_080),
		win('seven_day_opus', 97, 840, 10_080),
		win('seven_day_sonnet', 71, 840, 10_080),
	];
	const xw = [win('codex/primary', 97, 22, 300), win('codex/secondary', 91, 1560, 10_080)];
	const claude = ok('claude', 2, cw);
	const codex = ok('codex', 0.5, xw);
	return {
		key: 'quasi-epuise',
		label: 'Quasi épuisé',
		providers: [
			{
				provider: 'claude',
				plan: 'Max',
				latest: claude,
				lastGood: claude,
				previousResets: prevResets(cw, { five_hour: -263 }),
				pollEveryMin: 10,
				nextPollAt: at(8),
				backoff: null,
				triggerWindowId: 'five_hour',
				nextAutoTriggerAt: at(39),
			},
			{
				provider: 'codex',
				plan: 'Plus',
				latest: codex,
				lastGood: codex,
				previousResets: prevResets(xw, { 'codex/primary': -279 }),
				// Le TUI officiel accélère son poll près de l'épuisement (#5) : 1 min si une fenêtre ≥ 75 %.
				pollEveryMin: 1,
				nextPollAt: at(0.5),
				backoff: null,
				triggerWindowId: 'codex/primary',
				nextAutoTriggerAt: at(23),
			},
		],
		triggers: [...recentLog(-262, -278, false), ...olderLog()].sort(byDateDesc),
		history: buildHistory([
			{ provider: 'claude', windows: cw, gapMin: 0 },
			{ provider: 'codex', windows: xw, gapMin: 0 },
		]),
	};
}

function errors(): Scenario {
	const lastGoodClaude = ok('claude', 47, [
		win('five_hour', 29, 130, 300),
		win('seven_day', 40, 4572, 10_080),
		win('seven_day_opus', 12, 4572, 10_080),
		win('seven_day_sonnet', 26, 4572, 10_080),
	]);
	const base = [...recentLog(-170, -85, true), ...olderLog()];
	return {
		key: 'erreurs',
		label: 'Erreurs (429, CLI)',
		providers: [
			{
				provider: 'claude',
				plan: 'Max',
				latest: fail('claude', 2, 'RATE_LIMITED', 'Claude usage endpoint returned HTTP 429.'),
				lastGood: lastGoodClaude,
				previousResets: prevResets(lastGoodClaude.windows, { five_hour: -171 }),
				pollEveryMin: 10,
				nextPollAt: at(28),
				backoff: { stepMin: 30, retryAt: at(28) },
				triggerWindowId: 'five_hour',
				nextAutoTriggerAt: at(131),
			},
			{
				provider: 'codex',
				plan: 'Plus',
				latest: fail(
					'codex',
					1,
					'CLI_UNAVAILABLE',
					'Cannot start Codex. Install its CLI or set codexExecutable to its native executable path.',
				),
				lastGood: null,
				previousResets: {},
				pollEveryMin: 5,
				nextPollAt: at(4),
				backoff: null,
				triggerWindowId: 'codex/primary',
				nextAutoTriggerAt: null,
			},
		],
		triggers: [
			...base.filter((t) => t.provider === 'claude' || olderThan(1560)(t)),
			entry(-1559, 'codex', 'auto', 'échec', 'CLI_UNAVAILABLE : spawn codex ENOENT', { durationMs: 40 }),
			entry(-12, 'codex', 'manuel', 'échec', 'CLI_UNAVAILABLE : spawn codex ENOENT', { durationMs: 35 }),
		].sort(byDateDesc),
		history: buildHistory([
			{ provider: 'claude', windows: lastGoodClaude.windows, gapMin: 47 },
			{ provider: 'codex', windows: codexNominal(), gapMin: 1560 },
		]),
	};
}

function degraded(): Scenario {
	// Lecture valide vieille de 8 h 40 : le reset 5 h annoncé à l'époque est déjà passé.
	const lastGoodClaude = ok('claude', 520, [
		win('five_hour', 71, -425, 300),
		win('seven_day', 52, 4572, 10_080),
		win('seven_day_opus', 20, 4572, 10_080),
		win('seven_day_sonnet', 33, 4572, 10_080),
	]);
	// Plan Pro : pas de fenêtre 5 h (#4) ; hebdo sans resetsAt (non démarrée ?).
	const xw = [win('codex/secondary', 0, null, 10_080)];
	const codex = ok('codex', 2, xw);
	const base = [...recentLog(-170, -85, true), ...olderLog()];
	return {
		key: 'degrade',
		label: 'Dégradé (auth, plan Pro)',
		providers: [
			{
				provider: 'claude',
				plan: 'Max',
				latest: fail('claude', 4, 'AUTH_EXPIRED', 'Claude CLI login has expired. Refresh it using the Claude CLI.'),
				lastGood: lastGoodClaude,
				previousResets: prevResets(lastGoodClaude.windows, { five_hour: -726 }),
				pollEveryMin: 10,
				nextPollAt: at(6),
				backoff: null,
				triggerWindowId: 'five_hour',
				nextAutoTriggerAt: null,
			},
			{
				provider: 'codex',
				plan: 'Pro',
				latest: codex,
				lastGood: codex,
				previousResets: { 'codex/secondary': null },
				pollEveryMin: 5,
				nextPollAt: at(3),
				backoff: null,
				triggerWindowId: null,
				nextAutoTriggerAt: null,
			},
		],
		triggers: [
			...base.filter((t) => (t.provider === 'claude' ? olderThan(520)(t) : olderThan(1440)(t))),
			entry(-424, 'claude', 'auto', 'échec', 'AUTH_EXPIRED : Login expired · Please run /login', { durationMs: 2300 }),
		].sort(byDateDesc),
		history: buildHistory([
			{ provider: 'claude', windows: lastGoodClaude.windows, gapMin: 520 },
			{ provider: 'codex', windows: xw, gapMin: 0 },
		]),
	};
}

export const SCENARIOS: Record<ScenarioKey, Scenario> = {
	nominal: nominal(),
	'quasi-epuise': nearExhaustion(),
	erreurs: errors(),
	degrade: degraded(),
};
export const SCENARIO_KEYS = Object.keys(SCENARIOS) as ScenarioKey[];
