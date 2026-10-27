import { describe, expect, it } from "vitest";
import { verifyAccessToken, signAccessToken, bytesToBase64, utf8 } from "../../src/NIVEX.Api/src/crypto.ts";
import { generateDeviceId, hasPlan, isDeviceId, resolvePlan } from "../../src/NIVEX.Api/src/ids.ts";
import { reduceDeviceUi } from "../../src/NIVEX.Web/src/deviceFlow.ts";
import { createTurnstileVerifier } from "../../src/NIVEX.Api/src/turnstile.ts";
import { testConfig } from "../NIVEX.IntegrationTests/harness.ts";

describe("device id", () => {
  it("uses the public format and rejects predictable substitutes", () => {
    const id = generateDeviceId();
    expect(isDeviceId(id)).toBe(true);
    expect(id).toHaveLength("NIV-XXXX-XXXX-XXXX-XXXX".length);
    expect(isDeviceId("NIV-0000-0000-0000-0000")).toBe(false);
    expect(isDeviceId("user@example.com")).toBe(false);
    expect(isDeviceId("1")).toBe(false);
  });

  it("has 80 bits of formatted entropy", () => {
    const bytes = new Uint8Array(10);
    bytes.fill(0xff);
    expect(generateDeviceId(bytes)).toBe("NIV-9999-9999-9999-9999");
  });
});

describe("entitlements", () => {
  const now = new Date("2026-01-15T00:00:00.000Z");

  it("prefers an unexpired PRO grant and falls back to FREE", () => {
    expect(resolvePlan([{ plan: "FREE", status: "active", startsAt: now.toISOString(), expiresAt: null }], now)).toBe("FREE");
    expect(
      resolvePlan(
        [
          { plan: "FREE", status: "active", startsAt: now.toISOString(), expiresAt: null },
          { plan: "PRO", status: "active", startsAt: now.toISOString(), expiresAt: "2026-02-01T00:00:00.000Z" },
        ],
        now,
      ),
    ).toBe("PRO");
    expect(
      resolvePlan(
        [
          { plan: "FREE", status: "active", startsAt: now.toISOString(), expiresAt: null },
          { plan: "PRO", status: "active", startsAt: now.toISOString(), expiresAt: "2026-01-01T00:00:00.000Z" },
        ],
        now,
      ),
    ).toBe("FREE");
    expect(hasPlan([{ plan: "PRO", status: "revoked", startsAt: now.toISOString(), expiresAt: null }], "PRO", now)).toBe(false);
  });
});

describe("access tokens", () => {
  it("rejects tampered and expired tokens", async () => {
    const secret = "test-signing-key-must-be-32-characters";
    const token = await signAccessToken(secret, { sub: "NIV-ABCD-EFGH-JKMN-PQRS", sid: "session", iat: 100, exp: 200, typ: "access" });
    expect((await verifyAccessToken(secret, token, 150))?.sid).toBe("session");
    expect(await verifyAccessToken(secret, token, 400)).toBeUndefined();
    expect(await verifyAccessToken(secret, `${token}x`, 150)).toBeUndefined();
    expect(await verifyAccessToken("another-signing-key-must-be-32-chars", token, 150)).toBeUndefined();
  });
});

describe("turnstile", () => {
  it("does not honor the development token in production", async () => {
    let called = false;
    const verifier = createTurnstileVerifier(
      testConfig({ turnstileMode: "production", allowDevVerification: false, expectedHostname: "nivex.example", turnstileSecret: "real-secret" }),
      async () => {
        called = true;
        return new Response(JSON.stringify({ success: true, hostname: "nivex.example", action: "device_create" }));
      },
    );
    const dev = await verifier.verify("dev-verification", "nivex.example", "203.0.113.8", "device_create");
    expect(dev.ok).toBe(false);
    expect(called).toBe(false);
  });

  it("rejects a Cloudflare result for the wrong host or action", async () => {
    const verifier = createTurnstileVerifier(
      testConfig({ turnstileMode: "production", allowDevVerification: false, expectedHostname: "nivex.example", turnstileSecret: "real-secret" }),
      async () => new Response(JSON.stringify({ success: true, hostname: "evil.example", action: "device_create" })),
    );
    const result = await verifier.verify("token", "nivex.example", "203.0.113.8", "device_create");
    expect(result.ok).toBe(false);
  });

  it("accepts the development token only for localhost", async () => {
    const verifier = createTurnstileVerifier(testConfig());
    expect((await verifier.verify("dev-verification", "127.0.0.1", "", "device_create")).ok).toBe(true);
    expect((await verifier.verify("dev-verification", "evil.example", "", "device_create")).ok).toBe(false);
  });
});

describe("device creation ui state", () => {
  it("reaches created only after a real success event", () => {
    expect(reduceDeviceUi("idle", { type: "create_ok" })).toBe("idle");
    let phase = reduceDeviceUi("idle", { type: "submit_verification" });
    phase = reduceDeviceUi(phase, { type: "verification_ok" });
    phase = reduceDeviceUi(phase, { type: "submit_create" });
    expect(phase).toBe("creating");
    expect(reduceDeviceUi(phase, { type: "create_ok" })).toBe("created");
    expect(reduceDeviceUi("creating", { type: "create_fail", consumed: false })).toBe("verified");
    expect(reduceDeviceUi("creating", { type: "create_fail", consumed: true })).toBe("failed");
  });
});

describe("signature fixture", () => {
  it("exports a P-256 SPKI key that round-trips", async () => {
    const pair = await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"]);
    const spki = bytesToBase64(new Uint8Array(await crypto.subtle.exportKey("spki", pair.publicKey)));
    const signature = bytesToBase64(
      new Uint8Array(await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, pair.privateKey, utf8("nonce"))),
    );
    expect(spki.length).toBeGreaterThan(40);
    expect(signature).toMatch(/^[A-Za-z0-9+/=]+$/);
  });
});
