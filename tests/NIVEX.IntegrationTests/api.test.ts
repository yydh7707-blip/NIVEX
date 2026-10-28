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

  it("limits device creation to three per IP and requires the typed IP to match", async () => {
    const api = await createHarness();
    let serial = 0;
    const issue = async (ip: string, typed: string, key: string) => {
      const verificationId = crypto.randomUUID();
      serial += 1;
      await api.db.run(
        `INSERT INTO verifications (id, token_hash, action, hostname, expires_at, used_at, created_at)
         VALUES (?, ?, 'device_create', '127.0.0.1', ?, NULL, ?)`,
        [verificationId, `test-token-${serial}`, "2026-01-15T01:00:00.000Z", "2026-01-15T00:00:00.000Z"],
      );
      return api.request("/api/v1/devices", {
        method: "POST",
        ip,
        headers: { "idempotency-key": key },
        body: JSON.stringify({ verificationId, ip: typed }),
      });
    };

    const wrong = await issue("198.51.100.7", "198.51.100.8", "ip-wrong-key-000001");
    expect(wrong.status).toBe(403);
    expect((await readError(wrong)).code).toBe("ip_mismatch");
    const bad = await issue("198.51.100.7", "not-an-ip", "ip-bad-key-0000001");
    expect(bad.status).toBe(400);
    expect(await api.db.all("SELECT id FROM devices")).toHaveLength(0);

    const check = await api.request("/api/v1/ip/check", { method: "POST", ip: "198.51.100.7", body: JSON.stringify({ ip: "198.51.100.7" }) });
    expect(await check.json()).toEqual({ matched: true, limit: 3, used: 0, remaining: 3 });

    for (let i = 0; i < 3; i += 1) {
      const ok = await issue("198.51.100.7", "198.51.100.7", `ip-ok-key-00000${i}`);
      expect([ok.status, ok.status === 201 ? "" : await ok.text()]).toEqual([201, ""]);
    }
    const fourth = await issue("198.51.100.7", "198.51.100.7", "ip-ok-key-000009");
    expect(fourth.status).toBe(403);
    expect((await readError(fourth)).code).toBe("ip_limit");
    expect(await api.db.all("SELECT id FROM devices")).toHaveLength(3);

    const other = await issue("198.51.100.99", "198.51.100.99", "ip-other-key-00001");
    expect(other.status).toBe(201);

    const after = await api.request("/api/v1/ip/check", { method: "POST", ip: "198.51.100.7", body: JSON.stringify({ ip: "198.51.100.7" }) });
    expect(await after.json()).toEqual({ matched: true, limit: 3, used: 3, remaining: 0 });

    const lookup = await api.request("/api/v1/ip/devices", { method: "POST", ip: "198.51.100.7", body: JSON.stringify({ ip: "198.51.100.7" }) });
    const listed = (await lookup.json()) as { devices: { deviceId: string; status: string; activationCode: string | null }[] };
    expect(listed.devices).toHaveLength(3);
    expect(listed.devices.every((d) => d.status === "unclaimed" && /^[A-Z0-9-]+$/.test(d.activationCode ?? ""))).toBe(true);
    const ids = (await api.db.all<{ public_id: string }>("SELECT public_id FROM devices")).map((r) => r.public_id);
    expect(listed.devices.every((d) => ids.includes(d.deviceId))).toBe(true);

    const foreign = await api.request("/api/v1/ip/devices", { method: "POST", ip: "198.51.100.50", body: JSON.stringify({ ip: "198.51.100.50" }) });
    expect(((await foreign.json()) as { devices: unknown[] }).devices).toHaveLength(0);
    const mismatch = await api.request("/api/v1/ip/devices", { method: "POST", ip: "198.51.100.50", body: JSON.stringify({ ip: "198.51.100.7" }) });
    expect(mismatch.status).toBe(403);

    await api.db.run("UPDATE devices SET status = 'active', activation_code_hash = NULL WHERE public_id = ?", [listed.devices[0]!.deviceId]);
    const again = await api.request("/api/v1/ip/devices", { method: "POST", ip: "198.51.100.7", body: JSON.stringify({ ip: "198.51.100.7" }) });
    const second = (await again.json()) as { devices: { status: string; activationCode: string | null }[] };
    expect(second.devices.find((d) => d.status === "active")?.activationCode).toBeNull();
  });

  it("counts an IPv6 /64 as one address and ignores formatting differences", async () => {
    const api = await createHarness();
    const check = await api.request("/api/v1/ip/check", {
      method: "POST",
      ip: "2001:db8:0:0:0:0:0:1",
      body: JSON.stringify({ ip: "[2001:DB8::1]" }),
    });
    expect(check.status).toBe(200);
    const sameNet = await api.request("/api/v1/ip/check", {
      method: "POST",
      ip: "2001:db8::2",
      body: JSON.stringify({ ip: "2001:db8::2" }),
    });
    expect(sameNet.status).toBe(200);
  });

  it("rejects a missing IP when creating a device", async () => {
    const api = await createHarness();
    const verification = await api.request("/api/v1/verification/turnstile", {
      method: "POST",
      body: JSON.stringify({ token: "dev-verification", action: "device_create" }),
    });
    const { verificationId } = (await verification.json()) as { verificationId: string };
    const response = await api.request("/api/v1/devices", {
      method: "POST",
      headers: { "idempotency-key": "ip-missing-key-00001" },
      body: JSON.stringify({ verificationId, ip: "" }),
    });
    expect(response.status).toBe(400);
    expect((await readError(response)).code).toBe("ip_invalid");
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
