// PROTOTYPE jetable (#7) — barre flottante de choix de variante / scénario. Ne fait pas partie du design évalué.
import ChevronLeftIcon from '@mui/icons-material/ChevronLeft';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import { Divider, IconButton, Paper, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import type { ScenarioKey } from './mock';

interface Props {
	variantLabel: string;
	onPrev: () => void;
	onNext: () => void;
	scenarios: { key: ScenarioKey; label: string }[];
	scenario: ScenarioKey;
	onScenario: (key: ScenarioKey) => void;
}

export function PrototypeBar({ variantLabel, onPrev, onNext, scenarios, scenario, onScenario }: Props) {
	return (
		<Paper
			elevation={12}
			sx={{
				position: 'fixed',
				bottom: 16,
				left: '50%',
				transform: 'translateX(-50%)',
				zIndex: 2000,
				display: 'flex',
				alignItems: 'center',
				gap: 0.5,
				px: 1.5,
				py: 0.5,
				borderRadius: 999,
				bgcolor: '#fdd835',
				color: '#111',
				maxWidth: 'calc(100vw - 32px)',
				overflowX: 'auto',
				backgroundImage: 'none',
			}}
		>
			<Typography variant="caption" sx={{ fontWeight: 800, letterSpacing: 1, pr: 0.5 }}>
				PROTOTYPE
			</Typography>
			<IconButton size="small" onClick={onPrev} sx={{ color: 'inherit' }} aria-label="Variante précédente (←)">
				<ChevronLeftIcon />
			</IconButton>
			<Typography variant="body2" sx={{ fontWeight: 700, whiteSpace: 'nowrap', minWidth: 190, textAlign: 'center' }}>
				{variantLabel}
			</Typography>
			<IconButton size="small" onClick={onNext} sx={{ color: 'inherit' }} aria-label="Variante suivante (→)">
				<ChevronRightIcon />
			</IconButton>
			<Divider orientation="vertical" flexItem sx={{ mx: 1, borderColor: 'rgba(0,0,0,.3)' }} />
			<Typography variant="caption" sx={{ fontWeight: 700, whiteSpace: 'nowrap', pr: 0.5 }}>
				Scénario
			</Typography>
			<ToggleButtonGroup
				size="small"
				exclusive
				value={scenario}
				onChange={(_, v: ScenarioKey | null) => v && onScenario(v)}
				sx={{
					'& .MuiToggleButton-root': {
						color: '#111',
						borderColor: 'rgba(0,0,0,.25)',
						py: 0.25,
						textTransform: 'none',
						whiteSpace: 'nowrap',
					},
					'& .MuiToggleButton-root.Mui-selected': { bgcolor: 'rgba(0,0,0,.85)', color: '#fdd835' },
				}}
			>
				{scenarios.map((s) => (
					<ToggleButton key={s.key} value={s.key}>
						{s.label}
					</ToggleButton>
				))}
			</ToggleButtonGroup>
		</Paper>
	);
}
