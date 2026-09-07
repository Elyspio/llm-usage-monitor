import { getClaudeUsage } from './claude.ts';
import { getCodexUsage } from './codex.ts';
import type { Provider, UsageOptions, UsageResult } from './shared.ts';

export { getClaudeUsage } from './claude.ts';
export { getCodexUsage } from './codex.ts';
export * from './shared.ts';

/** Fetch both independently. Call getCodexUsage/getClaudeUsage to query only one. */
export async function getUsage(options: UsageOptions = {}): Promise<Record<Provider, UsageResult>> {
	const [codex, claude] = await Promise.all([getCodexUsage(options), getClaudeUsage(options)]);
	return { codex, claude };
}

export type { Provider, UsageOptions, UsageResult, UsageWindow } from './shared.ts';
