import { Alert, AlertTitle, Button } from "@mui/material";
import { apiErrorMessage } from "@/core/apis/api-error";

type FailedQuery = { data: unknown; error: unknown; isError: boolean; isFetching: boolean; refetch: () => unknown };

/**
 * Failure of a query, with a retry. Without data it replaces the content; after a failed refresh (TanStack keeps the
 * data and sets `isError`) it is a discreet banner above the values still shown. Nothing when the query is fine.
 */
export const QueryError = ({ query, subject }: { query: FailedQuery; subject: string }) => {
	if (!query.isError) return null;
	const retry = (
		<Button color="inherit" size="small" loading={query.isFetching} onClick={() => void query.refetch()}>
			Retry
		</Button>
	);
	if (query.data === undefined) {
		return (
			<Alert severity="error" action={retry}>
				<AlertTitle>Could not load {subject}.</AlertTitle>
				{apiErrorMessage(query.error)}
			</Alert>
		);
	}
	return (
		<Alert severity="warning" variant="outlined" action={retry} sx={{ py: 0 }}>
			Could not refresh {subject}: {apiErrorMessage(query.error)} The values shown may be stale.
		</Alert>
	);
};
