import fs from "node:fs";
import path from "node:path";
import { setTimeout } from "node:timers/promises";
import { readJson, writeJsonAtomic } from "../files";

/** A token about to expire is refreshed before the upload rather than refused by the monitor. */
const EXPIRY_MARGIN_MS = 60_000;

type Discovery = { token_endpoint: string; device_authorization_endpoint?: string; revocation_endpoint?: string };

type TokenResponse = { access_token: string; expires_in?: number; refresh_token?: string };

type TokenFile = {
	issuer: string;
	clientId: string;
	/** offline_access: valid until revoked in Keycloak, or unused for the realm's offline idle timeout. */
	refreshToken: string;
	accessToken?: string;
	/** Epoch milliseconds. */
	accessTokenExpiresAt?: number;
};

export type DevicePrompt = { verificationUri: string; verificationUriComplete?: string; userCode: string; expiresIn: number };

export type DeviceAuthOptions = {
	issuer: string;
	clientId: string;
	dataDir: string;
	now?: () => number;
	sleep?: (ms: number) => Promise<unknown>;
};

export class AuthError extends Error {}

/**
 * OIDC session of the CLI: device authorization grant (RFC 8628) to sign in once, then an offline refresh token kept in
 * the data folder (0600), so the scheduled runs never need a browser.
 */
export class DeviceAuth {
	private discovery?: Discovery;
	private readonly now: () => number;
	private readonly sleep: (ms: number) => Promise<unknown>;

	constructor(private readonly options: DeviceAuthOptions) {
		this.now = options.now ?? Date.now;
		this.sleep = options.sleep ?? ((ms) => setTimeout(ms));
	}

	private get tokenFile() {
		return path.join(this.options.dataDir, "token.json");
	}

	/** Shows the code to the user through `onPrompt`, then waits until it is entered, refused or expired. */
	async login(onPrompt: (prompt: DevicePrompt) => void): Promise<void> {
		const discovery = await this.discover();
		if (!discovery.device_authorization_endpoint) throw new AuthError(`${this.options.issuer} does not support the device authorization grant`);

		const device = await this.post<{
			device_code: string;
			user_code: string;
			verification_uri: string;
			verification_uri_complete?: string;
			expires_in: number;
			interval?: number;
		}>(discovery.device_authorization_endpoint, { client_id: this.options.clientId, scope: "openid offline_access" });

		onPrompt({ verificationUri: device.verification_uri, verificationUriComplete: device.verification_uri_complete, userCode: device.user_code, expiresIn: device.expires_in });

		let interval = (device.interval ?? 5) * 1000;
		const deadline = this.now() + device.expires_in * 1000;
		while (this.now() < deadline) {
			await this.sleep(interval);
			const response = await this.request(discovery.token_endpoint, {
				grant_type: "urn:ietf:params:oauth:grant-type:device_code",
				device_code: device.device_code,
				client_id: this.options.clientId,
			});
			if (response.ok) {
				await this.save((await response.json()) as TokenResponse);
				return;
			}
			const { error, error_description } = (await response.json().catch(() => ({}))) as { error?: string; error_description?: string };
			if (error === "authorization_pending") continue;
			if (error === "slow_down") {
				interval += 5000;
				continue;
			}
			throw new AuthError(`Sign-in failed: ${error_description ?? error ?? response.status}`);
		}
		throw new AuthError("Sign-in failed: the code expired, run `llm-usage login` again");
	}

	/** A valid access token, refreshed with the offline token when needed. */
	async getAccessToken(): Promise<string> {
		const stored = await this.load();
		if (!stored) throw new AuthError("Not signed in: run `llm-usage login`");
		if (stored.accessToken && (stored.accessTokenExpiresAt ?? 0) - EXPIRY_MARGIN_MS > this.now()) return stored.accessToken;

		const discovery = await this.discover();
		const response = await this.request(discovery.token_endpoint, { grant_type: "refresh_token", refresh_token: stored.refreshToken, client_id: this.options.clientId });
		if (!response.ok) {
			const { error, error_description } = (await response.json().catch(() => ({}))) as { error?: string; error_description?: string };
			if (error === "invalid_grant") throw new AuthError("Session expired or revoked: run `llm-usage login`");
			throw new AuthError(`Token refresh failed (${response.status}): ${error_description ?? error ?? ""}`);
		}
		return await this.save((await response.json()) as TokenResponse, stored.refreshToken);
	}

	/** Revokes the offline token (best effort) and forgets it. Returns whether a session existed. */
	async logout(): Promise<boolean> {
		const stored = await this.load();
		if (!stored) return false;
		try {
			const { revocation_endpoint } = await this.discover();
			if (revocation_endpoint) await this.request(revocation_endpoint, { token: stored.refreshToken, token_type_hint: "refresh_token", client_id: this.options.clientId });
		} catch {
			// Offline or unreachable issuer: the local session is removed anyway.
		}
		await fs.promises.rm(this.tokenFile, { force: true });
		return true;
	}

	async isSignedIn(): Promise<boolean> {
		return (await this.load()) !== undefined;
	}

	/** The stored session, if it belongs to the configured issuer and client. */
	private async load(): Promise<TokenFile | undefined> {
		const stored = await readJson<TokenFile>(this.tokenFile);
		if (!stored?.refreshToken || stored.issuer !== this.options.issuer || stored.clientId !== this.options.clientId) return undefined;
		return stored;
	}

	private async save(tokens: TokenResponse, previousRefreshToken?: string): Promise<string> {
		const refreshToken = tokens.refresh_token ?? previousRefreshToken;
		if (!refreshToken) throw new AuthError("The issuer returned no refresh token: the client needs the offline_access scope");
		const stored: TokenFile = {
			issuer: this.options.issuer,
			clientId: this.options.clientId,
			refreshToken,
			accessToken: tokens.access_token,
			accessTokenExpiresAt: this.now() + (tokens.expires_in ?? 60) * 1000,
		};
		await writeJsonAtomic(this.tokenFile, stored, 0o600);
		return tokens.access_token;
	}

	private async discover(): Promise<Discovery> {
		if (this.discovery) return this.discovery;
		const url = `${this.options.issuer.replace(/\/$/, "")}/.well-known/openid-configuration`;
		const response = await fetch(url);
		if (!response.ok) throw new AuthError(`OIDC discovery failed (${response.status}) on ${url}`);
		this.discovery = (await response.json()) as Discovery;
		return this.discovery;
	}

	private request(url: string, form: Record<string, string>) {
		return fetch(url, { method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" }, body: new URLSearchParams(form) });
	}

	private async post<T>(url: string, form: Record<string, string>): Promise<T> {
		const response = await this.request(url, form);
		if (!response.ok) {
			const { error, error_description } = (await response.json().catch(() => ({}))) as { error?: string; error_description?: string };
			throw new AuthError(`Request refused by ${new URL(url).host} (${response.status}): ${error_description ?? error ?? ""}`);
		}
		return (await response.json()) as T;
	}
}
