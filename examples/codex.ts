import { getCodexUsage, getClaudeUsage } from '../src/usage.ts';

const usage = await getCodexUsage();
if (usage.ok) {
	console.log(JSON.stringify(usage, null, 2));
	// Call your custom function here, e.g.:
	// if (usage.remainingPercent < 20) await onLowAllowance(usage);
} else {
	console.error(JSON.stringify(usage, null, 2));
	process.exitCode = 1;
}

for (const usage of await Promise.all([getCodexUsage(), getClaudeUsage()])) {
	if (usage.ok) {
		console.log(JSON.stringify(usage, null, 2));
	} else {
		console.error(JSON.stringify(usage, null, 2));
		process.exitCode = 1;
	}
}
