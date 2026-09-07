import { getCodexUsage, getClaudeUsage } from '../src/usage.ts';

const usage = await getCodexUsage();
if (usage.ok) {
	console.log(JSON.stringify(usage, null, 2));
} else {
	console.error(JSON.stringify(usage, null, 2));
	process.exitCode = 1;
}

