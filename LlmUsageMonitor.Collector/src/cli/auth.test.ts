import crypto from "node:crypto";
import fs from "node:fs";
import http from "node:http";
import type { AddressInfo } from "node:net";
import os from "node:os";
import path from "node:path";
import { afterEach, beforeEach, describe, expect, it } from "vite-plus/test";
import { DeviceAuth } from "./auth";

type Form = Record<string, string>;

let dataDir: string;
let server: http.Server;
let issuer: string;
let requests: { path: string; form: Form }[];
/** Answers of the token endpoint, in order; the last one repeats. */
let tokenAnswers: { status: number; body: object }[];

beforeEach(async () => {
	dataDir = await fs.promises.mkdtemp(path.join(os.tmpdir(), "llm-usage-auth-"));
	requests = [];
	tokenAnswers = [];
	server = http.createServer((request, response) => {
		let body = "";
		request.on("data", (chunk: Buffer) => (body += chunk.toString()));
		request.on("end", () => {
			const url = request.url ?? "";
			const send = (status: number, value: object) => response.writeHead(status, { "Content-Type": "application/json" }).end(JSON.stringify(value));
			if (url.endsWith("/.well-known/openid-configuration"))
				return send(200, { token_endpoint: `${issuer}/token`, device_authorization_endpoint: `${issuer}/device`, revocation_endpoint: `${issuer}/revoke` });

			requests.push({ path: url, form: Object.fromEntries(new URLSearchParams(body)) });
			if (url.endsWith("/device")) return send(200, { device_code: "dev-1", user_code: "ABCD-EFGH", verification_uri: `${issuer}/device`, expires_in: 600, interval: 1 });
			if (url.endsWith("/token")) {
				const answer = tokenAnswers.length > 1 ? tokenAnswers.shift()! : tokenAnswers[0];
				return send(answer.status, answer.body);
			}
			return send(200, {});
		});
	});
	await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
	issuer = `http://127.0.0.1:${(server.address() as AddressInfo).port}/realms/internal`;
});

afterEach(async () => {
	await new Promise((resolve) => server.close(resolve));
	await fs.promises.rm(dataDir, { recursive: true, force: true });
});

const clock = { now: Date.parse("2026-10-01T12:00:00Z") };
const auth = () => new DeviceAuth({ issuer, clientId: "i-llm-usage-collector", dataDir, now: () => clock.now, sleep: async () => undefined });

describe("device flow", () => {
	it("polls until the code is entered, slowing down when asked, and keeps the offline token", async () => {
		tokenAnswers = [
			{ status: 400, body: { error: "authorization_pending" } },
			{ status: 400, body: { error: "slow_down" } },
			{ status: 200, body: { access_token: "access-1", expires_in: 300, refresh_token: "refresh-1" } },
		];
		const prompts: string[] = [];

		await auth().login((prompt) => prompts.push(prompt.userCode));

		expect(prompts).toEqual(["ABCD-EFGH"]);
		const { code_challenge, ...device } = requests[0].form;
		expect(device).toEqual({ client_id: "i-llm-usage-collector", scope: "openid offline_access", code_challenge_method: "S256" });
		// PKCE: every poll proves the verifier of the challenge sent with the device request.
		const verifiers = requests.filter((request) => request.path.endsWith("/token")).map((request) => request.form.code_verifier);
		expect(new Set(verifiers).size).toBe(1);
		expect(crypto.createHash("sha256").update(verifiers[0]).digest("base64url")).toBe(code_challenge);
		expect(requests.filter((request) => request.path.endsWith("/token"))).toHaveLength(3);
		expect(await auth().getAccessToken()).toBe("access-1");
	});

	it("stops on a refused sign-in", async () => {
		tokenAnswers = [{ status: 400, body: { error: "access_denied", error_description: "denied by user" } }];

		await expect(auth().login(() => undefined)).rejects.toThrow("Sign-in failed: denied by user");
		expect(await auth().isSignedIn()).toBe(false);
	});
});

describe("session", () => {
	const signIn = async () => {
		tokenAnswers = [{ status: 200, body: { access_token: "access-1", expires_in: 300, refresh_token: "refresh-1" } }];
		await auth().login(() => undefined);
		requests = [];
	};

	it("refreshes an expired access token and keeps the rotated refresh token", async () => {
		await signIn();
		clock.now += 10 * 60_000;
		tokenAnswers = [{ status: 200, body: { access_token: "access-2", expires_in: 300, refresh_token: "refresh-2" } }];

		expect(await auth().getAccessToken()).toBe("access-2");
		expect(requests[0].form).toMatchObject({ grant_type: "refresh_token", refresh_token: "refresh-1" });

		clock.now += 10 * 60_000;
		await auth().getAccessToken();
		expect(requests[1].form.refresh_token).toBe("refresh-2");
	});

	it("asks to sign in again once the offline token is revoked", async () => {
		await signIn();
		clock.now += 10 * 60_000;
		tokenAnswers = [{ status: 400, body: { error: "invalid_grant" } }];

		await expect(auth().getAccessToken()).rejects.toThrow("run `llm-usage login`");
	});

	it("ignores a session of another issuer", async () => {
		await signIn();

		const other = new DeviceAuth({ issuer: `${issuer}-other`, clientId: "i-llm-usage-collector", dataDir });
		await expect(other.getAccessToken()).rejects.toThrow("Not signed in");
	});

	it("revokes and forgets the session on logout", async () => {
		await signIn();

		expect(await auth().logout()).toBe(true);

		expect(requests[0]).toEqual({ path: "/realms/internal/revoke", form: { token: "refresh-1", token_type_hint: "refresh_token", client_id: "i-llm-usage-collector" } });
		expect(fs.existsSync(path.join(dataDir, "token.json"))).toBe(false);
	});
});
