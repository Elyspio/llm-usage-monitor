// PROTOTYPE jetable (#7) — Variante A « Cartes par fenêtre » : état instantané.
// Une colonne par provider, une carte + jauge par fenêtre ; historique filtrable par puces ;
// journal court (10 lignes) ; déclenchement manuel AVEC confirmation, désactivé si voué à l'échec.
import AutorenewIcon from '@mui/icons-material/Autorenew';
import BoltIcon from '@mui/icons-material/Bolt';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import TouchAppIcon from '@mui/icons-material/TouchApp';
import {
	Alert,
	AlertTitle,
	Box,
	Button,
	Card,
	CardContent,
	Chip,
	CircularProgress,
	Dialog,
	DialogActions,
	DialogContent,
	DialogContentText,
	DialogTitle,
	Grid,
	Paper,
	Stack,
	Table,
	TableBody,
	TableCell,
	TableHead,
	TableRow,
	ToggleButton,
	ToggleButtonGroup,
	Tooltip,
	Typography,
} from '@mui/material';
import { LineChart } from '@mui/x-charts/LineChart';
import { useMemo, useState, type ReactNode } from 'react';
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
	type Range,
} from './common';
import { fmtAgo, fmtDayLabel, fmtDuration, fmtHour, fmtIn, fmtWhen } from './format';
import { MIN, type ProviderView, type Scenario, type TriggerEntry, type VariantProps } from './mock';

export const name = 'Cartes par fenêtre';

export function VariantA({ scenario, triggers, onTrigger }: VariantProps) {
	const now = useNow();
	return (
		<Box sx={{ p: { xs: 2, md: 3 }, pb: 14, maxWidth: 1440, mx: 'auto' }}>
			<Stack direction="row" sx={{ alignItems: 'baseline', justifyContent: 'space-between', mb: 3 }}>
				<Typography variant="h5" sx={{ fontWeight: 700 }}>
					LLM Usage Monitor
				</Typography>
				<Typography variant="body2" color="text.secondary">
					{fmtWhen(now, now)}
				</Typography>
			</Stack>
			<Grid container spacing={3}>
				{scenario.providers.map((view) => (
					<Grid key={view.provider} size={{ xs: 12, md: 6 }}>
						<ProviderColumn view={view} now={now} pending={isPending(triggers, view.provider)} onTrigger={onTrigger} />
					</Grid>
				))}
				<Grid size={{ xs: 12, lg: 7 }}>
					<HistoryCard scenario={scenario} />
				</Grid>
				<Grid size={{ xs: 12, lg: 5 }}>
					<TriggerTable triggers={triggers} now={now} />
				</Grid>
			</Grid>
		</Box>
	);
}

function ProviderColumn(props: { view: ProviderView; now: number; pending: boolean; onTrigger: (p: Provider) => void }) {
	const { view, now, pending, onTrigger } = props;
	const [confirm, setConfirm] = useState(false);
	const { data, stale, errorCode, message } = shown(view);
	const err = errorCode ? errorInfo(errorCode, view.provider) : null;
	const blocked = err?.blocksTrigger ?? false;
	const triggerWindow = data?.windows.find((w) => w.id === view.triggerWindowId);

	return (
		<Paper variant="outlined" sx={{ p: 2.5, height: '100%', borderTop: 4, borderTopColor: PROVIDER_COLOR[view.provider] }}>
			<Stack direction="row" spacing={2} sx={{ alignItems: 'flex-start', justifyContent: 'space-between', mb: 2 }}>
				<Box sx={{ minWidth: 0 }}>
					<Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
						<Typography variant="h6" sx={{ fontWeight: 700 }}>
							{PROVIDER_LABEL[view.provider]}
						</Typography>
						{view.plan && <Chip size="small" variant="outlined" label={`plan ${view.plan}`} />}
						{err ? <Chip size="small" color={err.severity} label={err.title} /> : <Chip size="small" color="success" label="OK" />}
					</Stack>
					<Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
						{pollLine(view, now)}
					</Typography>
				</Box>
				<Tooltip title={blocked ? `Impossible : ${err!.title.toLowerCase()}` : 'Lance le prompt « 1+1=? » maintenant'}>
					<span>
						<Button
							variant="contained"
							startIcon={<PlayArrowIcon />}
							loading={pending}
							loadingPosition="start"
							disabled={blocked}
							onClick={() => setConfirm(true)}
							sx={{ whiteSpace: 'nowrap' }}
						>
							Déclencher maintenant
						</Button>
					</span>
				</Tooltip>
			</Stack>

			{err && (
				<Alert severity={err.severity} sx={{ mb: 2 }}>
					<AlertTitle>{err.title}</AlertTitle>
					<code>{message}</code>
					<br />
					{err.action}
					<br />
					{data
						? `Valeurs affichées : dernière lecture valide ${fmtWhen(data.fetchedAt, now)} (${fmtAgo(data.fetchedAt, now)}).`
						: 'Aucune lecture valide depuis le démarrage du service.'}
				</Alert>
			)}

			<Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 2, color: 'text.secondary' }}>
				<BoltIcon fontSize="small" />
				<Typography variant="body2">
					{!view.triggerWindowId
						? 'Pas de fenêtre 5 h sur ce plan : déclenchement auto désactivé.'
						: view.nextAutoTriggerAt
							? `Déclenchement auto au prochain reset 5 h : ${fmtWhen(view.nextAutoTriggerAt, now)} (${fmtIn(view.nextAutoTriggerAt, now)})`
							: 'Déclenchement auto suspendu tant que l’erreur persiste.'}
				</Typography>
			</Stack>

			{data ? (
				<Grid container spacing={2}>
					{data.windows.map((w) => (
						<Grid key={w.id} size={{ xs: 12, sm: 6 }}>
							<WindowCard w={w} view={view} stale={stale} now={now} />
						</Grid>
					))}
					{view.provider === 'codex' && !view.triggerWindowId && (
						<Grid size={{ xs: 12, sm: 6 }}>
							<Placeholder title="Fenêtre 5 h absente">
								Le plan {view.plan ?? ''} n’a pas de limite 5 h : rien à déclencher au reset.
							</Placeholder>
						</Grid>
					)}
				</Grid>
			) : (
				<Placeholder title="Aucune donnée">Le service n’a encore obtenu aucune lecture valide pour ce provider.</Placeholder>
			)}

			<Dialog open={confirm} onClose={() => setConfirm(false)}>
				<DialogTitle>Déclencher {PROVIDER_LABEL[view.provider]} maintenant ?</DialogTitle>
				<DialogContent>
					<DialogContentText>{confirmText(view, triggerWindow, now)}</DialogContentText>
				</DialogContent>
				<DialogActions>
					<Button onClick={() => setConfirm(false)}>Annuler</Button>
					<Button
						variant="contained"
						onClick={() => {
							setConfirm(false);
							onTrigger(view.provider);
						}}
					>
						Déclencher
					</Button>
				</DialogActions>
			</Dialog>
		</Paper>
	);
}

function confirmText(view: ProviderView, w: UsageWindow | undefined, now: number): string {
	if (!view.triggerWindowId)
		return 'Aucune fenêtre 5 h sur ce plan : le prompt « 1+1=? » consommera un peu de quota hebdo sans effet sur un reset.';
	if (w?.resetsAt && Date.parse(w.resetsAt) > now)
		return `La fenêtre 5 h est déjà active (reset ${fmtWhen(w.resetsAt, now)}). Le prompt consommera un peu de quota sans déplacer le reset.`;
	return `Le prompt « 1+1=? » démarre une nouvelle fenêtre 5 h : reset prévu vers ${fmtHour(now + 300 * MIN)}.`;
}

function Placeholder({ title, children }: { title: string; children: ReactNode }) {
	return (
		<Box sx={{ border: '1px dashed', borderColor: 'divider', borderRadius: 2, p: 2, height: '100%', color: 'text.secondary' }}>
			<Typography variant="subtitle2" sx={{ fontWeight: 600, color: 'text.primary' }}>
				{title}
			</Typography>
			<Typography variant="body2">{children}</Typography>
		</Box>
	);
}

function Gauge({ value }: { value: number }) {
	return (
		<Box sx={{ position: 'relative', display: 'inline-flex', flexShrink: 0 }}>
			<CircularProgress variant="determinate" value={100} size={88} thickness={5} sx={{ color: 'action.hover', position: 'absolute' }} />
			<CircularProgress variant="determinate" value={value} size={88} thickness={5} color={remainingColor(value)} />
			<Box sx={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', flexDirection: 'column' }}>
				<Typography variant="h6" sx={{ fontWeight: 700, lineHeight: 1 }}>
					{value} %
				</Typography>
				<Typography variant="caption" color="text.secondary">
					restant
				</Typography>
			</Box>
		</Box>
	);
}

function Field({ label, children }: { label: string; children: ReactNode }) {
	return (
		<Box sx={{ mb: 1 }}>
			<Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
				{label}
			</Typography>
			<Typography variant="body2" component="div">
				{children}
			</Typography>
		</Box>
	);
}

function WindowCard({ w, view, stale, now }: { w: UsageWindow; view: ProviderView; stale: boolean; now: number }) {
	const isTrigger = w.id === view.triggerWindowId;
	const prev = view.previousResets[w.id];
	const passed = w.resetsAt != null && Date.parse(w.resetsAt) < now;
	return (
		<Card variant="outlined" sx={{ height: '100%', opacity: stale ? 0.6 : 1, borderStyle: stale ? 'dashed' : 'solid' }}>
			<CardContent>
				<Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', mb: 1.5, flexWrap: 'wrap' }}>
					<Typography variant="subtitle1" sx={{ fontWeight: 600 }}>
						{windowLabel(w.id)}
					</Typography>
					<Chip size="small" label={fmtDuration(w.windowDurationMinutes)} />
					{isTrigger && <Chip size="small" color="primary" variant="outlined" icon={<BoltIcon />} label="déclencheur" />}
					{stale && <Chip size="small" color="warning" variant="outlined" label="périmé" />}
				</Stack>
				<Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
					<Gauge value={w.remainingPercent} />
					<Box sx={{ minWidth: 0 }}>
						<Field label="Prochain reset">
							{w.resetsAt == null ? (
								<Box sx={{ color: 'warning.main' }}>Non annoncé : fenêtre pas encore démarrée ?</Box>
							) : passed ? (
								<>
									{fmtWhen(w.resetsAt, now)}
									<Box sx={{ color: 'warning.main' }}>passé depuis la dernière lecture</Box>
								</>
							) : (
								<>
									{fmtWhen(w.resetsAt, now)}
									<Box sx={{ fontWeight: 700, fontVariantNumeric: 'tabular-nums' }}>{fmtIn(w.resetsAt, now)}</Box>
								</>
							)}
						</Field>
						<Field label="Reset précédent">{prev ? `${fmtWhen(prev, now)} (${fmtAgo(prev, now)})` : '—'}</Field>
					</Box>
				</Stack>
			</CardContent>
		</Card>
	);
}

function HistoryCard({ scenario }: { scenario: Scenario }) {
	const [range, setRange] = useState<Range>('24h');
	const [hidden, setHidden] = useState<string[]>(['claude:seven_day_opus', 'claude:seven_day_sonnet']);
	const h = useMemo(() => sliceHistory(scenario.history, range), [scenario, range]);
	const visible = h.series.filter((s) => !hidden.includes(s.id));
	const toggle = (id: string) => setHidden((x) => (x.includes(id) ? x.filter((i) => i !== id) : [...x, id]));
	const now = Date.now();
	return (
		<Paper variant="outlined" sx={{ p: 2.5, height: '100%' }}>
			<Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
				<Typography variant="h6" sx={{ fontWeight: 700 }}>
					Historique · % restant
				</Typography>
				<ToggleButtonGroup size="small" exclusive value={range} onChange={(_, v: Range | null) => v && setRange(v)}>
					<ToggleButton value="24h">24 h</ToggleButton>
					<ToggleButton value="7j">7 j</ToggleButton>
				</ToggleButtonGroup>
			</Stack>
			<Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', mb: 1 }}>
				{h.series.map((s) => {
					const off = hidden.includes(s.id);
					const color = SERIES_COLORS[s.id] ?? '#999';
					return (
						<Chip
							key={s.id}
							size="small"
							label={seriesLabel(s)}
							variant={off ? 'outlined' : 'filled'}
							onClick={() => toggle(s.id)}
							sx={{ borderColor: color, bgcolor: off ? undefined : `${color}40` }}
						/>
					);
				})}
			</Stack>
			{visible.length ? (
				<LineChart
					height={300}
					skipAnimation
					hideLegend
					grid={{ horizontal: true }}
					xAxis={[
						{
							scaleType: 'time',
							data: h.times,
							valueFormatter: (d: Date) => (range === '24h' ? fmtHour(d) : `${fmtDayLabel(d, now)} ${fmtHour(d)}`),
						},
					]}
					yAxis={[{ min: 0, max: 100, valueFormatter: (v: number) => `${v} %` }]}
					series={visible.map((s) => ({
						id: s.id,
						label: seriesLabel(s),
						data: s.data,
						showMark: false,
						color: SERIES_COLORS[s.id],
					}))}
				/>
			) : (
				<Typography color="text.secondary" sx={{ py: 8, textAlign: 'center' }}>
					Choisir au moins une série.
				</Typography>
			)}
			<Typography variant="caption" color="text.secondary">
				Trous = aucune lecture valide (erreur, 429…). 24 h : un point / 10 min · 7 j : un point / heure.
			</Typography>
		</Paper>
	);
}

function OutcomeChip({ t }: { t: TriggerEntry }) {
	if (t.outcome === 'en cours') return <Chip size="small" icon={<CircularProgress size={12} />} label="en cours" />;
	return <Chip size="small" color={t.outcome === 'succès' ? 'success' : 'error'} variant="outlined" label={t.outcome} />;
}

function TriggerTable({ triggers, now }: { triggers: TriggerEntry[]; now: number }) {
	const rows = triggers.slice(0, 10);
	return (
		<Paper variant="outlined" sx={{ p: 2.5, height: '100%' }}>
			<Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
				<Typography variant="h6" sx={{ fontWeight: 700 }}>
					Journal des déclenchements
				</Typography>
				<Typography variant="caption" color="text.secondary">
					10 derniers sur {triggers.length}
				</Typography>
			</Stack>
			<Box sx={{ overflowX: 'auto' }}>
				<Table size="small">
					<TableHead>
						<TableRow>
							<TableCell>Quand</TableCell>
							<TableCell>Provider</TableCell>
							<TableCell>Mode</TableCell>
							<TableCell>Résultat</TableCell>
						</TableRow>
					</TableHead>
					<TableBody>
						{rows.map((t) => (
							<TableRow key={t.id}>
								<TableCell sx={{ whiteSpace: 'nowrap', verticalAlign: 'top' }}>
									{fmtWhen(t.at, now)}
									<Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
										{fmtAgo(t.at, now)}
									</Typography>
								</TableCell>
								<TableCell sx={{ verticalAlign: 'top', color: PROVIDER_COLOR[t.provider], fontWeight: 600 }}>
									{PROVIDER_LABEL[t.provider]}
								</TableCell>
								<TableCell sx={{ verticalAlign: 'top' }}>
									<Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
										{t.mode === 'auto' ? <AutorenewIcon fontSize="small" /> : <TouchAppIcon fontSize="small" />}
										<span>{t.mode}</span>
									</Stack>
								</TableCell>
								<TableCell sx={{ verticalAlign: 'top' }}>
									<OutcomeChip t={t} />
									<Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
										{t.detail}
										{t.newResetAt ? ` · reset → ${fmtHour(t.newResetAt)}` : ''}
									</Typography>
								</TableCell>
							</TableRow>
						))}
					</TableBody>
				</Table>
			</Box>
		</Paper>
	);
}
