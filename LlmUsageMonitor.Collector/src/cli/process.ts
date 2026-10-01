import { spawn } from "node:child_process";
import { isSea } from "node:sea";

export type RunResult = { code: number; stdout: string; stderr: string };

/** Runs a command without a shell; never throws on a non-zero exit code. */
export function run(command: string, args: string[], input?: string): Promise<RunResult> {
	return new Promise((resolve) => {
		const child = spawn(command, args, { windowsHide: true, stdio: ["pipe", "pipe", "pipe"] });
		let stdout = "";
		let stderr = "";
		child.stdout.on("data", (chunk: Buffer) => (stdout += chunk.toString()));
		child.stderr.on("data", (chunk: Buffer) => (stderr += chunk.toString()));
		child.on("error", (error) => resolve({ code: -1, stdout, stderr: error.message }));
		child.on("close", (code) => resolve({ code: code ?? -1, stdout, stderr }));
		child.stdin.end(input);
	});
}

/** Fails with the output of the command. */
export async function runChecked(command: string, args: string[], input?: string): Promise<RunResult> {
	const result = await run(command, args, input);
	if (result.code !== 0) throw new Error(`${command} ${args.join(" ")} failed (${result.code}): ${(result.stderr || result.stdout).trim()}`);
	return result;
}

/** Running as the standalone executable (Node SEA), not through `node` / `npx`. */
export function isStandalone(): boolean {
	return isSea();
}
