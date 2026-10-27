import { describe, expect, it } from "vitest";
import { bytesToBase64, utf8 } from "../../src/NIVEX.Api/src/crypto.ts";
import { isDeviceId } from "../../src/NIVEX.Api/src/ids.ts";
import { createHarness, readError, testConfig } from "./harness.ts";
import { handleRequest } from "../../src/NIVEX.Api/src/app.ts";
import { createTurnstileVerifier } from "../../src/NIVEX.Api/src/turnstile.ts";

async function sign(privateKey: CryptoKey, nonce: string): Promise<string> {
  const signature = await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, privateKey, utf8(nonce));
  return bytesToBase64(new Uint8Array(signature));
}

async function keyPair(): Promise<{ publicKey: string; privateKey: CryptoKey }> {
  const pair = await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"]);
  return {
    publicKey: bytesToBase64(new Uint8Array(await crypto.subtle.exportKey("spki", pair.publicKey))),
    privateKey: pair.privateKey,
  };
}

describe("NIVEX API", () => {
  it("creates one persisted device and replays the same idempotency key", async () => {
    const api = await createHarness();
    const verification = await api.request("/api/v1/verification/turnstile", {
      method: "POST",
      body: JSON.stringify({ token: "dev-verification", action: "device_create" }),
    });
    expect(verification.status).toBe(201);
    const { verificationId } = (await verification.json()) as { verificationId: string };
    const first = await api.request("/api/v1/devices", {
      method: "POST",
      headers: { "idempotency-key": "create-device-key-1" },
      body: JSON.stringify({ verificationId }),
    });
    expect(first.status).toBe(201);
    const created = (await first.json()) as { deviceId: string; activationCode: string; recoveryCode: string };
    expect(isDeviceId(created.deviceId)).toBe(true);
    const replay = await api.request("/api/v1/devices", {
      method: "POST",
      headers: { "idempotency-key": "create-device-key-1" },
      body: JSON.stringify({ verificationId }),
    });
    expect(replay.status).toBe(201);
    expect(await replay.json()).toEqual(created);
    const rows = await api.db.all("SELECT public_id FROM devices");
    expect(rows).toHaveLength(1);

    const conflict = await api.request("/api/v1/devices", {
      method: "POST",
      headers: { "idempotency-key": "create-device-key-1" },
      body: JSON.stringify({ verificationId: crypto.randomUUID() }),
    });
    expect(conflict.status).toBe(409);
    expect((await readError(conflict)).code).toBe("idempotency_conflict");
  });

  it("rejects a missing or reused verification and a second parallel creation", async () => {
    const api = await createHarness();
    const missing = await api.request("/api/v1/devices", {
      method: "POST",
      headers: { "idempotency-key": "missing-verification1" },
      body: JSON.stringify({ verificationId: crypto.randomUUID() }),
    });
    expect(missing.status).toBe(400);

    const issued = await api.request("/api/v1/verification/turnstile", {
      method: "POST",
      body: JSON.stringify({ token: "dev-verification", action: "device_create" }),
    });
    const { verificationId } = (await issued.json()) as { verificationId: string };
    const [left, right] = await Promise.all([
      api.request("/api/v1/devices", {
        method: "POST",
        headers: { "idempotency-key": "parallel-create-left1" },
        body: JSON.stringify({ verificationId }),
      }),
      api.request("/api/v1/devices", {
        method: "POST",
        headers: { "idempotency-key": "parallel-create-right" },
        body: JSON.stringify({ verificationId }),
      }),
    ]);
    const statuses = [left.status, right.status].sort();
    expect(statuses).toEqual([201, 400]);
    expect(await api.db.all("SELECT id FROM devices")).toHaveLength(1);

    const again = await api.request("/api/v1/verification/turnstile", {
      method: "POST",
      body: JSON.stringify({ token: "dev-verification", action: "device_create" }),
    });
    expect(again.status).toBe(403);
  });

  it("binds the first installation and rejects a copied device id", async () => {
    const api = await createHarness();
    const created = await issueDevice(api);
    const owner = await keyPair();
    const other = await keyPair();
    const unknown = await api.request("/api/v1/auth/challenge", {
      method: "POST",
      body: JSON.stringify({ deviceId: "NIV-AAAA-BBBB-CCCC-DDDD" }),
    });
    expect(await unknown.json()).toEqual({ status: "unknown" });
    const badFormat = await api.request("/api/v1/auth/challenge", {
      method: "POST",
      body: JSON.stringify({ deviceId: "device-1" }),
    });
    expect(badFormat.status).toBe(400);

    const unclaimed = await api.request("/api/v1/auth/challenge", {
      method: "POST",
      body: JSON.stringify({ deviceId: created.deviceId }),
    });
    expect(await unclaimed.json()).toEqual({ status: "unclaimed" });

    const wrong = await api.request("/api/v1/auth/activate", {
      method: "POST",
      body: JSON.stringify({
        deviceId: created.deviceId,
        activationCode: created.recoveryCode,
        publicKeySpki: owner.publicKey,
        installationId: crypto.randomUUID(),
      }),
    });
    expect(wrong.status).toBe(403);

    const activated = await api.request("/api/v1/auth/activate", {
      method: "POST",
      body: JSON.stringify({
        deviceId: created.deviceId,
        activationCode: created.activationCode,
        publicKeySpki: owner.publicKey,
        installationId: crypto.randomUUID(),
      }),
    });
    expect(activated.status).toBe(201);
    const session = (await activated.json()) as { accessToken: string; refreshToken: string; deviceId: string };

    const takeover = await api.request("/api/v1/auth/activate", {
      method: "POST",
      body: JSON.stringify({
        deviceId: created.deviceId,
        activationCode: created.activationCode,
        publicKeySpki: other.publicKey,
        installationId: crypto.randomUUID(),
      }),
    });
    expect(takeover.status).toBe(409);
    expect((await readError(takeover)).code).toBe("device_already_claimed");

    const challenge = await api.request("/api/v1/auth/challenge", {
      method: "POST",
      body: JSON.stringify({ deviceId: created.deviceId }),
    });
    const { nonce } = (await challenge.json()) as { nonce: string };
    const forged = await api.request("/api/v1/auth/prove", {
      method: "POST",
      body: JSON.stringify({ deviceId: created.deviceId, nonce, signature: await sign(other.privateKey, nonce) }),
    });
    expect(forged.status).toBe(401);

    const fresh = await api.request("/api/v1/auth/challenge", {
      method: "POST",
      body: JSON.stringify({ deviceId: created.deviceId }),
    });
    const proof = (await fresh.json()) as { nonce: string };
    const proved = await api.request("/api/v1/auth/prove", {
      method: "POST",
      body: JSON.stringify({ deviceId: created.deviceId, nonce: proof.nonce, signature: await sign(owner.privateKey, proof.nonce) }),
    });
    expect(proved.status).toBe(200);

    const me = await api.request("/api/v1/devices/me", { headers: { authorization: `Bearer ${session.accessToken}` } });
    const profile = (await me.json()) as { plan: string; deviceId: string; activationCode?: string };
    expect(me.status).toBe(200);
    expect(profile.deviceId).toBe(created.deviceId);
    expect(profile.plan).toBe("FREE");
    expect(profile.activationCode).toBeUndefined();

    const loggedOut = await api.request("/api/v1/auth/logout", {
      method: "POST",
      headers: { authorization: `Bearer ${session.accessToken}` },
      body: JSON.stringify({ refreshToken: session.refreshToken }),
    });
    expect(loggedOut.status).toBe(200);
    const refreshed = await api.request("/api/v1/auth/refresh", {
      method: "POST",
      body: JSON.stringify({ refreshToken: session.refreshToken }),
    });
    expect(refreshed.status).toBe(401);
  });

  it("rejects revoked devices and expired sessions", async () => {
    const api = await createHarness();
    const created = await issueDevice(api);
    const owner = await keyPair();
    const activated = await api.request("/api/v1/auth/activate", {
      method: "POST",
      body: JSON.stringify({
        deviceId: created.deviceId,
        activationCode: created.activationCode,
        publicKeySpki: owner.publicKey,
        installationId: crypto.randomUUID(),
      }),
    });
    const session = (await activated.json()) as { accessToken: string; refreshToken: string };
    await api.db.run("UPDATE devices SET status = 'revoked' WHERE public_id = ?", [created.deviceId]);
    const revoked = await api.request("/api/v1/devices/me", { headers: { authorization: `Bearer ${session.accessToken}` } });
    expect(revoked.status).toBe(403);
    const challenge = await api.request("/api/v1/auth/challenge", {
      method: "POST",
      body: JSON.stringify({ deviceId: created.deviceId }),
    });
    expect(await challenge.json()).toEqual({ status: "revoked" });

    await api.db.run("UPDATE devices SET status = 'active' WHERE public_id = ?", [created.deviceId]);
    await api.db.run("UPDATE sessions SET expires_at = ?", ["2020-01-01T00:00:00.000Z"]);
    const expired = await api.request("/api/v1/auth/refresh", {
      method: "POST",
      body: JSON.stringify({ refreshToken: session.refreshToken }),
    });
    expect(expired.status).toBe(401);
    const me = await api.request("/api/v1/devices/me", { headers: { authorization: `Bearer ${session.accessToken}` } });
    expect(me.status).toBe(401);
  });

  it("keeps admin actions and PRO entitlement on the server", async () => {
    const api = await createHarness({ accessTokenSeconds: 60 * 60 * 24 * 90, refreshTokenSeconds: 60 * 60 * 24 * 90 });
    const anonymous = await api.request("/api/v1/admin/devices?deviceId=NIV-AAAA-BBBB-CCCC-DDDD");
    expect(anonymous.status).toBe(401);
    const created = await issueDevice(api);
    const bootstrap = await api.request("/api/v1/admin/bootstrap", {
      method: "POST",
      headers: { "x-setup-token": "test-setup-token" },
      body: JSON.stringify({ email: "owner@nivex.example", password: "correct-horse-battery" }),
    });
    expect(bootstrap.status).toBe(201);
    const login = await api.request("/api/v1/admin/login", {
      method: "POST",
      body: JSON.stringify({ email: "owner@nivex.example", password: "correct-horse-battery" }),
    });
    expect(login.status).toBe(200);
    const { token } = (await login.json()) as { token: string };
    const granted = await api.request("/api/v1/admin/entitlements", {
      method: "POST",
      headers: { authorization: `Bearer ${token}` },
      body: JSON.stringify({ deviceId: created.deviceId, plan: "PRO", action: "grant", expiresAt: "2026-03-01T00:00:00.000Z" }),
    });
    expect(granted.status).toBe(200);
    const owner = await keyPair();
    const activated = await api.request("/api/v1/auth/activate", {
      method: "POST",
      body: JSON.stringify({
        deviceId: created.deviceId,
        activationCode: created.activationCode,
        publicKeySpki: owner.publicKey,
        installationId: crypto.randomUUID(),
      }),
    });
    const session = (await activated.json()) as { accessToken: string };
    const mine = await api.request("/api/v1/entitlements/me", { headers: { authorization: `Bearer ${session.accessToken}` } });
    expect(await mine.json()).toMatchObject({ plan: "PRO", pro: true });
    const selfGrant = await api.request("/api/v1/entitlements/me", {
      method: "POST",
      headers: { authorization: `Bearer ${session.accessToken}` },
      body: JSON.stringify({ plan: "PRO" }),
    });
    expect(selfGrant.status).toBe(404);

    api.now = new Date("2026-04-01T00:00:00.000Z");
    const expired = await api.request("/api/v1/entitlements/me", { headers: { authorization: `Bearer ${session.accessToken}` } });
    expect((await expired.json()) as { plan: string }).toMatchObject({ plan: "FREE", pro: false });
    const relogin = await api.request("/api/v1/admin/login", {
      method: "POST",
      body: JSON.stringify({ email: "owner@nivex.example", password: "correct-horse-battery" }),
    });
    const again = (await relogin.json()) as { token: string };

    const revoked = await api.request("/api/v1/admin/devices/revoke", {
      method: "POST",
      headers: { authorization: `Bearer ${again.token}` },
      body: JSON.stringify({ deviceId: created.deviceId }),
    });
    expect(revoked.status).toBe(200);
    const after = await api.request("/api/v1/devices/me", { headers: { authorization: `Bearer ${session.accessToken}` } });
    expect(after.status).toBe(403);
    const audit = await api.request("/api/v1/admin/audit", { headers: { authorization: `Bearer ${again.token}` } });
    expect(audit.status).toBe(200);
  });

  it("fails closed when production verification is not configured", async () => {
    const config = testConfig({
      turnstileMode: "production",
      allowDevVerification: false,
      expectedHostname: "",
      turnstileSecret: "",
    });
    const api = await createHarness(config);
    api.deps.turnstile = createTurnstileVerifier(config);
    api.deps.config = config;
    const response = await handleRequest(
      new Request("http://nivex.example/api/v1/verification/turnstile", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ token: "dev-verification", action: "device_create" }),
      }),
      api.deps,
    );
    expect(response.status).toBe(503);
  });
});

async function issueDevice(api: Awaited<ReturnType<typeof createHarness>>): Promise<{
  deviceId: string;
  activationCode: string;
  recoveryCode: string;
}> {
  const verification = await api.request("/api/v1/verification/turnstile", {
    method: "POST",
    body: JSON.stringify({ token: "dev-verification", action: "device_create" }),
  });
  const { verificationId } = (await verification.json()) as { verificationId: string };
  const response = await api.request("/api/v1/devices", {
    method: "POST",
    headers: { "idempotency-key": crypto.randomUUID().replaceAll("-", "") },
    body: JSON.stringify({ verificationId }),
  });
  expect(response.status).toBe(201);
  return (await response.json()) as { deviceId: string; activationCode: string; recoveryCode: string };
}
