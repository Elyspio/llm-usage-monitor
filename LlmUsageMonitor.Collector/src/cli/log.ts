import fs from "node:fs";
import path from "node:path";
import type { CollectorLogger } from "../types";

type Level = "debug" | "info" | "warn" | "error";

export type FileLoggerOptions = {
	/** Copies the warnings and errors to stderr: for the commands run by hand. */
	echo?: boolean;
	maxBytes?: number;
	/** Rotated files kept besides the current one. */
	keep?: number;
};

/**
 * JSON lines in `logs/collector.log`, the only trace of the scheduled runs. Written synchronously: a one-shot process
 * may exit right after. Rotated past 1 MB, 3 older files kept.
 */
export class FileLogger implements CollectorLogger {
	private readonly echo: boolean;
	private readonly maxBytes: number;
	private readonly keep: number;

	constructor(
		readonly file: string,
		{ echo = false, maxBytes = 1024 * 1024, keep = 3 }: FileLoggerOptions = {}
	) {
		this.echo = echo;
		this.maxBytes = maxBytes;
		this.keep = keep;
	}

	debug(message: string, meta?: unknown): void {
		this.write("debug", message, meta);
	}

	info(message: string, meta?: unknown): void {
		this.write("info", message, meta);
	}

	warn(message: string, meta?: unknown): void {
		this.write("warn", message, meta);
	}

	error(message: string, meta?: unknown): void {
		this.write("error", message, meta);
	}

	private write(level: Level, message: string, meta?: unknown) {
		if (this.echo && (level === "warn" || level === "error")) console.error(`${level}: ${message}${meta ? ` ${JSON.stringify(meta)}` : ""}`);
		try {
			fs.mkdirSync(path.dirname(this.file), { recursive: true });
			this.rotate();
			const line = JSON.stringify({
				time: new Date().toISOString(),
				level,
				pid: process.pid,
				message,
				...(meta && typeof meta === "object" ? meta : meta === undefined ? {} : { meta }),
			});
			fs.appendFileSync(this.file, `${line}\n`);
		} catch {
			// Never fail a sync because of its log.
		}
	}

	private rotate() {
		let size: number;
		try {
			size = fs.statSync(this.file).size;
		} catch {
			return;
		}
		if (size < this.maxBytes) return;
		fs.rmSync(`${this.file}.${this.keep}`, { force: true });
		for (let index = this.keep - 1; index >= 1; index--) {
			if (fs.existsSync(`${this.file}.${index}`)) fs.renameSync(`${this.file}.${index}`, `${this.file}.${index + 1}`);
		}
		fs.renameSync(this.file, `${this.file}.1`);
	}
}
