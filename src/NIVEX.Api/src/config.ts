export interface Config {
  turnstileMode: "production" | "development";
  allowDevVerification: boolean;
  turnstileSecret: string;
  turnstileSiteKey: string;
  expectedHostname: string;
  authSigningKey: string;
  codePepper: string;
  ipHashPepper: string;
  adminSetupToken: string;
  webOrigin: string;
  downloadUrl: string;
  appVersion: string;
  accessTokenSeconds: number;
  refreshTokenSeconds: number;
  passwordIterations: number;
  geminiApiKey: string;
}

export interface WorkerEnv {
  DB: D1Like;
  ASSETS?: { fetch(input: Request): Promise<Response> };
  APP_VERSION?: string;
  TURNSTILE_MODE?: string;
  ALLOW_DEV_VERIFICATION?: string;
  TURNSTILE_SITE_KEY?: string;
  TURNSTILE_SECRET?: string;
  EXPECTED_HOSTNAME?: string;
  WEB_ORIGIN?: string;
  DOWNLOAD_URL?: string;
  ACCESS_TOKEN_SECONDS?: string;
  REFRESH_TOKEN_SECONDS?: string;
  PASSWORD_ITERATIONS?: string;
  GEMINI_API_KEY?: string;
  AUTH_SIGNING_KEY?: string;
  CODE_PEPPER?: string;
  IP_HASH_PEPPER?: string;
  ADMIN_SETUP_TOKEN?: string;
}

export interface D1Like {
  prepare(sql: string): D1Prepared;
  batch(statements: D1Prepared[]): Promise<D1Result[]>;
}

export interface D1Prepared {
  bind(...values: unknown[]): D1Prepared;
  first<T>(): Promise<T | null>;
  all<T>(): Promise<{ results: T[] }>;
  run(): Promise<{ meta?: { changes?: number } }>;
}

export interface D1Result {
  results?: unknown[];
  meta?: { changes?: number };
}

function intSetting(value: string | undefined, fallback: number): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed > 0 ? Math.floor(parsed) : fallback;
}

export function readConfig(env: WorkerEnv): Config {
  const mode = env.TURNSTILE_MODE === "development" ? "development" : "production";
  const download = (env.DOWNLOAD_URL ?? "").trim();
  return {
    turnstileMode: mode,
    allowDevVerification: env.ALLOW_DEV_VERIFICATION === "true",
    turnstileSecret: env.TURNSTILE_SECRET ?? "",
    turnstileSiteKey: env.TURNSTILE_SITE_KEY ?? "",
    expectedHostname: (env.EXPECTED_HOSTNAME ?? "").trim().toLowerCase(),
    authSigningKey: env.AUTH_SIGNING_KEY ?? "",
    codePepper: env.CODE_PEPPER ?? "",
    ipHashPepper: env.IP_HASH_PEPPER ?? "",
    adminSetupToken: env.ADMIN_SETUP_TOKEN ?? "",
    webOrigin: (env.WEB_ORIGIN ?? "").trim(),
    downloadUrl: download.startsWith("https://") ? download : "",
    appVersion: env.APP_VERSION ?? "0.1.0",
    accessTokenSeconds: intSetting(env.ACCESS_TOKEN_SECONDS, 600),
    refreshTokenSeconds: intSetting(env.REFRESH_TOKEN_SECONDS, 60 * 60 * 24 * 30),
    passwordIterations: intSetting(env.PASSWORD_ITERATIONS, 120_000),
    geminiApiKey: env.GEMINI_API_KEY ?? "",
  };
}

export function assertProductionConfig(config: Config): void {
  if (config.turnstileMode !== "production") {
    throw new Error("production_mode_required");
  }
  const missing: string[] = [];
  if (config.authSigningKey.length < 32) missing.push("AUTH_SIGNING_KEY");
  if (config.codePepper.length < 16) missing.push("CODE_PEPPER");
  if (config.ipHashPepper.length < 16) missing.push("IP_HASH_PEPPER");
  if (config.turnstileSecret.length < 8) missing.push("TURNSTILE_SECRET");
  if (!config.expectedHostname) missing.push("EXPECTED_HOSTNAME");
  if (config.allowDevVerification) missing.push("ALLOW_DEV_VERIFICATION");
  if (config.passwordIterations < 100_000) missing.push("PASSWORD_ITERATIONS");
  if (missing.length > 0) {
    throw new Error(`missing_configuration:${missing.join(",")}`);
  }
}
