// PROTOTYPE jetable (#7) — deux variantes du dashboard sur une seule page, switchables via ?variant=A|B
// et un scénario mock via ?scenario=nominal|quasi-epuise|erreurs|degrade (barre flottante, ou ← / →).
import { CssBaseline, ThemeProvider, createTheme } from '@mui/material';
import { StrictMode, useCallback, useEffect, useMemo, useState } from 'react';
import { createRoot } from 'react-dom/client';
import type { Provider } from '../../../src/shared.ts';
import { MIN, SCENARIOS, SCENARIO_KEYS, type Scenario, type ScenarioKey, type TriggerEntry } from './mock';
import { PrototypeBar } from './PrototypeBar';
import { VariantA, name as nameA } from './VariantA';
import { VariantB, name as nameB } from './VariantB';

const VARIANTS = [
	{ key: 'A', name: nameA, Component: VariantA },
	{ key: 'B', name: nameB, Component: VariantB },
];

const theme = createTheme({
	palette: {
		mode: 'dark',
		primary: { main: '#8ab4f8' },
		background: { default: '#0e1116', paper: '#151a21' },
	},
	shape: { borderRadius: 10 },
	typography: { fontFamily: '"Inter", "Segoe UI", system-ui, sans-serif' },
	components: { MuiPaper: { styleOverrides: { root: { backgroundImage: 'none' } } } },
});

function useUrlParam<T extends string>(name: string, allowed: readonly T[], fallback: T): [T, (v: T) => void] {
	const [value, setValue] = useState<T>(() => {
		const v = new URLSearchParams(location.search).get(name) as T | null;
		return v && allowed.includes(v) ? v : fallback;
	});
	const set = useCallback(
		(v: T) => {
			const url = new URL(location.href);
			url.searchParams.set(name, v);
			history.replaceState(null, '', url);
			setValue(v);
		},
		[name],
	);
	return [value, set];
}

/** Stub du déclenchement manuel : aucune mutation réelle, résultat déduit du scénario après 1,8 s. */
function simulate(scenario: Scenario, provider: Provider): Pick<TriggerEntry, 'outcome' | 'detail' | 'newResetAt' | 'durationMs'> {
	const view = scenario.providers.find((p) => p.provider === provider)!;
	const code = view.latest.ok ? null : view.latest.error.code;
	if (code === 'AUTH_EXPIRED')
		return { outcome: 'échec', detail: 'AUTH_EXPIRED : Login expired · Please run /login', durationMs: 2100 };
	if (code === 'CLI_UNAVAILABLE')
		return { outcome: 'échec', detail: `CLI_UNAVAILABLE : spawn ${provider} ENOENT`, durationMs: 40 };
	if (!view.triggerWindowId)
		return { outcome: 'succès', detail: 'Prompt OK · aucune fenêtre 5 h sur ce plan', durationMs: 6400 };
	const w = view.lastGood?.windows.find((x) => x.id === view.triggerWindowId);
	if (w?.resetsAt && Date.parse(w.resetsAt) > Date.now())
		return { outcome: 'succès', detail: 'Fenêtre 5 h déjà active : reset inchangé', newResetAt: w.resetsAt, durationMs: 5300 };
	return {
		outcome: 'succès',
		detail: 'Prompt « 1+1=? » OK · fenêtre 5 h démarrée',
		newResetAt: new Date(Date.now() + 300 * MIN).toISOString(),
		durationMs: 5900,
	};
}

function App() {
	const keys = VARIANTS.map((v) => v.key);
	const [variant, setVariant] = useUrlParam('variant', keys, 'A');
	const [scenarioKey, setScenarioKey] = useUrlParam<ScenarioKey>('scenario', SCENARIO_KEYS, 'nominal');
	const scenario = SCENARIOS[scenarioKey];
	// Déclenchements manuels faits pendant la session, par scénario (en mémoire uniquement).
	const [manual, setManual] = useState<Partial<Record<ScenarioKey, TriggerEntry[]>>>({});

	const triggers = useMemo(
		() => [...(manual[scenarioKey] ?? []), ...scenario.triggers].sort((a, b) => b.at.localeCompare(a.at)),
		[manual, scenarioKey, scenario],
	);

	const onTrigger = useCallback(
		(provider: Provider) => {
			const id = `m${Date.now()}-${provider}`;
			const pending: TriggerEntry = {
				id,
				at: new Date().toISOString(),
				provider,
				mode: 'manuel',
				outcome: 'en cours',
				detail: 'Prompt « 1+1=? » en cours…',
			};
			setManual((m) => ({ ...m, [scenarioKey]: [pending, ...(m[scenarioKey] ?? [])] }));
			setTimeout(() => {
				const result = simulate(scenario, provider);
				setManual((m) => ({
					...m,
					[scenarioKey]: (m[scenarioKey] ?? []).map((t) => (t.id === id ? { ...t, ...result } : t)),
				}));
			}, 1800);
		},
		[scenario, scenarioKey],
	);

	const index = keys.indexOf(variant);
	const cycle = useCallback((delta: number) => setVariant(keys[(index + delta + keys.length) % keys.length]), [index]);

	useEffect(() => {
		const onKey = (e: KeyboardEvent) => {
			const el = e.target as HTMLElement | null;
			if (el?.closest('input, textarea, [contenteditable="true"]')) return;
			if (e.key === 'ArrowLeft') cycle(-1);
			if (e.key === 'ArrowRight') cycle(1);
		};
		window.addEventListener('keydown', onKey);
		return () => window.removeEventListener('keydown', onKey);
	}, [cycle]);

	const { Component, name } = VARIANTS[index];
	return (
		<>
			<Component key={`${variant}-${scenarioKey}`} scenario={scenario} triggers={triggers} onTrigger={onTrigger} />
			{/* Le prototype n'est jamais déployé ; la barre reste visible aussi en build pour pouvoir changer de scénario. */}
			<PrototypeBar
				variantLabel={`${variant} — ${name}`}
				onPrev={() => cycle(-1)}
				onNext={() => cycle(1)}
				scenarios={SCENARIO_KEYS.map((k) => ({ key: k, label: SCENARIOS[k].label }))}
				scenario={scenarioKey}
				onScenario={setScenarioKey}
			/>
		</>
	);
}

createRoot(document.getElementById('root')!).render(
	<StrictMode>
		<ThemeProvider theme={theme}>
			<CssBaseline />
			<App />
		</ThemeProvider>
	</StrictMode>,
);
