import { assertProductionConfig, type Config } from "./config.js";
import {
  hashSecret,
  pbkdf2,
  randomBytes,
  bytesToBase64,
  base64ToBytes,
  safeEqual,
  signAccessToken,
  verifyAccessToken,
  verifySignature,
  importVerifyKey,
  sha256Hex,
  type AccessClaims,
} from "./crypto.js";
import type { Db, SqlValue, Statement } from "./db.js";
import { ApiError } from "./errors.js";
import { errorBody, idempotencyKey, json, readJsonObject, requestHost, textField, withHeaders } from "./http.js";
import { generateDeviceId, generateOpaqueToken, generateOwnerCode, hasPlan, isDeviceId, isOwnerCode, normalizeDeviceId, resolvePlan, type EntitlementRecord, type Plan } from "./ids.js";
import { constraintMessage, withIdempotency } from "./idempotency.js";
import { DEVICES_PER_IP, parseIp } from "./ip.js";
import { openCode, sealCode } from "./vault.js";
import { logError } from "./db.js";
import { purgeExpired } from "./maintenance.js";
import { enforceRateLimit } from "./rateLimit.js";
import { createTurnstileVerifier, hashTurnstileToken, type TurnstileVerifier } from "./turnstile.js";

export interface AppDeps {
  db: Db;
  config: Config;
  turnstile: TurnstileVerifier;
  now: () => Date;
  assets?: (request: Request) => Promise<Response>;
}

interface DeviceRow {
  id: string;
  public_id: string;
  status: string;
  activation_code_hash: string | null;
  recovery_code_hash: string;
  created_at: string;
  claimed_at: string | null;
  last_seen_at: string | null;
  revoked_at: string | null;
}

interface CredentialRow {
  public_key_spki: string;
  installation_id: string;
}

const UUID_PATTERN = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

export async function handleRequest(request: Request, deps: AppDeps): Promise<Response> {
  const url = new URL(request.url);
  try {
    if (request.method === "OPTIONS" && url.pathname.startsWith("/api/")) {
      return finish(request, new Response(null, { status: 204 }), deps, false);
    }
    if (url.pathname.startsWith("/api/")) {
      return finish(request, await routeApi(request, url, deps), deps, false);
    }
    if (!deps.assets) {
      return finish(request, json({ error: { code: "not_found", message: "Not found" } }, 404), deps, false);
    }
    const asset = await deps.assets(request);
    const html = (asset.headers.get("content-type") ?? "").includes("text/html");
    return finish(request, asset, deps, html);
  } catch (error) {
    if (error instanceof ApiError) return finish(request, errorBody(error), deps, false);
    logError("unhandled", { message: error instanceof Error ? error.message : "unknown", path: url.pathname });
    return finish(request, json({ error: { code: "internal_error", message: "요청을 처리하지 못했습니다." } }, 500), deps, false);
  }
}

export async function handleScheduled(deps: AppDeps): Promise<void> {
  await purgeExpired(deps.db, deps.now());
}

function finish(request: Request, response: Response, deps: AppDeps, html: boolean): Response {
  return withHeaders(request, response, deps.config, html);
}

async function routeApi(request: Request, url: URL, deps: AppDeps): Promise<Response> {
  const path = url.pathname.replace(/\/+$/, "") || "/";
  const method = request.method.toUpperCase();
  if (method === "GET" && path === "/api/v1/health") return health(deps);
  if (method === "GET" && path === "/api/v1/public/config") return publicConfig(deps);
  if (method === "POST" && path === "/api/v1/verification/turnstile") return verifyTurnstile(request, deps);
  if (method === "POST" && path === "/api/v1/ip/check") return checkIp(request, deps);
  if (method === "POST" && path === "/api/v1/ip/devices") return ipDevices(request, deps);
  if (method === "POST" && path === "/api/v1/devices") return createDevice(request, deps);
  if (method === "POST" && path === "/api/v1/devices/recover") return recoverDevice(request, deps);
  if (method === "POST" && path === "/api/v1/devices/delete") return deleteDevice(request, deps);
  if (method === "POST" && path === "/api/v1/auth/challenge") return challenge(request, deps);
  if (method === "POST" && path === "/api/v1/auth/activate") return activate(request, deps);
  if (method === "POST" && path === "/api/v1/auth/prove") return prove(request, deps);
  if (method === "POST" && path === "/api/v1/auth/refresh") return refresh(request, deps);
  if (method === "POST" && path === "/api/v1/auth/logout") return logout(request, deps);
  if (method === "GET" && path === "/api/v1/devices/me") return deviceMe(request, deps);
  if (method === "GET" && path === "/api/v1/entitlements/me") return entitlementsMe(request, deps);
  if (method === "POST" && path === "/api/v1/admin/bootstrap") return adminBootstrap(request, deps);
  if (method === "POST" && path === "/api/v1/admin/login") return adminLogin(request, deps);
  if (method === "POST" && path === "/api/v1/admin/logout") return adminLogout(request, deps);
  if (method === "GET" && path === "/api/v1/admin/devices") return adminGetDevice(request, url, deps);
  if (method === "POST" && path === "/api/v1/admin/devices/revoke") return adminRevoke(request, deps);
  if (method === "POST" && path === "/api/v1/admin/devices/restore") return adminRestore(request, deps);
  if (method === "POST" && path === "/api/v1/admin/devices/delete") return adminDelete(request, deps);
  if (method === "GET" && path === "/api/v1/admin/events") return adminEvents(request, url, deps);
  if (method === "GET" && path === "/api/v1/admin/abuse") return adminAbuse(request, deps);
  if (method === "POST" && path === "/api/v1/admin/entitlements") return adminEntitlement(request, deps);
  if (method === "GET" && path === "/api/v1/admin/audit") return adminAudit(request, deps);
  throw new ApiError(404, "not_found", "요청한 API가 없습니다.");
}

function ensureSecrets(config: Config): void {
  if (config.authSigningKey.length < 32 || config.codePepper.length < 16 || config.ipHashPepper.length < 16) {
    throw new ApiError(503, "configuration_error", "서버 구성이 완료되지 않았습니다.");
  }
  if (config.turnstileMode === "production") {
    try {
      assertProductionConfig(config);
    } catch {
      throw new ApiError(503, "configuration_error", "서버 구성이 완료되지 않았습니다.");
    }
  }
}

async function health(deps: AppDeps): Promise<Response> {
  try {
    await deps.db.get("SELECT 1 AS ok");
  } catch {
    return json({ status: "unavailable" }, 503);
  }
  return json({ status: "ok", version: deps.config.appVersion });
}

function publicConfig(deps: AppDeps): Response {
  const devVerification = deps.config.turnstileMode === "development" && deps.config.allowDevVerification;
  return json({
    product: "NIVEX",
    version: deps.config.appVersion,
    turnstileSiteKey: deps.config.turnstileSiteKey,
    devVerification,
    consentVersion: CONSENT_VERSION,
    downloadUrl: deps.config.downloadUrl,
  });
}

async function verifyTurnstile(request: Request, deps: AppDeps): Promise<Response> {
  ensureSecrets(deps.config);
  const now = deps.now();
  await enforceRateLimit(deps.db, deps.config, request, "turnstile", 12, 600, now);
  const body = await readJsonObject(request);
  const token = textField(body, "token");
  const action = textField(body, "action");
  if (!token || !["device_create", "device_recover", "device_delete"].includes(action)) {
    throw new ApiError(400, "invalid_request", "보안 확인 정보가 올바르지 않습니다.");
  }
  const result = await deps.turnstile.verify(token, requestHost(request), request.headers.get("cf-connecting-ip") ?? "", action);
  if (!result.ok) throw new ApiError(403, "verification_failed", "보안 확인에 실패했습니다. 다시 시도하세요.");
  const tokenHash = await hashTurnstileToken(token);
  const id = crypto.randomUUID();
  const expires = new Date(now.getTime() + 10 * 60 * 1000).toISOString();
  try {
    await deps.db.run(
      `INSERT INTO verifications (id, token_hash, action, hostname, expires_at, used_at, created_at)
       VALUES (?, ?, ?, ?, ?, NULL, ?)`,
      [id, tokenHash, action, result.hostname ?? requestHost(request), expires, now.toISOString()],
    );
  } catch (error) {
    if (constraintMessage(error).toLowerCase().includes("unique")) {
      throw new ApiError(403, "verification_failed", "이미 사용된 보안 확인입니다.");
    }
    throw error;
  }
  return json({ verificationId: id, expiresAt: expires }, 201);
}

export const CONSENT_VERSION = "2026-10-29";

// The server never trusts a bare `consent: true`; the request must name the policy version being accepted.
function requireConsent(body: Record<string, unknown>): void {
  const consent = body.consent;
  const accepted =
    typeof consent === "object" && consent !== null && !Array.isArray(consent) &&
    (consent as Record<string, unknown>).accepted === true &&
    (consent as Record<string, unknown>).version === CONSENT_VERSION;
  if (!accepted) {
    throw new ApiError(403, "consent_required", "IP 주소 수집·이용에 동의해야 이 기능을 사용할 수 있습니다.");
  }
}

async function ipQuotaKey(deps: AppDeps, request: Request, body: Record<string, unknown>): Promise<string> {
  requireConsent(body);
  const typed = parseIp(textField(body, "ip"));
  if (!typed) throw new ApiError(400, "ip_invalid", "IP 주소 형식이 올바르지 않습니다.");
  const seen = parseIp(request.headers.get("cf-connecting-ip") ?? "");
  if (!seen || seen.canonical !== typed.canonical) {
    throw new ApiError(403, "ip_mismatch", "입력한 IP가 현재 접속 IP와 일치하지 않습니다.");
  }
  return sha256Hex(`${deps.config.ipHashPepper}|device_ip|${seen.quotaKey}`);
}

async function ipUsage(deps: AppDeps, ipHash: string): Promise<number> {
  const row = await deps.db.get<{ used: number }>("SELECT COUNT(*) AS used FROM ip_claims WHERE ip_hash = ?", [ipHash]);
  return Number(row?.used ?? 0);
}

async function checkIp(request: Request, deps: AppDeps): Promise<Response> {
  ensureSecrets(deps.config);
  await enforceRateLimit(deps.db, deps.config, request, "ip_check", 30, 3600, deps.now());
  const body = await readJsonObject(request);
  const ipHash = await ipQuotaKey(deps, request, body);
  const used = await ipUsage(deps, ipHash);
  return json({ matched: true, limit: DEVICES_PER_IP, used, remaining: Math.max(0, DEVICES_PER_IP - used) });
}

function ipClaim(ipHash: string, devicePublicId: string, nowIso: string, activationEnc: string): Statement {
  return {
    sql: `INSERT INTO ip_claims (id, ip_hash, device_public_id, created_at, activation_enc)
          SELECT ?, ?, ?, ?, ? WHERE (SELECT COUNT(*) FROM ip_claims WHERE ip_hash = ?) < ?
          UNION ALL
          SELECT NULL, NULL, NULL, NULL, NULL WHERE (SELECT COUNT(*) FROM ip_claims WHERE ip_hash = ?) >= ?`,
    params: [crypto.randomUUID(), ipHash, devicePublicId, nowIso, activationEnc, ipHash, DEVICES_PER_IP, ipHash, DEVICES_PER_IP],
  };
}

interface ClaimRow {
  device_public_id: string;
  created_at: string;
  activation_enc: string | null;
  status: string | null;
  activation_code_hash: string | null;
}

async function ipDevices(request: Request, deps: AppDeps): Promise<Response> {
  ensureSecrets(deps.config);
  await enforceRateLimit(deps.db, deps.config, request, "ip_devices", 20, 3600, deps.now());
  const body = await readJsonObject(request);
  const ipHash = await ipQuotaKey(deps, request, body);
  const rows = await deps.db.all<ClaimRow>(
    `SELECT c.device_public_id, c.created_at, c.activation_enc, d.status, d.activation_code_hash
       FROM ip_claims c LEFT JOIN devices d ON d.public_id = c.device_public_id
      WHERE c.ip_hash = ? ORDER BY c.created_at`,
    [ipHash],
  );
  const devices = [];
  for (const row of rows) {
    if (!row.status) continue;
    let activationCode: string | null = null;
    if (row.status === "unclaimed" && row.activation_code_hash) {
      const opened = await openCode(deps.config.codePepper, row.device_public_id, row.activation_enc);
      // A recovery may have replaced the code, so only show a code that still matches the server hash.
      if (opened && (await safeEqual(await hashSecret(deps.config.codePepper, "activation", opened), row.activation_code_hash))) {
        activationCode = opened;
      }
    }
    devices.push({ deviceId: row.device_public_id, createdAt: row.created_at, status: row.status, activationCode });
  }
  return json({ matched: true, limit: DEVICES_PER_IP, used: await ipUsage(deps, ipHash), devices });
}

async function createDevice(request: Request, deps: AppDeps): Promise<Response> {
  ensureSecrets(deps.config);
  const now = deps.now();
  await enforceRateLimit(deps.db, deps.config, request, "device_create", 8, 3600, now);
  const key = idempotencyKey(request);
  const body = await readJsonObject(request);
  const verificationId = textField(body, "verificationId");
  if (!UUID_PATTERN.test(verificationId)) throw new ApiError(400, "verification_invalid", "보안 확인이 필요합니다.");
  const ipHash = await ipQuotaKey(deps, request, body);
  const requestHash = await sha256Hex(`device_create\n${verificationId}\n${ipHash}`);
  const existing = await deps.db.get<{ state: string }>("SELECT state FROM idempotency_records WHERE idempotency_key = ?", [key]);
  if (!existing && (await ipUsage(deps, ipHash)) >= DEVICES_PER_IP) {
    throw new ApiError(403, "ip_limit", `이 IP에서는 장치 ID를 최대 ${DEVICES_PER_IP}개까지만 만들 수 있습니다.`);
  }
  for (let attempt = 0; attempt < 5; attempt += 1) {
    try {
      const outcome = await withIdempotency(deps.db, now, key, requestHash, async () => {
        const publicId = generateDeviceId();
        const activationCode = generateOwnerCode();
        const recoveryCode = generateOwnerCode();
        const activationHash = await hashSecret(deps.config.codePepper, "activation", activationCode);
        const recoveryHash = await hashSecret(deps.config.codePepper, "recovery", recoveryCode);
        const deviceId = crypto.randomUUID();
        const entitlementId = crypto.randomUUID();
        const createdAt = now.toISOString();
        const response = { deviceId: publicId, activationCode, recoveryCode, createdAt };
        return {
          status: 201,
          body: response,
          statements: [
            redeem(verificationId, "device_create", createdAt),
            markVerificationUsed(verificationId, createdAt),
            {
              sql: `INSERT INTO devices (
                     id, public_id, status, activation_code_hash, recovery_code_hash, created_at, claimed_at, last_seen_at, revoked_at
                   ) VALUES (?, ?, 'unclaimed', ?, ?, ?, NULL, NULL, NULL)`,
              params: [deviceId, publicId, activationHash, recoveryHash, createdAt],
            },
            {
              sql: `INSERT INTO entitlements (
                     id, device_id, plan, status, source, starts_at, expires_at, created_at, revoked_at
                   ) VALUES (?, ?, 'FREE', 'active', 'system', ?, NULL, ?, NULL)`,
              params: [entitlementId, deviceId, createdAt, createdAt],
            },
            authEvent(publicId, "create", "success", createdAt),
            {
              sql: "INSERT INTO consent_records (id, device_public_id, policy_version, accepted_at) VALUES (?, ?, ?, ?)",
              params: [crypto.randomUUID(), publicId, CONSENT_VERSION, createdAt],
            },
            ipClaim(ipHash, publicId, createdAt, await sealCode(deps.config.codePepper, publicId, activationCode)),
          ],
        };
      });
      return json(outcome.body, outcome.status);
    } catch (error) {
      if (attempt < 4 && constraintMessage(error).includes("devices.public_id")) continue;
      rethrowConstraint(error);
    }
  }
  throw new ApiError(500, "internal_error", "요청을 처리하지 못했습니다.");
}

async function recoverDevice(request: Request, deps: AppDeps): Promise<Response> {
  return mutateWithRecovery(request, deps, "device_recover", "device_recover", 5, async (device, now) => {
    const activationCode = generateOwnerCode();
    const recoveryCode = generateOwnerCode();
    const activationHash = await hashSecret(deps.config.codePepper, "activation", activationCode);
    const recoveryHash = await hashSecret(deps.config.codePepper, "recovery", recoveryCode);
    const instant = now.toISOString();
    return {
      status: 200,
      body: { deviceId: device.public_id, activationCode, recoveryCode, createdAt: instant },
      statements: [
        { sql: "DELETE FROM device_credentials WHERE device_id = ?", params: [device.id] },
        { sql: "UPDATE sessions SET revoked_at = ? WHERE device_id = ? AND revoked_at IS NULL", params: [instant, device.id] },
        { sql: "UPDATE challenges SET used_at = ? WHERE device_id = ? AND used_at IS NULL", params: [instant, device.id] },
        {
          sql: `UPDATE devices
                SET status = 'unclaimed', activation_code_hash = ?, recovery_code_hash = ?, claimed_at = NULL, revoked_at = NULL
                WHERE id = ? AND recovery_code_hash = ?`,
          params: [activationHash, recoveryHash, device.id, device.recovery_code_hash],
        },
        guardFromDevice(device.id, activationHash, "recovery", instant),
      ],
    };
  });
}

async function deleteDevice(request: Request, deps: AppDeps): Promise<Response> {
  return mutateWithRecovery(request, deps, "device_delete", "device_delete", 5, async (device, now) => {
    const instant = now.toISOString();
    return {
      status: 200,
      body: { deleted: true },
      statements: [
        { sql: "DELETE FROM challenges WHERE device_id = ?", params: [device.id] },
        { sql: "DELETE FROM sessions WHERE device_id = ?", params: [device.id] },
        { sql: "DELETE FROM device_credentials WHERE device_id = ?", params: [device.id] },
        { sql: "DELETE FROM entitlement_events WHERE device_id = ?", params: [device.id] },
        { sql: "DELETE FROM entitlements WHERE device_id = ?", params: [device.id] },
        { sql: "DELETE FROM auth_events WHERE device_public_id = ?", params: [device.public_id] },
        { sql: "DELETE FROM devices WHERE id = ? AND recovery_code_hash = ?", params: [device.id, device.recovery_code_hash] },
        {
          sql: `INSERT INTO admin_audit (id, admin_id, action, target, detail, created_at)
                SELECT ?, NULL, 'device_deleted', ?, 'owner', ?
                WHERE NOT EXISTS (SELECT 1 FROM devices WHERE id = ?)
                UNION ALL
                SELECT NULL, NULL, NULL, NULL, NULL, NULL
                WHERE EXISTS (SELECT 1 FROM devices WHERE id = ?)`,
          params: [crypto.randomUUID(), device.public_id, instant, device.id, device.id],
        },
      ],
    };
  });
}

async function challenge(request: Request, deps: AppDeps): Promise<Response> {
  ensureSecrets(deps.config);
  const now = deps.now();
  await enforceRateLimit(deps.db, deps.config, request, "challenge", 30, 600, now);
  const body = await readJsonObject(request);
  const deviceId = normalizeDeviceId(textField(body, "deviceId"));
  if (!isDeviceId(deviceId)) throw new ApiError(400, "invalid_device_id", "장치 ID 형식이 올바르지 않습니다.");
  const device = await findDevice(deps.db, deviceId);
  if (!device) return json({ status: "unknown" });
  if (device.status === "revoked") return json({ status: "revoked" });
  if (device.status === "unclaimed") return json({ status: "unclaimed" });
  const nonce = bytesToBase64(randomBytes(32));
  const nonceHash = await hashSecret(deps.config.codePepper, "nonce", nonce);
  const expires = new Date(now.getTime() + 2 * 60 * 1000).toISOString();
  await deps.db.run(
    "INSERT INTO challenges (id, device_id, nonce_hash, expires_at, used_at) VALUES (?, ?, ?, ?, NULL)",
    [crypto.randomUUID(), device.id, nonceHash, expires],
  );
  return json({ status: "active", nonce, expiresAt: expires });
}

async function activate(request: Request, deps: AppDeps): Promise<Response> {
  ensureSecrets(deps.config);
  const now = deps.now();
  await enforceRateLimit(deps.db, deps.config, request, "activate", 10, 600, now);
  const body = await readJsonObject(request);
  const device = await requireKnownDevice(deps.db, textField(body, "deviceId"));
  if (device.status === "revoked") throw new ApiError(403, "device_revoked", "이 장치 ID는 사용할 수 없습니다.");
  const activationCode = textField(body, "activationCode").toUpperCase();
  const publicKey = textField(body, "publicKeySpki");
  const installationId = textField(body, "installationId");
  if (!isOwnerCode(activationCode) || !UUID_PATTERN.test(installationId) || !publicKey) {
    throw new ApiError(400, "invalid_request", "활성화 정보가 올바르지 않습니다.");
  }
  try {
    await importVerifyKey(publicKey);
  } catch {
    throw new ApiError(400, "invalid_request", "장치 공개 키가 올바르지 않습니다.");
  }
  const existing = await deps.db.get<CredentialRow>(
    "SELECT public_key_spki, installation_id FROM device_credentials WHERE device_id = ?",
    [device.id],
  );
  if (existing) {
    throw new ApiError(
      409,
      existing.public_key_spki === publicKey ? "already_activated" : "device_already_claimed",
      existing.public_key_spki === publicKey
        ? "이 설치는 이미 활성화되어 있습니다."
        : "이 장치 ID는 이미 다른 설치에 연결되어 있습니다.",
    );
  }
  if (device.status !== "unclaimed" || !device.activation_code_hash) {
    throw new ApiError(409, "device_already_claimed", "이 장치 ID는 이미 다른 설치에 연결되어 있습니다.");
  }
  const activationHash = await hashSecret(deps.config.codePepper, "activation", activationCode);
  if (!(await safeEqual(activationHash, device.activation_code_hash))) {
    await deps.db.run(
      "INSERT INTO auth_events (id, device_public_id, event_type, result, detail, created_at) VALUES (?, ?, 'activate', 'denied', NULL, ?)",
      [crypto.randomUUID(), device.public_id, now.toISOString()],
    );
    throw new ApiError(403, "activation_invalid", "활성화 코드가 올바르지 않습니다.");
  }
  const instant = now.toISOString();
  try {
    await deps.db.batch([
      {
        sql: `INSERT INTO device_credentials (device_id, public_key_spki, installation_id, created_at)
              SELECT id, ?, ?, ? FROM devices
              WHERE id = ? AND status = 'unclaimed' AND activation_code_hash = ?
              UNION ALL
              SELECT NULL, NULL, NULL, NULL
              WHERE NOT EXISTS (
                SELECT 1 FROM devices WHERE id = ? AND status = 'unclaimed' AND activation_code_hash = ?
              )`,
        params: [publicKey, installationId, instant, device.id, activationHash, device.id, activationHash],
      },
      {
        sql: `UPDATE devices
              SET status = 'active', claimed_at = ?, activation_code_hash = NULL, last_seen_at = ?
              WHERE id = ?`,
        params: [instant, instant, device.id],
      },
      authEvent(device.public_id, "activate", "success", instant),
      { sql: "UPDATE ip_claims SET activation_enc = NULL WHERE device_public_id = ?", params: [device.public_id] },
    ]);
  } catch (error) {
    const current = await deps.db.get<CredentialRow>(
      "SELECT public_key_spki, installation_id FROM device_credentials WHERE device_id = ?",
      [device.id],
    );
    if (current?.public_key_spki === publicKey) {
      throw new ApiError(409, "already_activated", "이 설치는 이미 활성화되어 있습니다.");
    }
    if (constraintMessage(error).includes("NOT NULL") || current) {
      throw new ApiError(409, "device_already_claimed", "이 장치 ID는 이미 다른 설치에 연결되어 있습니다.");
    }
    throw error;
  }
  const tokens = await issueSession(deps, device, now);
  return json(tokens, 201);
}

async function prove(request: Request, deps: AppDeps): Promise<Response> {
  ensureSecrets(deps.config);
  const now = deps.now();
  await enforceRateLimit(deps.db, deps.config, request, "prove", 20, 600, now);
  const body = await readJsonObject(request);
  const device = await requireKnownDevice(deps.db, textField(body, "deviceId"));
  if (device.status === "revoked") throw new ApiError(403, "device_revoked", "이 장치 ID는 사용할 수 없습니다.");
  if (device.status !== "active") throw new ApiError(409, "activation_required", "이 장치 ID는 아직 활성화되지 않았습니다.");
  const credential = await deps.db.get<CredentialRow>(
    "SELECT public_key_spki, installation_id FROM device_credentials WHERE device_id = ?",
    [device.id],
  );
  if (!credential) throw new ApiError(409, "activation_required", "이 장치 ID는 아직 활성화되지 않았습니다.");
  const nonce = textField(body, "nonce");
  const signature = textField(body, "signature");
  if (!nonce || !signature) throw new ApiError(400, "invalid_request", "인증 정보가 올바르지 않습니다.");
  const nonceHash = await hashSecret(deps.config.codePepper, "nonce", nonce);
  const challengeRow = await deps.db.get<{ id: string }>(
    `SELECT id FROM challenges
     WHERE device_id = ? AND nonce_hash = ? AND used_at IS NULL AND expires_at > ?`,
    [device.id, nonceHash, now.toISOString()],
  );
  if (!challengeRow) throw new ApiError(401, "challenge_invalid", "인증 요청이 만료되었습니다. 다시 시도하세요.");
  let valid = false;
  try {
    valid = await verifySignature(credential.public_key_spki, nonce, signature);
  } catch {
    valid = false;
  }
  if (!valid) throw new ApiError(401, "invalid_signature", "이 설치의 장치 자격 증명으로 확인하지 못했습니다.");
  const instant = now.toISOString();
  const session = await prepareSession(deps, device, now);
  try {
    await deps.db.batch([
      {
        sql: "UPDATE challenges SET used_at = ? WHERE id = ? AND used_at IS NULL",
        params: [session.sessionId, challengeRow.id],
      },
      sessionInsert(session, instant, "EXISTS (SELECT 1 FROM challenges WHERE id = ? AND used_at = ?)", [
        challengeRow.id,
        session.sessionId,
      ]),
      { sql: "UPDATE devices SET last_seen_at = ? WHERE id = ?", params: [instant, device.id] },
      authEvent(device.public_id, "login", "success", instant),
    ]);
  } catch (error) {
    if (constraintMessage(error).includes("NOT NULL") || constraintMessage(error).toLowerCase().includes("unique")) {
      throw new ApiError(401, "challenge_invalid", "인증 요청이 만료되었습니다. 다시 시도하세요.");
    }
    throw error;
  }
  return json(session.body, 200);
}

async function refresh(request: Request, deps: AppDeps): Promise<Response> {
  ensureSecrets(deps.config);
  const now = deps.now();
  await enforceRateLimit(deps.db, deps.config, request, "refresh", 60, 600, now);
  const body = await readJsonObject(request);
  const refreshToken = textField(body, "refreshToken");
  if (!refreshToken.startsWith("nrt_")) throw new ApiError(401, "session_expired", "세션이 만료되었습니다. 다시 로그인하세요.");
  const refreshHash = await hashSecret(deps.config.codePepper, "refresh", refreshToken);
  const current = await deps.db.get<{ id: string; device_id: string }>(
    `SELECT id, device_id FROM sessions
     WHERE refresh_token_hash = ? AND revoked_at IS NULL AND expires_at > ?`,
    [refreshHash, now.toISOString()],
  );
  if (!current) throw new ApiError(401, "session_expired", "세션이 만료되었습니다. 다시 로그인하세요.");
  const device = await deps.db.get<DeviceRow>("SELECT * FROM devices WHERE id = ?", [current.device_id]);
  if (!device || device.status !== "active") throw new ApiError(403, "device_revoked", "이 장치 ID는 사용할 수 없습니다.");
  const session = await prepareSession(deps, device, now);
  const consumed = await hashSecret(deps.config.codePepper, "consumed", `${current.id}:${session.refreshToken}`);
  const instant = now.toISOString();
  try {
    await deps.db.batch([
      {
        sql: `UPDATE sessions
              SET refresh_token_hash = ?, revoked_at = ?, replaced_by = ?
              WHERE id = ? AND revoked_at IS NULL AND refresh_token_hash = ?`,
        params: [consumed, instant, session.sessionId, current.id, refreshHash],
      },
      sessionInsert(session, instant, "EXISTS (SELECT 1 FROM sessions WHERE id = ? AND refresh_token_hash = ?)", [current.id, consumed]),
      { sql: "UPDATE devices SET last_seen_at = ? WHERE id = ?", params: [instant, device.id] },
    ]);
  } catch (error) {
    if (constraintMessage(error).includes("NOT NULL") || constraintMessage(error).toLowerCase().includes("unique")) {
      throw new ApiError(401, "session_expired", "세션이 만료되었습니다. 다시 로그인하세요.");
    }
    throw error;
  }
  return json(session.body, 200);
}

async function logout(request: Request, deps: AppDeps): Promise<Response> {
  const user = await requireUser(request, deps);
  const body = await readJsonObject(request);
  const refreshToken = textField(body, "refreshToken");
  const refreshHash = await hashSecret(deps.config.codePepper, "refresh", refreshToken);
  const revoked = await deps.db.run(
    "UPDATE sessions SET revoked_at = ? WHERE id = ? AND device_id = ? AND refresh_token_hash = ? AND revoked_at IS NULL",
    [deps.now().toISOString(), user.sessionId, user.device.id, refreshHash],
  );
  if (revoked.changes !== 1) throw new ApiError(401, "session_expired", "세션이 만료되었습니다. 다시 로그인하세요.");
  await deps.db.run(
    "INSERT INTO auth_events (id, device_public_id, event_type, result, detail, created_at) VALUES (?, ?, 'logout', 'success', NULL, ?)",
    [crypto.randomUUID(), user.device.public_id, deps.now().toISOString()],
  );
  return json({ loggedOut: true });
}

async function deviceMe(request: Request, deps: AppDeps): Promise<Response> {
  const user = await requireUser(request, deps);
  const now = deps.now().toISOString();
  await deps.db.run("UPDATE devices SET last_seen_at = ? WHERE id = ?", [now, user.device.id]);
  const fresh = await findDevice(deps.db, user.device.public_id);
  const events = await deps.db.all<{ event_type: string; result: string; created_at: string }>(
    `SELECT event_type, result, created_at FROM auth_events
     WHERE device_public_id = ? ORDER BY created_at DESC LIMIT 8`,
    [user.device.public_id],
  );
  const plan = await currentPlan(deps, user.device.id);
  return json({
    deviceId: fresh?.public_id,
    status: fresh?.status,
    createdAt: fresh?.created_at,
    claimedAt: fresh?.claimed_at,
    lastSeenAt: now,
    plan,
    recentEvents: events.map((event) => ({ type: event.event_type, result: event.result, at: event.created_at })),
  });
}

async function entitlementsMe(request: Request, deps: AppDeps): Promise<Response> {
  const user = await requireUser(request, deps);
  const records = await loadEntitlements(deps.db, user.device.id);
  const plan = resolvePlan(records, deps.now());
  return json({
    plan,
    features: [],
    free: hasPlan(records, "FREE", deps.now()),
    pro: hasPlan(records, "PRO", deps.now()),
  });
}

async function adminBootstrap(request: Request, deps: AppDeps): Promise<Response> {
  ensureSecrets(deps.config);
  const now = deps.now();
  await enforceRateLimit(deps.db, deps.config, request, "admin_bootstrap", 3, 3600, now);
  if (!deps.config.adminSetupToken) throw new ApiError(503, "configuration_error", "서버 구성이 완료되지 않았습니다.");
  const setup = request.headers.get("x-setup-token") ?? "";
  if (!(await safeEqual(setup, deps.config.adminSetupToken))) {
    throw new ApiError(401, "unauthorized", "운영자 인증이 필요합니다.");
  }
  const existing = await deps.db.get<{ count: number }>("SELECT COUNT(*) AS count FROM admin_users");
  if (Number(existing?.count ?? 0) > 0) throw new ApiError(409, "conflict", "운영자 계정이 이미 있습니다.");
  const body = await readJsonObject(request);
  const email = textField(body, "email").toLowerCase();
  const password = textField(body, "password");
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) || password.length < 12 || password.length > 200) {
    throw new ApiError(400, "invalid_request", "이메일과 12자 이상의 비밀번호가 필요합니다.");
  }
  const salt = randomBytes(16);
  const hash = await pbkdf2(password, salt, deps.config.passwordIterations);
  const id = crypto.randomUUID();
  const instant = now.toISOString();
  await deps.db.batch([
    {
      sql: `INSERT INTO admin_users (id, email, password_hash, password_salt, iterations, created_at)
            VALUES (?, ?, ?, ?, ?, ?)`,
      params: [id, email, hash, bytesToBase64(salt), deps.config.passwordIterations, instant],
    },
    {
      sql: "INSERT INTO admin_audit (id, admin_id, action, target, detail, created_at) VALUES (?, ?, 'bootstrap', NULL, NULL, ?)",
      params: [crypto.randomUUID(), id, instant],
    },
  ]);
  return json({ created: true }, 201);
}

async function adminLogin(request: Request, deps: AppDeps): Promise<Response> {
  ensureSecrets(deps.config);
  const now = deps.now();
  await enforceRateLimit(deps.db, deps.config, request, "admin_login", 8, 900, now);
  const body = await readJsonObject(request);
  const email = textField(body, "email").toLowerCase();
  const password = textField(body, "password");
  const user = await deps.db.get<{ id: string; password_hash: string; password_salt: string; iterations: number }>(
    "SELECT id, password_hash, password_salt, iterations FROM admin_users WHERE email = ?",
    [email],
  );
  const iterations = user ? Number(user.iterations) : deps.config.passwordIterations;
  if (iterations < 1000 || iterations > 400_000) throw new ApiError(401, "unauthorized", "이메일 또는 비밀번호가 올바르지 않습니다.");
  const actual = await pbkdf2(password, user ? base64ToBytes(user.password_salt) : randomBytes(16), iterations);
  if (!user || !(await safeEqual(actual, user.password_hash))) {
    throw new ApiError(401, "unauthorized", "이메일 또는 비밀번호가 올바르지 않습니다.");
  }
  const token = generateOpaqueToken("nad");
  const tokenHash = await hashSecret(deps.config.codePepper, "admin", token);
  const expires = new Date(now.getTime() + 8 * 60 * 60 * 1000).toISOString();
  await deps.db.batch([
    {
      sql: `INSERT INTO admin_sessions (id, admin_id, token_hash, expires_at, created_at, revoked_at)
            VALUES (?, ?, ?, ?, ?, NULL)`,
      params: [crypto.randomUUID(), user.id, tokenHash, expires, now.toISOString()],
    },
    {
      sql: "INSERT INTO admin_audit (id, admin_id, action, target, detail, created_at) VALUES (?, ?, 'login', NULL, NULL, ?)",
      params: [crypto.randomUUID(), user.id, now.toISOString()],
    },
  ]);
  return json({ token, expiresAt: expires });
}

async function adminLogout(request: Request, deps: AppDeps): Promise<Response> {
  const admin = await requireAdmin(request, deps);
  await deps.db.run("UPDATE admin_sessions SET revoked_at = ? WHERE id = ? AND revoked_at IS NULL", [
    deps.now().toISOString(),
    admin.sessionId,
  ]);
  return json({ loggedOut: true });
}

async function adminGetDevice(request: Request, url: URL, deps: AppDeps): Promise<Response> {
  await requireAdmin(request, deps);
  const deviceId = normalizeDeviceId(url.searchParams.get("deviceId") ?? "");
  if (!isDeviceId(deviceId)) throw new ApiError(400, "invalid_device_id", "장치 ID 형식이 올바르지 않습니다.");
  const device = await findDevice(deps.db, deviceId);
  if (!device) throw new ApiError(404, "not_found", "장치를 찾지 못했습니다.");
  const credential = await deps.db.get<{ installation_id: string; created_at: string }>(
    "SELECT installation_id, created_at FROM device_credentials WHERE device_id = ?",
    [device.id],
  );
  const entitlements = await deps.db.all<Record<string, unknown>>(
    "SELECT id, plan, status, source, starts_at, expires_at, created_at, revoked_at FROM entitlements WHERE device_id = ? ORDER BY created_at DESC",
    [device.id],
  );
  return json({
    deviceId: device.public_id,
    status: device.status,
    createdAt: device.created_at,
    claimedAt: device.claimed_at,
    lastSeenAt: device.last_seen_at,
    revokedAt: device.revoked_at,
    plan: await currentPlan(deps, device.id),
    installationId: credential?.installation_id ?? null,
    credentialCreatedAt: credential?.created_at ?? null,
    entitlements,
  });
}

async function adminRevoke(request: Request, deps: AppDeps): Promise<Response> {
  const admin = await requireAdmin(request, deps);
  const device = await deviceFromBody(request, deps);
  const instant = deps.now().toISOString();
  await deps.db.batch([
    { sql: "UPDATE devices SET status = 'revoked', revoked_at = ? WHERE id = ?", params: [instant, device.id] },
    { sql: "UPDATE sessions SET revoked_at = ? WHERE device_id = ? AND revoked_at IS NULL", params: [instant, device.id] },
    audit(admin.adminId, "revoke_device", device.public_id, instant),
    authEvent(device.public_id, "admin_revoke", "success", instant),
  ]);
  return json({ status: "revoked" });
}

async function adminRestore(request: Request, deps: AppDeps): Promise<Response> {
  const admin = await requireAdmin(request, deps);
  const device = await deviceFromBody(request, deps);
  const credential = await deps.db.get("SELECT device_id FROM device_credentials WHERE device_id = ?", [device.id]);
  const status = credential ? "active" : "unclaimed";
  const instant = deps.now().toISOString();
  await deps.db.batch([
    { sql: "UPDATE devices SET status = ?, revoked_at = NULL WHERE id = ?", params: [status, device.id] },
    audit(admin.adminId, "restore_device", device.public_id, instant),
    authEvent(device.public_id, "admin_restore", "success", instant),
  ]);
  return json({ status });
}

async function adminDelete(request: Request, deps: AppDeps): Promise<Response> {
  const admin = await requireAdmin(request, deps);
  const device = await deviceFromBody(request, deps);
  const instant = deps.now().toISOString();
  await deps.db.batch([
    audit(admin.adminId, "delete_device", device.public_id, instant),
    { sql: "DELETE FROM challenges WHERE device_id = ?", params: [device.id] },
    { sql: "DELETE FROM sessions WHERE device_id = ?", params: [device.id] },
    { sql: "DELETE FROM device_credentials WHERE device_id = ?", params: [device.id] },
    { sql: "DELETE FROM entitlement_events WHERE device_id = ?", params: [device.id] },
    { sql: "DELETE FROM entitlements WHERE device_id = ?", params: [device.id] },
    { sql: "DELETE FROM auth_events WHERE device_public_id = ?", params: [device.public_id] },
    { sql: "DELETE FROM devices WHERE id = ?", params: [device.id] },
  ]);
  return json({ deleted: true });
}

async function adminEvents(request: Request, url: URL, deps: AppDeps): Promise<Response> {
  await requireAdmin(request, deps);
  const deviceId = normalizeDeviceId(url.searchParams.get("deviceId") ?? "");
  if (!isDeviceId(deviceId)) throw new ApiError(400, "invalid_device_id", "장치 ID 형식이 올바르지 않습니다.");
  const events = await deps.db.all(
    "SELECT event_type, result, created_at FROM auth_events WHERE device_public_id = ? ORDER BY created_at DESC LIMIT 100",
    [deviceId],
  );
  return json({ events });
}

async function adminAbuse(request: Request, deps: AppDeps): Promise<Response> {
  await requireAdmin(request, deps);
  const events = await deps.db.all(
    "SELECT id, scope, reason, created_at FROM abuse_events ORDER BY created_at DESC LIMIT 100",
  );
  return json({ events });
}

async function adminEntitlement(request: Request, deps: AppDeps): Promise<Response> {
  const admin = await requireAdmin(request, deps);
  const body = await readJsonObject(request);
  const device = await requireKnownDevice(deps.db, textField(body, "deviceId"));
  const plan = textField(body, "plan");
  const action = textField(body, "action");
  if ((plan !== "FREE" && plan !== "PRO") || (action !== "grant" && action !== "revoke")) {
    throw new ApiError(400, "invalid_request", "이용권 요청이 올바르지 않습니다.");
  }
  const expiresRaw = textField(body, "expiresAt");
  const expiresAt = expiresRaw ? new Date(expiresRaw).toISOString() : null;
  if (expiresRaw && Number.isNaN(Date.parse(expiresRaw))) throw new ApiError(400, "invalid_request", "만료 시각이 올바르지 않습니다.");
  const instant = deps.now().toISOString();
  if (action === "revoke") {
    await deps.db.batch([
      {
        sql: "UPDATE entitlements SET status = 'revoked', revoked_at = ? WHERE device_id = ? AND plan = ? AND status = 'active'",
        params: [instant, device.id, plan],
      },
      entitlementEvent(device.id, "revoke", admin.adminId, plan, instant),
      audit(admin.adminId, "revoke_entitlement", device.public_id, instant),
    ]);
  } else {
    await deps.db.batch([
      {
        sql: "UPDATE entitlements SET status = 'revoked', revoked_at = ? WHERE device_id = ? AND plan = ? AND status = 'active'",
        params: [instant, device.id, plan],
      },
      {
        sql: `INSERT INTO entitlements (id, device_id, plan, status, source, starts_at, expires_at, created_at, revoked_at)
              VALUES (?, ?, ?, 'active', 'admin', ?, ?, ?, NULL)`,
        params: [crypto.randomUUID(), device.id, plan, instant, expiresAt, instant],
      },
      entitlementEvent(device.id, "grant", admin.adminId, plan, instant),
      audit(admin.adminId, "grant_entitlement", device.public_id, instant),
    ]);
  }
  return json({ plan: await currentPlan(deps, device.id) });
}

async function adminAudit(request: Request, deps: AppDeps): Promise<Response> {
  await requireAdmin(request, deps);
  const events = await deps.db.all(
    "SELECT id, admin_id, action, target, created_at FROM admin_audit ORDER BY created_at DESC LIMIT 100",
  );
  return json({ events });
}

async function mutateWithRecovery(
  request: Request,
  deps: AppDeps,
  action: string,
  scope: string,
  limit: number,
  build: (device: DeviceRow, now: Date) => Promise<{ status: number; body: unknown; statements: Statement[] }>,
): Promise<Response> {
  ensureSecrets(deps.config);
  const now = deps.now();
  await enforceRateLimit(deps.db, deps.config, request, scope, limit, 3600, now);
  const key = idempotencyKey(request);
  const body = await readJsonObject(request);
  const deviceId = normalizeDeviceId(textField(body, "deviceId"));
  const recoveryCode = textField(body, "recoveryCode").toUpperCase();
  const verificationId = textField(body, "verificationId");
  if (!isDeviceId(deviceId) || !isOwnerCode(recoveryCode) || !UUID_PATTERN.test(verificationId)) {
    throw new ApiError(400, "invalid_request", "복구 정보가 올바르지 않습니다.");
  }
  const device = await findDevice(deps.db, deviceId);
  if (!device) throw new ApiError(404, "not_found", "장치를 찾지 못했습니다.");
  const recoveryHash = await hashSecret(deps.config.codePepper, "recovery", recoveryCode);
  if (!(await safeEqual(recoveryHash, device.recovery_code_hash))) {
    throw new ApiError(403, "activation_invalid", "복구 코드가 올바르지 않습니다.");
  }
  const requestHash = await sha256Hex(`${action}\n${verificationId}\n${deviceId}\n${recoveryHash}`);
  try {
    const outcome = await withIdempotency(deps.db, now, key, requestHash, async () => {
      const built = await build(device, now);
      return {
        ...built,
        statements: [redeem(verificationId, action, now.toISOString()), markVerificationUsed(verificationId, now.toISOString()), ...built.statements],
      };
    });
    return json(outcome.body, outcome.status);
  } catch (error) {
    rethrowConstraint(error);
  }
}

function redeem(verificationId: string, action: string, nowIso: string): Statement {
  return {
    sql: `INSERT INTO verification_redemptions (verification_id, redeemed_at)
          SELECT id, ? FROM verifications
          WHERE id = ? AND action = ? AND used_at IS NULL AND expires_at > ?
          UNION ALL
          SELECT NULL, ?
          WHERE NOT EXISTS (
            SELECT 1 FROM verifications
            WHERE id = ? AND action = ? AND used_at IS NULL AND expires_at > ?
          )`,
    params: [nowIso, verificationId, action, nowIso, nowIso, verificationId, action, nowIso],
  };
}

function markVerificationUsed(verificationId: string, nowIso: string): Statement {
  return { sql: "UPDATE verifications SET used_at = ? WHERE id = ?", params: [nowIso, verificationId] };
}

function authEvent(devicePublicId: string, eventType: string, result: string, nowIso: string): Statement {
  return {
    sql: "INSERT INTO auth_events (id, device_public_id, event_type, result, detail, created_at) VALUES (?, ?, ?, ?, NULL, ?)",
    params: [crypto.randomUUID(), devicePublicId, eventType, result, nowIso],
  };
}

function guardFromDevice(deviceId: string, activationHash: string, eventType: string, nowIso: string): Statement {
  return {
    sql: `INSERT INTO auth_events (id, device_public_id, event_type, result, detail, created_at)
          SELECT ?, public_id, ?, 'success', NULL, ? FROM devices
          WHERE id = ? AND activation_code_hash = ?
          UNION ALL
          SELECT NULL, NULL, NULL, NULL, NULL, NULL
          WHERE NOT EXISTS (SELECT 1 FROM devices WHERE id = ? AND activation_code_hash = ?)`,
    params: [crypto.randomUUID(), eventType, nowIso, deviceId, activationHash, deviceId, activationHash],
  };
}

function audit(adminId: string, action: string, target: string, nowIso: string): Statement {
  return {
    sql: "INSERT INTO admin_audit (id, admin_id, action, target, detail, created_at) VALUES (?, ?, ?, ?, NULL, ?)",
    params: [crypto.randomUUID(), adminId, action, target, nowIso],
  };
}

function entitlementEvent(deviceId: string, action: string, actor: string, plan: string, nowIso: string): Statement {
  return {
    sql: `INSERT INTO entitlement_events (id, entitlement_id, device_id, action, actor, detail, created_at)
          VALUES (?, NULL, ?, ?, ?, ?, ?)`,
    params: [crypto.randomUUID(), deviceId, action, actor, plan, nowIso],
  };
}

async function findDevice(db: Db, publicId: string): Promise<DeviceRow | undefined> {
  return db.get<DeviceRow>("SELECT * FROM devices WHERE public_id = ?", [publicId]);
}

async function requireKnownDevice(db: Db, rawId: string): Promise<DeviceRow> {
  const deviceId = normalizeDeviceId(rawId);
  if (!isDeviceId(deviceId)) throw new ApiError(400, "invalid_device_id", "장치 ID 형식이 올바르지 않습니다.");
  const device = await findDevice(db, deviceId);
  if (!device) throw new ApiError(404, "not_found", "장치를 찾지 못했습니다.");
  return device;
}

async function deviceFromBody(request: Request, deps: AppDeps): Promise<DeviceRow> {
  const body = await readJsonObject(request);
  return requireKnownDevice(deps.db, textField(body, "deviceId"));
}

async function currentPlan(deps: AppDeps, deviceId: string): Promise<Plan | "NONE"> {
  return resolvePlan(await loadEntitlements(deps.db, deviceId), deps.now());
}

async function loadEntitlements(db: Db, deviceId: string): Promise<EntitlementRecord[]> {
  const rows = await db.all<{ plan: Plan; status: "active" | "revoked"; starts_at: string; expires_at: string | null }>(
    "SELECT plan, status, starts_at, expires_at FROM entitlements WHERE device_id = ?",
    [deviceId],
  );
  return rows.map((row) => ({ plan: row.plan, status: row.status, startsAt: row.starts_at, expiresAt: row.expires_at }));
}

interface IssuedSession {
  sessionId: string;
  refreshToken: string;
  refreshHash: string;
  expiresAt: string;
  body: { accessToken: string; refreshToken: string; expiresAt: string; deviceId: string };
}

async function prepareSession(deps: AppDeps, device: DeviceRow, now: Date): Promise<IssuedSession> {
  const sessionId = crypto.randomUUID();
  const refreshToken = generateOpaqueToken("nrt");
  const refreshHash = await hashSecret(deps.config.codePepper, "refresh", refreshToken);
  const accessExpires = new Date(now.getTime() + deps.config.accessTokenSeconds * 1000);
  const sessionExpires = new Date(now.getTime() + deps.config.refreshTokenSeconds * 1000).toISOString();
  const accessToken = await signAccessToken(deps.config.authSigningKey, {
    sub: device.public_id,
    sid: sessionId,
    iat: Math.floor(now.getTime() / 1000),
    exp: Math.floor(accessExpires.getTime() / 1000),
    typ: "access",
  });
  return {
    sessionId,
    refreshToken,
    refreshHash,
    expiresAt: sessionExpires,
    body: { accessToken, refreshToken, expiresAt: accessExpires.toISOString(), deviceId: device.public_id },
  };
}

function sessionInsert(session: IssuedSession, createdAt: string, predicateSql: string, predicateParams: SqlValue[]): Statement {
  return {
    sql: `INSERT INTO sessions (id, device_id, refresh_token_hash, created_at, expires_at, revoked_at, replaced_by)
          SELECT ?, id, ?, ?, ?, NULL, NULL FROM devices
          WHERE public_id = ? AND ${predicateSql}
          UNION ALL
          SELECT NULL, NULL, NULL, NULL, NULL, NULL, NULL
          WHERE NOT EXISTS (
            SELECT 1 FROM devices WHERE public_id = ? AND ${predicateSql}
          )`,
    params: [
      session.sessionId,
      session.refreshHash,
      createdAt,
      session.expiresAt,
      session.body.deviceId,
      ...predicateParams,
      session.body.deviceId,
      ...predicateParams,
    ],
  };
}

async function issueSession(deps: AppDeps, device: DeviceRow, now: Date): Promise<IssuedSession["body"]> {
  const session = await prepareSession(deps, device, now);
  await deps.db.run(
    `INSERT INTO sessions (id, device_id, refresh_token_hash, created_at, expires_at, revoked_at, replaced_by)
     VALUES (?, ?, ?, ?, ?, NULL, NULL)`,
    [session.sessionId, device.id, session.refreshHash, now.toISOString(), session.expiresAt],
  );
  return session.body;
}

async function requireUser(request: Request, deps: AppDeps): Promise<{ device: DeviceRow; sessionId: string; claims: AccessClaims }> {
  ensureSecrets(deps.config);
  const claims = await verifyAccessToken(
    deps.config.authSigningKey,
    bearer(request),
    Math.floor(deps.now().getTime() / 1000),
  );
  if (!claims) throw new ApiError(401, "unauthorized", "로그인이 필요합니다.");
  const device = await deps.db.get<DeviceRow>("SELECT * FROM devices WHERE public_id = ?", [claims.sub]);
  if (device?.status === "revoked") throw new ApiError(403, "device_revoked", "이 장치 ID는 사용할 수 없습니다.");
  const session = await deps.db.get<{ id: string; device_id: string; revoked_at: string | null; expires_at: string }>(
    "SELECT id, device_id, revoked_at, expires_at FROM sessions WHERE id = ?",
    [claims.sid],
  );
  if (!device || device.status !== "active" || !session || session.device_id !== device.id || session.revoked_at || Date.parse(session.expires_at) <= deps.now().getTime()) {
    throw new ApiError(401, "session_expired", "세션이 만료되었습니다. 다시 로그인하세요.");
  }
  return { device, sessionId: session.id, claims };
}

async function requireAdmin(request: Request, deps: AppDeps): Promise<{ adminId: string; sessionId: string }> {
  ensureSecrets(deps.config);
  const token = bearer(request);
  if (!token.startsWith("nad_")) throw new ApiError(401, "unauthorized", "운영자 인증이 필요합니다.");
  const tokenHash = await hashSecret(deps.config.codePepper, "admin", token);
  const session = await deps.db.get<{ id: string; admin_id: string; expires_at: string; revoked_at: string | null }>(
    "SELECT id, admin_id, expires_at, revoked_at FROM admin_sessions WHERE token_hash = ?",
    [tokenHash],
  );
  if (!session || session.revoked_at || Date.parse(session.expires_at) <= deps.now().getTime()) {
    throw new ApiError(401, "unauthorized", "운영자 인증이 필요합니다.");
  }
  return { adminId: session.admin_id, sessionId: session.id };
}

function bearer(request: Request): string {
  const header = request.headers.get("authorization") ?? "";
  return header.toLowerCase().startsWith("bearer ") ? header.slice(7).trim() : "";
}

function rethrowConstraint(error: unknown): never {
  const message = constraintMessage(error);
  if (message.includes("ip_claims")) {
    throw new ApiError(403, "ip_limit", `이 IP에서는 장치 ID를 최대 ${DEVICES_PER_IP}개까지만 만들 수 있습니다.`);
  }
  if (message.includes("verification_redemptions")) {
    throw new ApiError(400, "verification_invalid", "보안 확인이 만료되었거나 이미 사용되었습니다.");
  }
  if (message.includes("admin_audit") && message.includes("NOT NULL")) {
    throw new ApiError(409, "conflict", "장치를 삭제하지 못했습니다. 다시 시도하세요.");
  }
  if (error instanceof ApiError) throw error;
  throw error;
}

export function testTurnstile(config: Config, fetchFn?: typeof fetch): TurnstileVerifier {
  return createTurnstileVerifier(config, fetchFn);
}
