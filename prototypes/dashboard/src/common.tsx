// PROTOTYPE jetable (#7) — petits utilitaires partagés (pas de layout partagé entre variantes).
import { useEffect, useState } from 'react';
import type { Provider, UsageWindow } from '../../../src/shared.ts';
import { fmtAgo, fmtIn } from './format';
import { MIN, type History, type HistorySeries, type OkResult, type ProviderView, type TriggerEntry } from './mock';

export function useNow(intervalMs = 1000): number {
	const [now, setNow] = useState(() => Date.now());
	useEffect(() => {
		const id = setInterval(() => setNow(Date.now()), intervalMs);
		return () => clearInterval(id);
	}, [intervalMs]);
	return now;
}

export const PROVIDER_LABEL: Record<Provider, string> = { claude: 'Claude', codex: 'Codex' };
export const PROVIDER_COLOR: Record<Provider, string> = { claude: '#d97757', codex: '#10a37f' };

const WINDOW_LABELS: Record<string, string> = {
	five_hour: 'Session 5 h',
	seven_day: 'Hebdo · tous modèles',
	seven_day_opus: 'Hebdo · Opus',
	seven_day_sonnet: 'Hebdo · Sonnet',
	'codex/primary': 'Fenêtre 5 h',
	'codex/secondary': 'Hebdo',
};
export const windowLabel = (id: string) => WINDOW_LABELS[id] ?? id;

export const remainingColor = (r: number): 'success' | 'warning' | 'error' =>
	r >= 50 ? 'success' : r >= 20 ? 'warning' : 'error';

export interface ErrorInfo {
	title: string;
	action: string;
	severity: 'error' | 'warning';
	/** Le déclenchement ne peut pas réussir tant que l'erreur persiste. */
	blocksTrigger: boolean;
}

export function errorInfo(code: string, provider: Provider): ErrorInfo {
	switch (code) {
		case 'AUTH_EXPIRED':
			return {
				title: 'Connexion expirée',
				action:
					provider === 'claude'
						? 'Relancer « claude auth login » sur l’hôte du service.'
						: 'Relancer « codex login --device-auth » sur l’hôte du service.',
				severity: 'error',
				blocksTrigger: true,
			};
		case 'RATE_LIMITED':
			return {
				title: 'Limité par le provider (429)',
				action: 'Pas de nouvel essai immédiat : backoff 15 → 30 → 60 min.',
				severity: 'warning',
				blocksTrigger: false,
			};
		case 'CLI_UNAVAILABLE':
			return {
				title: 'CLI introuvable',
				action: 'Installer le CLI ou corriger le PATH du service.',
				severity: 'error',
				blocksTrigger: true,
			};
		default:
			return { title: code, action: 'Voir les logs du service.', severity: 'error', blocksTrigger: false };
	}
}

/** Ce qu'on affiche : la lecture courante si OK, sinon la dernière lecture valide (périmée). */
export function shown(view: ProviderView): {
	data: OkResult | null;
	stale: boolean;
	errorCode: string | null;
	message: string | null;
} {
	if (view.latest.ok) return { data: view.latest, stale: false, errorCode: null, message: null };
	return { data: view.lastGood, stale: true, errorCode: view.latest.error.code, message: view.latest.error.message };
}

export function windowStart(w: UsageWindow): number | null {
	if (!w.resetsAt || !w.windowDurationMinutes) return null;
	return Date.parse(w.resetsAt) - w.windowDurationMinutes * MIN;
}

export function pollLine(view: ProviderView, now: number): string {
	if (view.backoff)
		return `Lu ${fmtAgo(view.latest.fetchedAt, now)} · backoff ${view.backoff.stepMin} min après 429 · nouvel essai ${fmtIn(view.backoff.retryAt, now)}`;
	const accel = view.provider === 'codex' && view.pollEveryMin < 5 ? ' (accéléré, fenêtre ≥ 75 %)' : '';
	return `Lu ${fmtAgo(view.latest.fetchedAt, now)} · prochain poll ${fmtIn(view.nextPollAt, now)} · toutes les ${view.pollEveryMin} min${accel}`;
}

export const isPending = (triggers: TriggerEntry[], provider: Provider) =>
	triggers.some((t) => t.provider === provider && t.outcome === 'en cours');

// --- Historique ---------------------------------------------------------------------------------

export type Range = '24h' | '7j';

export function sliceHistory(h: History, range: Range): History {
	const step = range === '24h' ? 1 : 6;
	const from = range === '24h' ? h.times.length - 145 : 0;
	const idx: number[] = [];
	for (let i = from; i < h.times.length; i += step) idx.push(i);
	if (idx[idx.length - 1] !== h.times.length - 1) idx.push(h.times.length - 1);
	return {
		times: idx.map((i) => h.times[i]),
		series: h.series.map((s) => ({ ...s, data: idx.map((i) => s.data[i]) })),
	};
}

export const seriesLabel = (s: HistorySeries) => `${PROVIDER_LABEL[s.provider]} · ${windowLabel(s.windowId)}`;

export const SERIES_COLORS: Record<string, string> = {
	'claude:five_hour': '#d97757',
	'claude:seven_day': '#f4c1a9',
	'claude:seven_day_opus': '#9c5a44',
	'claude:seven_day_sonnet': '#e6a15c',
	'codex:codex/primary': '#10a37f',
	'codex:codex/secondary': '#8fe3c8',
};
