import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import {
	capture,
	record,
	timeout,
	UsageError,
	type UsageOptions,
	type UsageResult,
	type UsageWindow,
	windowOf,
} from './shared.ts';

/** Starts only the account protocol; never starts a thread or model turn. */
function readCodexLimits(options: UsageOptions): Promise<unknown> {
	const timeoutMs = timeout(options);
	return new Promise((resolve, reject) => {
		const child = spawn(
			options.codexExecutable ?? 'codex',
			['app-server', '--listen', 'stdio://'],
			{
				windowsHide: true,
				stdio: ['pipe', 'pipe', 'pipe'],
			},
		);
		const lines = createInterface({ input: child.stdout });
		let finished = false;
		let initialized = false;
		const timer = setTimeout(
			() => finish(new UsageError('TIMEOUT', 'Codex usage request timed out.')),
			timeoutMs,
		);

		function finish(error?: UsageError, result?: unknown) {
			if (finished) return;
			finished = true;
			clearTimeout(timer);
			lines.close();
			child.stdin.end();
			child.kill();
			if (error) reject(error);
			else resolve(result);
		}

		function send(message: unknown) {
			if (!finished) child.stdin.write(JSON.stringify(message) + '\n');
		}

		// Drain diagnostics without printing potentially sensitive account information.
		child.stderr.resume();
		child.on('error', () =>
			finish(
				new UsageError(
					'CLI_UNAVAILABLE',
					'Cannot start Codex. Install its CLI or set codexExecutable to its native executable path.',
				),
			),
		);
		child.stdin.on('error', () =>
			finish(new UsageError('CLI_IO_ERROR', 'Codex closed its input before returning usage.')),
		);
		child.on('exit', () =>
			finish(new UsageError('CLI_EXITED', 'Codex exited before returning usage.')),
		);
		lines.on('line', (line) => {
			if (finished) return;
			try {
				const message = record(JSON.parse(line));
				if (message.id !== 1 && message.id !== 2) return;
				if (message.error) {
					finish(
						new UsageError(
							'CODEX_REQUEST_FAILED',
							'Codex rejected the account request. Check codex login status and that your CLI supports account/rateLimits/read.',
						),
					);
					return;
				}
				if (message.id === 1 && !initialized) {
					initialized = true;
					send({ method: 'initialized', params: {} });
					send({ id: 2, method: 'account/rateLimits/read', params: {} });
				} else if (message.id === 2 && initialized) {
					finish(undefined, message.result);
				}
			} catch {
				finish(new UsageError('INVALID_RESPONSE', 'Codex returned invalid protocol data.'));
			}
		});
		send({
			id: 1,
			method: 'initialize',
			params: { clientInfo: { name: 'llm_refresh', title: 'LLM usage reader', version: '0.1.0' } },
		});
	});
}

export function getCodexUsage(options: UsageOptions = {}): Promise<UsageResult> {
	return capture('codex', async () => {
		const result = record(await readCodexLimits(options));
		const windows: UsageWindow[] = [];
		const buckets = result.rateLimitsByLimitId == null ? {} : record(result.rateLimitsByLimitId);
		// Older CLI versions expose only the legacy rateLimits snapshot.
		if (!Object.keys(buckets).length && result.rateLimits != null) {
			const legacy = record(result.rateLimits);
			buckets[typeof legacy.limitId === 'string' ? legacy.limitId : 'codex'] = legacy;
		}
		for (const [bucketId, value] of Object.entries(buckets)) {
			const bucket = record(value);
			for (const name of ['primary', 'secondary']) {
				if (bucket[name] == null) continue;
				const window = record(bucket[name]);
				windows.push(
					windowOf(
						`${bucketId}/${name}`,
						window.usedPercent,
						window.resetsAt,
						window.windowDurationMins,
					),
				);
			}
		}
		return windows;
	});
}
