import RestartAltIcon from "@mui/icons-material/RestartAlt";
import { Box, Switch, Table, TableBody, TableCell, TableHead, TableRow, TextField } from "@mui/material";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { type FormEvent, useState } from "react";
import { getDashboardQueryKey, getResetCreditSettingsQueryKey, updateResetCreditSettingsMutation } from "@/core/apis/generated/@tanstack/react-query.gen";
import type { ResetCreditSettings } from "@/core/apis/generated/types.gen";
import { providerLabel, providers } from "@/core/dashboard";
import { type FieldErrors, serverFieldErrors } from "@/core/settings.validation";
import { SaveBar, SettingsSection, useDraft } from "./SettingsSection";

export function ResetCreditSection({ settings }: { settings: ResetCreditSettings }) {
	const queryClient = useQueryClient();
	const { values, edit, settle } = useDraft(settings);
	const [errors, setErrors] = useState<FieldErrors>({});
	const save = useMutation({
		...updateResetCreditSettingsMutation(),
		onSuccess: (saved, { body }) => {
			queryClient.setQueryData(getResetCreditSettingsQueryKey(), saved);
			settle(body);
			void queryClient.invalidateQueries({ queryKey: getDashboardQueryKey() });
		},
		onError: (error) => setErrors(serverFieldErrors(error)),
	});
	const change = (next: ResetCreditSettings) => {
		edit(next);
		if (save.isSuccess) save.reset();
	};
	const submit = (event: FormEvent) => {
		event.preventDefault();
		const local: FieldErrors = {};
		for (const provider of providers) {
			const minutes = values[provider].beforeExpiryMinutes;
			if (!Number.isInteger(minutes) || minutes < 1 || minutes > 10080) local[`${provider}.beforeExpiryMinutes`] = "Between 1 and 10080 minutes.";
		}
		setErrors(local);
		if (Object.keys(local).length === 0) save.mutate({ body: values });
	};
	return (
		<SettingsSection
			title="Earned resets"
			subtitle="Use each expiring reset, earliest expiry first, even with unused quota. No wake-up prompt is sent."
			icon={<RestartAltIcon fontSize="small" />}
			onSubmit={submit}
			actions={<SaveBar pending={save.isPending} saved={save.isSuccess} error={save.error} />}
		>
			<Box sx={{ overflowX: "auto" }}>
				<Table size="small" aria-label="Earned resets per provider">
					<TableHead>
						<TableRow>
							<TableCell>Provider</TableCell>
							<TableCell>Automatic use</TableCell>
							<TableCell>Before expiry</TableCell>
						</TableRow>
					</TableHead>
					<TableBody>
						{providers.map((provider) => (
							<TableRow key={provider}>
								<TableCell component="th" scope="row">
									{providerLabel[provider]}
								</TableCell>
								<TableCell>
									<Switch
										checked={values[provider].autoEnabled}
										onChange={(event) => change({ ...values, [provider]: { ...values[provider], autoEnabled: event.target.checked } })}
										slotProps={{ input: { "aria-label": `${providerLabel[provider]} automatic earned resets` } }}
									/>
								</TableCell>
								<TableCell>
									<TextField
										sx={{ width: 180 }}
										type="number"
										label="Minutes before expiry"
										value={values[provider].beforeExpiryMinutes}
										onChange={(event) => change({ ...values, [provider]: { ...values[provider], beforeExpiryMinutes: Number(event.target.value) } })}
										error={Boolean(errors[`${provider}.beforeExpiryMinutes`])}
										helperText={errors[`${provider}.beforeExpiryMinutes`]}
										slotProps={{ htmlInput: { min: 1, max: 10080, step: 1, "aria-label": `${providerLabel[provider]} before expiry (minutes)` } }}
									/>
								</TableCell>
							</TableRow>
						))}
					</TableBody>
				</Table>
			</Box>
		</SettingsSection>
	);
}
