import SpeedOutlinedIcon from "@mui/icons-material/SpeedOutlined";
import { InputAdornment, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from "@mui/material";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { type FormEvent, useState } from "react";
import { getDashboardQueryKey, getPollingSettingsQueryKey, updatePollingSettingsMutation } from "@/core/apis/generated/@tanstack/react-query.gen";
import type { PollingSettings } from "@/core/apis/generated/types.gen";
import { providerLabel, providers } from "@/core/dashboard";
import { collect, type FieldErrors, serverFieldErrors, validateInterval } from "@/core/settings.validation";
import { SaveBar, SettingsSection, useDraft } from "./SettingsSection";

export function PollingSection({ settings }: { settings: PollingSettings }) {
	const queryClient = useQueryClient();
	const { values, edit, discard } = useDraft(settings);
	const [errors, setErrors] = useState<FieldErrors>({});
	const save = useMutation({
		...updatePollingSettingsMutation(),
		onSuccess: (saved) => {
			queryClient.setQueryData(getPollingSettingsQueryKey(), saved);
			discard();
			void queryClient.invalidateQueries({ queryKey: getDashboardQueryKey() });
		},
		onError: (error) => setErrors(serverFieldErrors(error)),
	});

	const setValues = (next: PollingSettings) => {
		edit(next);
		if (save.isSuccess) save.reset();
	};

	const submit = (event: FormEvent) => {
		event.preventDefault();
		const local = collect({ claudeIntervalMinutes: validateInterval(values.claudeIntervalMinutes), codexIntervalMinutes: validateInterval(values.codexIntervalMinutes) });
		setErrors(local);
		if (Object.keys(local).length === 0) save.mutate({ body: values });
	};

	return (
		<SettingsSection
			title="Usage reading"
			subtitle="How often each provider is polled."
			icon={<SpeedOutlinedIcon fontSize="small" />}
			onSubmit={submit}
			actions={<SaveBar pending={save.isPending} saved={save.isSuccess} error={save.error} />}
		>
			<Table size="small" aria-label="Reading intervals">
				<TableHead>
					<TableRow>
						<TableCell sx={{ pl: 0 }}>Provider</TableCell>
						<TableCell sx={{ pr: 0, width: { xs: 140, sm: 200 } }}>Value</TableCell>
					</TableRow>
				</TableHead>
				<TableBody>
					{providers.map((provider) => {
						const field = provider === "claude" ? "claudeIntervalMinutes" : "codexIntervalMinutes";
						return (
							<TableRow key={provider}>
								<TableCell component="th" scope="row" sx={{ pl: 0, fontWeight: 600 }}>
									{providerLabel[provider]}
								</TableCell>
								<TableCell sx={{ pr: 0 }}>
									<TextField
										fullWidth
										type="number"
										value={values[field]}
										onChange={(event) => setValues({ ...values, [field]: Number(event.target.value) })}
										error={Boolean(errors[field])}
										helperText={errors[field]}
										slotProps={{
											htmlInput: { min: 1, max: 60, "aria-label": `${providerLabel[provider]} interval (minutes)` },
											input: { endAdornment: <InputAdornment position="end">min</InputAdornment> },
										}}
									/>
								</TableCell>
							</TableRow>
						);
					})}
				</TableBody>
			</Table>
			<Typography variant="caption" sx={{ color: "text.secondary" }}>
				A divisor of 60 (1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30 or 60 minutes). A short interval raises the risk of 429.
			</Typography>
		</SettingsSection>
	);
}
