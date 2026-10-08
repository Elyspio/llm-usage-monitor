import SpeedOutlinedIcon from "@mui/icons-material/SpeedOutlined";
import { Autocomplete, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from "@mui/material";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { type FormEvent, useState } from "react";
import { getDashboardQueryKey, getPollingSettingsQueryKey, updatePollingSettingsMutation } from "@/core/apis/generated/@tanstack/react-query.gen";
import type { PollingSettings } from "@/core/apis/generated/types.gen";
import { providerLabel, providers } from "@/core/dashboard";
import { collect, type FieldErrors, serverFieldErrors, validateInterval } from "@/core/settings.validation";
import { SaveBar, SettingsSection, useDraft } from "./SettingsSection";

const intervals = [1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30, 60];
const intervalLabel = (minutes: number) => `${minutes} min`;

export function PollingSection({ settings }: { settings: PollingSettings }) {
	const queryClient = useQueryClient();
	const { values, edit, settle } = useDraft(settings);
	const [errors, setErrors] = useState<FieldErrors>({});
	const save = useMutation({
		...updatePollingSettingsMutation(),
		onSuccess: (saved, { body }) => {
			queryClient.setQueryData(getPollingSettingsQueryKey(), saved);
			settle(body);
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
									<Autocomplete
										sx={{ width: 120 }}
										size="small"
										disableClearable
										openOnFocus
										autoHighlight
										options={intervals}
										slotProps={{ listbox: { sx: { maxHeight: "min(480px, 50vh)" } } }}
										getOptionLabel={intervalLabel}
										value={values[field]}
										onChange={(_, minutes) => setValues({ ...values, [field]: minutes })}
										renderInput={(params) => (
											<TextField
												{...params}
												error={Boolean(errors[field])}
												helperText={errors[field]}
												slotProps={{
													...params.slotProps,
													htmlInput: { ...params.slotProps.htmlInput, "aria-label": `${providerLabel[provider]} interval (minutes)` },
												}}
											/>
										)}
									/>
								</TableCell>
							</TableRow>
						);
					})}
				</TableBody>
			</Table>
			<Typography variant="caption" sx={{ color: "text.secondary" }}>
				Short intervals increase the risk of 429 responses.
			</Typography>
		</SettingsSection>
	);
}
