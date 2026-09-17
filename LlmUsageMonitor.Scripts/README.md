# LLM remaining usage

Origin of the project: these readers are ported to C# in `LlmUsageMonitor.Api/LlmUsageMonitor.Adapters.*`, and kept here as a reference and for one-off checks from a terminal. This folder is a standalone pnpm project, unrelated to the front-end workspace.

TypeScript functions to read subscription allowance using existing CLI authentication, without sending prompts or starting model turns. Requires Node.js 22+ and pnpm.

```powershell
pnpm install
pnpm usage:codex
```

## Use from your code

```ts
import { getCodexUsage, getClaudeUsage, getUsage } from './src/usage.ts';

const codex = await getCodexUsage(); // Only Codex is contacted.
if (codex.ok) {
	if (codex.remainingPercent < 20) {
		// Call your custom function here.
	}
} else {
	console.error(codex.error.code, codex.error.message);
}

// When you want both providers:
// const { codex, claude } = await getUsage();
// Or Claude alone: await getClaudeUsage();
```

Every successful result contains `provider`, `fetchedAt` (UTC ISO), `remainingPercent`, and `windows`. Every window has `id`, `usedPercent`, `remainingPercent`, `resetsAt` (UTC ISO or null), and `windowDurationMinutes` (number or null).

100 means no usage / all allowance remaining. 0 means exhausted. The summary is the minimum remaining percentage across all returned windows, including model-specific buckets; inspect window IDs if your custom logic targets a particular model. Extra monetary credits are not included. Reset timestamps are reported as supplied: the reader does not assume an old window has reset or invent a new percentage.

Failures return `{ ok: false, provider, fetchedAt, error: { code, message } }`, with no percentage. Failure of one provider does not hide the other provider's result. Missing windows return `NO_USAGE_DATA`, not 100%.

## Authentication and options

```ts
const usage = await getCodexUsage({
	timeoutMs: 20_000,
	// Optional native executable path; do not supply a shell command:
	// codexExecutable: 'C:/path/to/codex.exe',
});

// Claude, when ready:
// const claude = await getClaudeUsage({
//   claudeCredentialsPath: 'C:/path/to/.claude/.credentials.json',
// });
```

Codex must be on PATH (or use `codexExecutable`) and signed in with your subscription. Its app server handles the existing login, including `CODEX_HOME`. The only protocol methods sent are `initialize`, `initialized`, and `account/rateLimits/read`; the child process is terminated after the result or timeout. No conversation is created.

Claude reads `CLAUDE_CONFIG_DIR/.credentials.json`, falling back to `~/.claude/.credentials.json`. An explicit path takes priority. It sends a single authenticated GET to `https://api.anthropic.com/api/oauth/usage`. This endpoint is undocumented and may change or reject requests. The reader does not refresh or rewrite Claude credentials. Expired credentials must be refreshed through the CLI. API keys and desktop-only credentials are not supported; macOS Keychain credentials require a separate adapter. No credentials or raw server errors are logged or returned.

There is no automatic polling, retry, or cache. Each function call reads live usage. Avoid frequent polling, especially after Claude returns `RATE_LIMITED`.

## Verification

`pnpm check` verifies formatting with Oxfmt, lint rules with Oxlint, and types with TypeScript. Use `pnpm format` to write formatting changes or `pnpm lint:fix` to apply safe lint fixes. `pnpm usage:codex` performs a live Codex-only usage check. Claude live verification is intentionally deferred.

## Sources

- [Codex app-server protocol and account rate limits](https://learn.chatgpt.com/docs/app-server)
- [Claude usage endpoint reproduction in the Claude Code issue tracker](https://github.com/anthropics/claude-code/issues/31021) (community report, not a supported public API contract)
- [Claude authentication and usage scope errors](https://code.claude.com/docs/en/errors)
