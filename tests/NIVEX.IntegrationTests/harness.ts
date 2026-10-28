import { readdirSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { handleRequest, type AppDeps } from "../../src/NIVEX.Api/src/app.ts";
import { CONSENT_VERSION } from "../../src/NIVEX.Api/src/routes.ts";
import type { Config } from "../../src/NIVEX.Api/src/config.ts";
import type { Db } from "../../src/NIVEX.Api/src/db.ts";
import { createSqliteDb } from "../../src/NIVEX.Api/src/sqlite.ts";
import { createTurnstileVerifier } from "../../src/NIVEX.Api/src/turnstile.ts";

const migrationDir = join(dirname(fileURLToPath(import.meta.url)), "../../src/NIVEX.Api/migrations");
const migration = readdirSync(migrationDir)
  .filter((name) => name.endsWith(".sql"))
  .sort()
  .map((name) => readFileSync(join(migrationDir, name), "utf8"))
  .join("\n");

export function testConfig(overrides: Partial<Config> = {}): Config {
  return {
    turnstileMode: "development",
    allowDevVerification: true,
    turnstileSecret: "development-only-secret",
    turnstileSiteKey: "",
    expectedHostname: "127.0.0.1",
    authSigningKey: "test-signing-key-must-be-32-characters",
    codePepper: "test-code-pepper-value",
    ipHashPepper: "test-ip-pepper-value",
    adminSetupToken: "test-setup-token",
    webOrigin: "http://127.0.0.1:8787",
    downloadUrl: "",
    appVersion: "0.1.0",
    accessTokenSeconds: 600,
    refreshTokenSeconds: 60 * 60,
    passwordIterations: 1000,
    ...overrides,
  };
}

export interface Harness {
  db: Db;
  now: Date;
  deps: AppDeps;
  request(path: string, init?: RequestInit & { ip?: string; noConsent?: boolean }): Promise<Response>;
}

export async function createHarness(overrides: Partial<Config> = {}): Promise<Harness> {
  const db = createSqliteDb();
  await db.batch(
    migration
      .split(";")
      .map((sql) => sql.trim())
      .filter(Boolean)
      .map((sql) => ({ sql, params: [] })),
  );
  const state = { now: new Date("2026-01-15T00:00:00.000Z") };
  const config = testConfig(overrides);
  const deps: AppDeps = {
    db,
    config,
    turnstile: createTurnstileVerifier(config),
    now: () => state.now,
  };
  return {
    db,
    get now() {
      return state.now;
    },
    set now(value: Date) {
      state.now = value;
    },
    deps,
    request(path, init = {}) {
      const headers = new Headers(init.headers);
      if (!headers.has("content-type") && init.body) headers.set("content-type", "application/json");
      const ip = init.ip ?? "203.0.113.10";
      headers.set("cf-connecting-ip", ip);
      let body = init.body;
      if (["/api/v1/devices", "/api/v1/ip/check", "/api/v1/ip/devices"].includes(path) && typeof body === "string") {
        const parsed = JSON.parse(body) as Record<string, unknown>;
        if (path === "/api/v1/devices" && parsed.ip === undefined) parsed.ip = ip;
        if (!init.noConsent && parsed.consent === undefined) parsed.consent = { accepted: true, version: CONSENT_VERSION };
        body = JSON.stringify(parsed);
      }
      return handleRequest(new Request(`http://127.0.0.1:8787${path}`, { ...init, body, headers }), deps);
    },
  };
}

export async function readError(response: Response): Promise<{ code: string; message: string }> {
  const body = (await response.json()) as { error: { code: string; message: string } };
  return body.error;
}
