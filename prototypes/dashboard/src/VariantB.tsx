// PROTOTYPE jetable (#7) — Variante B « Chronologie et rythme » : lecture temporelle, dense.
// Bandeau d'incidents global ; une ligne par fenêtre (barre consommée vs temps écoulé → rythme) ;
// grand graphe avec les déclenchements en repères verticaux ; panneau latéral fixe « Déclencheur »
// avec split-button SANS confirmation (jamais désactivé : l'échec apparaît dans le journal) et
// journal complet filtrable, groupé par jour.
import ArrowDropDownIcon from '@mui/icons-material/ArrowDropDown';
import BoltIcon from '@mui/icons-material/Bolt';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import {
	Alert,
	Box,
	Button,
	ButtonGroup,
	Chip,
	Divider,
	Menu,
	MenuItem,
	Paper,
	Stack,
	ToggleButton,
	ToggleButtonGroup,
	Typography,
} from '@mui/material';
import { ChartsReferenceLine } from '@mui/x-charts/ChartsReferenceLine';
import { LineChart } from '@mui/x-charts/LineChart';
import { useMemo, useState } from 'react';
import type { Provider, UsageWindow } from '../../../src/shared.ts';
import {
	PROVIDER_COLOR,
	PROVIDER_LABEL,
	SERIES_COLORS,
	errorInfo,
	isPending,
	pollLine,
	remainingColor,
	seriesLabel,
	shown,
	sliceHistory,
	useNow,
	windowLabel,
	windowStart,
	type Range,
} from './common';
import { fmtAgo, fmtDayLabel, fmtDuration, fmtHour, fmtIn, fmtSpan, fmtWhen } from './format';
import type { ProviderView, Scenario, TriggerEntry, VariantProps } from './mock';

export const name = 'Chronologie et rythme';

const mono = { fontVariantNumeric: 'tabular-nums' } as const;

export function VariantB({ scenario, triggers, onTrigger }: VariantProps) {
	const now = useNow();
	return (
		<Box sx={{ minHeight: '100vh', display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(0,1fr) 380px' } }}>
			<Box sx={{ p: { xs: 2, md: 3 }, pb: 14, minWidth: 0 }}>
				<TopBar scenario={scenario} now={now} />
				<Incidents scenario={scenario} now={now} />
				<WindowsTimeline scenario={scenario} now={now} />
				<HistoryPanel scenario={scenario} triggers={triggers} />
			</Box>
			<Box
				component="aside"
				sx={{
					borderLeft: { lg: 1 },
					borderColor: 'divider',
					bgcolor: 'background.paper',
					p: 2.5,
					pb: 14,
					position: { lg: 'sticky' },
					top: 0,
					height: { lg: '100vh' },
					overflow: 'auto',
				}}
			>
				<TriggerPanel scenario={scenario} triggers={triggers} onTrigger={onTrigger} now={now} />
			</Box>
		</Box>
	);
}

function statusColor(view: ProviderView): string {
	if (view.latest.ok) return '#66bb6a';
	return errorInfo(view.latest.error.code, view.provider).severity === 'warning' ? '#ffa726' : '#ef5350';
}

function TopBar({ scenario, now }: { scenario: Scenario; now: number }) {
	return (
		<Stack direction="row" spacing={3} useFlexGap sx={{ alignItems: 'center', mb: 2, flexWrap: 'wrap' }}>
			<Typography variant="h6" sx={{ fontWeight: 700, mr: 'auto' }}>
				LLM Usage Monitor
			</Typography>
			{scenario.providers.map((v) => (
				<Stack key={v.provider} direction="row" spacing={1} sx={{ alignItems: 'center' }}>
					<Box sx={{ width: 10, height: 10, borderRadius: '50%', bgcolor: statusColor(v) }} />
					<Typography variant="body2" sx={{ fontWeight: 600 }}>
						{PROVIDER_LABEL[v.provider]}
					</Typography>
					<Typography variant="body2" color="text.secondary">
						lu {fmtAgo(v.latest.fetchedAt, now)}
					</Typography>
				</Stack>
			))}
			<Typography variant="body2" color="text.secondary" sx={mono}>
				{fmtHour(now)}
			</Typography>
		</Stack>
	);
}

function Incidents({ scenario, now }: { scenario: Scenario; now: number }) {
	const issues = scenario.providers.filter((v) => !v.latest.ok);
	if (!issues.length) return null;
	return (
		<Stack spacing={1} sx={{ mb: 2 }}>
			{issues.map((v) => {
				if (v.latest.ok) return null;
				const info = errorInfo(v.latest.error.code, v.provider);
				return (
					<Alert key={v.provider} severity={info.severity} variant="outlined" sx={{ py: 0 }}>
						<b>{PROVIDER_LABEL[v.provider]} — {info.title}</b> ({v.latest.error.code}) ·{' '}
						{v.lastGood ? `dernière lecture valide ${fmtAgo(v.lastGood.fetchedAt, now)}` : 'aucune lecture valide'}
						{v.backoff ? ` · nouvel essai ${fmtIn(v.backoff.retryAt, now)}` : ''} · {info.action}
					</Alert>
				);
			})}
		</Stack>
	);
}

function WindowsTimeline({ scenario, now }: { scenario: Scenario; now: number }) {
	return (
		<Paper variant="outlined" sx={{ p: 2.5, mb: 2 }}>
			<Stack direction="row" sx={{ alignItems: 'baseline', justifyContent: 'space-between', mb: 1 }}>
				<Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
					Fenêtres en cours
				</Typography>
				<Typography variant="caption" color="text.secondary">
					barre = % consommé · trait blanc = temps écoulé dans la fenêtre
				</Typography>
			</Stack>
			{scenario.providers.map((view) => {
				const { data, stale, errorCode } = shown(view);
				return (
					<Box key={view.provider} sx={{ mt: 2 }}>
						<Stack direction="row" spacing={1.5} useFlexGap sx={{ alignItems: 'baseline', flexWrap: 'wrap' }}>
							<Typography sx={{ fontWeight: 700, color: PROVIDER_COLOR[view.provider] }}>{PROVIDER_LABEL[view.provider]}</Typography>
							{view.plan && (
								<Typography variant="caption" color="text.secondary">
									plan {view.plan}
								</Typography>
							)}
							<Typography variant="caption" color="text.secondary">
								{pollLine(view, now)}
							</Typography>
						</Stack>
						{data?.windows.map((w) => <WindowRow key={w.id} w={w} view={view} stale={stale} now={now} />)}
						{data && view.provider === 'codex' && !view.triggerWindowId && (
							<MutedRow text={`Fenêtre 5 h — absente sur ce plan (${view.plan ?? '?'}) : pas de déclenchement auto`} />
						)}
						{!data && <MutedRow text={`Aucune lecture valide — ${errorCode ? errorInfo(errorCode, view.provider).title : ''}`} />}
					</Box>
				);
			})}
		</Paper>
	);
}

function MutedRow({ text }: { text: string }) {
	return (
		<Box sx={{ py: 1.5, borderTop: 1, borderColor: 'divider', color: 'text.secondary', fontStyle: 'italic' }}>
			<Typography variant="body2">{text}</Typography>
		</Box>
	);
}

function WindowRow({ w, view, stale, now }: { w: UsageWindow; view: ProviderView; stale: boolean; now: number }) {
	const start = windowStart(w);
	const R = w.resetsAt ? Date.parse(w.resetsAt) : null;
	const passed = R != null && R < now;
	const elapsed = start != null && R != null && !passed ? Math.min(1, Math.max(0, (now - start) / (R - start))) : null;
	const color = remainingColor(w.remainingPercent);
	const pace = elapsed != null ? w.usedPercent - elapsed * 100 : null;
	const prev = view.previousResets[w.id];
	return (
		<Box
			sx={{
				display: 'grid',
				gridTemplateColumns: { xs: '1fr', md: '190px minmax(0,1fr) 100px 190px' },
				gap: 2,
				alignItems: 'center',
				py: 1.25,
				borderTop: 1,
				borderColor: 'divider',
				opacity: stale ? 0.55 : 1,
			}}
		>
			<Box>
				<Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
					<Typography variant="body2" sx={{ fontWeight: 600 }}>
						{windowLabel(w.id)}
					</Typography>
					{w.id === view.triggerWindowId && <BoltIcon fontSize="small" color="primary" titleAccess="Fenêtre du déclencheur auto" />}
				</Stack>
				<Typography variant="caption" color="text.secondary">
					{fmtDuration(w.windowDurationMinutes)}
					{stale ? ' · périmé' : ''}
				</Typography>
			</Box>
			<Box>
				<Box sx={{ position: 'relative', height: 14 }}>
					<Box
						sx={{
							position: 'absolute',
							inset: 0,
							borderRadius: 7,
							overflow: 'hidden',
							bgcolor: 'action.hover',
							border: R == null ? '1px dashed rgba(255,255,255,.3)' : undefined,
							backgroundImage: stale ? 'repeating-linear-gradient(45deg, transparent 0 6px, rgba(255,255,255,.08) 6px 12px)' : undefined,
						}}
					>
						<Box sx={{ height: '100%', width: `${w.usedPercent}%`, bgcolor: `${color}.main` }} />
					</Box>
					{elapsed != null && (
						<Box sx={{ position: 'absolute', top: -4, bottom: -4, left: `${elapsed * 100}%`, width: 2, bgcolor: 'common.white' }} />
					)}
				</Box>
				<Stack direction="row" sx={{ justifyContent: 'space-between', mt: 0.5, gap: 1 }}>
					<Typography variant="caption" color="text.secondary">
						{start != null ? `début ${fmtWhen(start, now)}` : 'pas de reset annoncé (non démarrée ?)'}
					</Typography>
					<Typography variant="caption" sx={{ color: pace != null && pace > 10 ? 'warning.main' : 'text.secondary', textAlign: 'right' }}>
						{pace != null
							? `${w.usedPercent} % consommé pour ${Math.round(elapsed! * 100)} % du temps${pace > 10 ? ' · rythme élevé' : ''}`
							: passed
								? 'reset passé depuis la dernière lecture'
								: ''}
					</Typography>
				</Stack>
			</Box>
			<Box sx={{ textAlign: { md: 'right' } }}>
				<Typography sx={{ ...mono, fontSize: 28, fontWeight: 700, lineHeight: 1, color: `${color}.main` }}>{w.remainingPercent} %</Typography>
				<Typography variant="caption" color="text.secondary">
					restant
				</Typography>
			</Box>
			<Box>
				<Typography sx={{ ...mono, fontWeight: 600 }}>
					{R == null ? 'reset non annoncé' : passed ? 'reset passé' : `reset dans ${fmtSpan(R - now, true)}`}
				</Typography>
				<Typography variant="caption" color="text.secondary">
					{R != null ? fmtWhen(R, now) : '—'}
					{prev ? ` · préc. ${fmtWhen(prev, now)}` : ''}
				</Typography>
			</Box>
		</Box>
	);
}

function HistoryPanel({ scenario, triggers }: { scenario: Scenario; triggers: TriggerEntry[] }) {
	const [range, setRange] = useState<Range>('24h');
	const [scope, setScope] = useState<'5h' | 'tout'>('5h');
	const h = useMemo(() => sliceHistory(scenario.history, range), [scenario, range]);
	const series = h.series.filter((s) => scope === 'tout' || s.windowId === 'five_hour' || s.windowId === 'codex/primary');
	const from = h.times[0].getTime();
	const marks = triggers.filter((t) => Date.parse(t.at) >= from && t.outcome !== 'en cours');
	const now = Date.now();
	return (
		<Paper variant="outlined" sx={{ p: 2.5 }}>
			<Stack direction="row" spacing={2} useFlexGap sx={{ alignItems: 'center', mb: 1, flexWrap: 'wrap' }}>
				<Typography variant="subtitle1" sx={{ fontWeight: 700, mr: 'auto' }}>
					Historique · % restant et déclenchements
				</Typography>
				<ToggleButtonGroup size="small" exclusive value={scope} onChange={(_, v: '5h' | 'tout' | null) => v && setScope(v)}>
					<ToggleButton value="5h">Fenêtres 5 h</ToggleButton>
					<ToggleButton value="tout">Toutes</ToggleButton>
				</ToggleButtonGroup>
				<ToggleButtonGroup size="small" exclusive value={range} onChange={(_, v: Range | null) => v && setRange(v)}>
					<ToggleButton value="24h">24 h</ToggleButton>
					<ToggleButton value="7j">7 j</ToggleButton>
				</ToggleButtonGroup>
			</Stack>
			{series.length ? (
				<LineChart
					height={340}
					skipAnimation
					grid={{ horizontal: true, vertical: true }}
					xAxis={[
						{
							scaleType: 'time',
							data: h.times,
							valueFormatter: (d: Date) => (range === '24h' ? fmtHour(d) : `${fmtDayLabel(d, now)} ${fmtHour(d)}`),
						},
					]}
					yAxis={[{ min: 0, max: 100, valueFormatter: (v: number) => `${v} %` }]}
					series={series.map((s) => ({ id: s.id, label: seriesLabel(s), data: s.data, showMark: false, area: scope === '5h', color: SERIES_COLORS[s.id] }))}
				>
					{marks.map((t) => (
						<ChartsReferenceLine
							key={t.id}
							x={new Date(t.at)}
							lineStyle={{
								stroke: t.outcome === 'succès' ? PROVIDER_COLOR[t.provider] : '#ef5350',
								strokeDasharray: t.mode === 'auto' ? '4 3' : undefined,
								strokeOpacity: 0.8,
							}}
						/>
					))}
				</LineChart>
			) : (
				<Typography color="text.secondary" sx={{ py: 8, textAlign: 'center' }}>
					Aucune série pour ce filtre (pas de fenêtre 5 h ?).
				</Typography>
			)}
			<Typography variant="caption" color="text.secondary">
				Repères verticaux : déclenchements (pointillé = auto, plein = manuel, rouge = échec). Trous = aucune lecture valide.
			</Typography>
		</Paper>
	);
}

type Target = Provider | 'both';
const TARGET_LABEL: Record<Target, string> = { claude: 'Claude', codex: 'Codex', both: 'les deux' };
type Filter = 'tous' | 'auto' | 'manuel' | 'échecs';

function TriggerPanel({ scenario, triggers, onTrigger, now }: VariantProps & { now: number }) {
	const [target, setTarget] = useState<Target>('both');
	const [anchor, setAnchor] = useState<HTMLElement | null>(null);
	const [filter, setFilter] = useState<Filter>('tous');
	const providers: Provider[] = target === 'both' ? ['claude', 'codex'] : [target];
	const busy = providers.every((p) => isPending(triggers, p));
	const fire = () => providers.filter((p) => !isPending(triggers, p)).forEach(onTrigger);

	const rows = triggers.filter((t) =>
		filter === 'tous' ? true : filter === 'échecs' ? t.outcome === 'échec' : t.mode === filter,
	);
	const groups = new Map<string, TriggerEntry[]>();
	for (const t of rows) {
		const k = fmtDayLabel(t.at, now);
		groups.set(k, [...(groups.get(k) ?? []), t]);
	}

	return (
		<>
			<Typography variant="overline" color="text.secondary">
				Déclencheur
			</Typography>
			<Stack spacing={1.25} sx={{ mb: 2 }}>
				{scenario.providers.map((v) => {
					const blocked = !v.latest.ok && errorInfo(v.latest.error.code, v.provider).blocksTrigger;
					return (
						<Box key={v.provider}>
							<Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
								<Typography variant="body2" sx={{ fontWeight: 700, color: PROVIDER_COLOR[v.provider] }}>
									{PROVIDER_LABEL[v.provider]}
								</Typography>
								{blocked && <WarningAmberIcon fontSize="small" color="error" />}
							</Stack>
							<Typography variant="body2" sx={mono}>
								{!v.triggerWindowId
									? 'Auto désactivé : pas de fenêtre 5 h'
									: v.nextAutoTriggerAt
										? `Auto ${fmtIn(v.nextAutoTriggerAt, now)} (${fmtHour(v.nextAutoTriggerAt)})`
										: 'Auto suspendu (erreur en cours)'}
							</Typography>
						</Box>
					);
				})}
			</Stack>
			<ButtonGroup variant="contained" fullWidth>
				<Button onClick={fire} loading={busy} loadingPosition="start" sx={{ flex: 1 }}>
					Déclencher maintenant · {TARGET_LABEL[target]}
				</Button>
				<Button sx={{ maxWidth: 44 }} onClick={(e) => setAnchor(e.currentTarget)} aria-label="Choisir la cible">
					<ArrowDropDownIcon />
				</Button>
			</ButtonGroup>
			<Menu anchorEl={anchor} open={!!anchor} onClose={() => setAnchor(null)}>
				{(['both', 'claude', 'codex'] as Target[]).map((o) => (
					<MenuItem
						key={o}
						selected={o === target}
						onClick={() => {
							setTarget(o);
							setAnchor(null);
						}}
					>
						{TARGET_LABEL[o]}
					</MenuItem>
				))}
			</Menu>
			<Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
				Sans confirmation : chaque clic lance « 1+1=? » (un seul process CLI à la fois par provider).
			</Typography>

			<Divider sx={{ my: 2 }} />
			<Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1 }}>
				<Typography variant="overline" color="text.secondary" sx={{ mr: 'auto' }}>
					Journal ({rows.length})
				</Typography>
			</Stack>
			<Stack direction="row" spacing={0.5} useFlexGap sx={{ flexWrap: 'wrap', mb: 1.5 }}>
				{(['tous', 'auto', 'manuel', 'échecs'] as Filter[]).map((f) => (
					<Chip key={f} size="small" label={f} color={f === filter ? 'primary' : 'default'} onClick={() => setFilter(f)} />
				))}
			</Stack>
			{[...groups.entries()].map(([day, items]) => (
				<Box key={day} sx={{ mb: 1 }}>
					<Typography variant="caption" color="text.secondary" sx={{ fontWeight: 700, textTransform: 'uppercase' }}>
						{day}
					</Typography>
					{items.map((t) => (
						<Box key={t.id} sx={{ display: 'grid', gridTemplateColumns: '12px 42px 1fr', gap: 1, py: 0.6 }}>
							<Box
								sx={{
									width: 10,
									height: 10,
									mt: 0.6,
									borderRadius: '50%',
									bgcolor: t.outcome === 'succès' ? '#66bb6a' : t.outcome === 'échec' ? '#ef5350' : '#bdbdbd',
								}}
							/>
							<Typography variant="body2" color="text.secondary" sx={mono}>
								{fmtHour(t.at)}
							</Typography>
							<Box sx={{ minWidth: 0 }}>
								<Typography variant="body2">
									<Box component="span" sx={{ fontWeight: 700, color: PROVIDER_COLOR[t.provider] }}>
										{PROVIDER_LABEL[t.provider]}
									</Box>{' '}
									· {t.mode} · {t.outcome}
								</Typography>
								<Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
									{t.detail}
									{t.newResetAt ? ` · reset → ${fmtHour(t.newResetAt)}` : ''}
									{t.durationMs ? ` · ${(t.durationMs / 1000).toFixed(1)} s` : ''}
								</Typography>
							</Box>
						</Box>
					))}
				</Box>
			))}
		</>
	);
}
