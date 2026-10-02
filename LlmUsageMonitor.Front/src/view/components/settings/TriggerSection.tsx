import BoltOutlinedIcon from "@mui/icons-material/BoltOutlined";
import { Autocomplete, Box, Switch, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from "@mui/material";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { type FormEvent, useState } from "react";
import { getDashboardQueryKey, updateTriggerSettingsMutation } from "@/core/apis/generated/@tanstack/react-query.gen";
import type { TriggerSettings } from "@/core/apis/generated/types.gen";
import { modelSuggestions, providerLabel, providers } from "@/core/dashboard";
import { collect, type FieldErrors, serverFieldErrors, validateModel } from "@/core/settings.validation";
import { SaveBar, SettingsSection } from "./SettingsSection";

export function TriggerSection({ initial }: { initial: TriggerSettings }) {
	const queryClient = useQueryClient();
	const [values, setValues] = useState(initial);
	const [errors, setErrors] = useState<FieldErrors>({});
	const save = useMutation({
		...updateTriggerSettingsMutation(),
		onSuccess: () => void queryClient.invalidateQueries({ queryKey: getDashboardQueryKey() }),
		onError: (error) => setErrors(serverFieldErrors(error)),
	});

	const submit = (event: FormEvent) => {
		event.preventDefault();
		const local = collect({ "claude.model": validateModel(values.claude.model), "codex.model": validateModel(values.codex.model) });
		setErrors(local);
		if (Object.keys(local).length === 0) save.mutate({ body: values });
	};

	return (
		<SettingsSection
			title="Trigger"
			subtitle="Restarts a usage window after a reset."
			icon={<BoltOutlinedIcon fontSize="small" />}
			onSubmit={submit}
			actions={<SaveBar pending={save.isPending} saved={save.isSuccess} failed={save.isError} />}
		>
			<Box sx={{ overflowX: "auto" }}>
				<Table size="small" aria-label="Trigger per provider" sx={{ minWidth: 440 }}>
					<TableHead>
						<TableRow>
							<TableCell sx={{ pl: 0 }}>Provider</TableCell>
							<TableCell align="center" sx={{ width: 130, whiteSpace: "normal" }}>
								Automatic after reset
							</TableCell>
							<TableCell sx={{ pr: 0 }}>Model</TableCell>
						</TableRow>
					</TableHead>
					<TableBody>
						{providers.map((provider) => (
							<TableRow key={provider}>
								<TableCell component="th" scope="row" sx={{ pl: 0, fontWeight: 600 }}>
									{providerLabel[provider]}
								</TableCell>
								<TableCell align="center">
									<Switch
										checked={values[provider].autoEnabled}
										onChange={(event) => setValues({ ...values, [provider]: { ...values[provider], autoEnabled: event.target.checked } })}
										slotProps={{ input: { "aria-label": `${providerLabel[provider]} automatic after reset` } }}
									/>
								</TableCell>
								<TableCell sx={{ pr: 0 }}>
									<Autocomplete
										freeSolo
										autoSelect
										size="small"
										fullWidth
										options={modelSuggestions[provider]}
										inputValue={values[provider].model}
										onInputChange={(_, model) => setValues({ ...values, [provider]: { ...values[provider], model } })}
										renderInput={(params) => (
											<TextField
												{...params}
												error={Boolean(errors[`${provider}.model`])}
												helperText={errors[`${provider}.model`]}
												slotProps={{
													...params.slotProps,
													htmlInput: { ...params.slotProps.htmlInput, "aria-label": `${providerLabel[provider]} model` },
												}}
											/>
										)}
									/>
								</TableCell>
							</TableRow>
						))}
					</TableBody>
				</Table>
			</Box>
			<Typography variant="caption" sx={{ color: "text.secondary" }}>
				Model used for the "1+1=?" prompt.
			</Typography>
		</SettingsSection>
	);
}
