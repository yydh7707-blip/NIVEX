import type { Config } from "./config.js";
import { sha256Hex } from "./crypto.js";

export interface TurnstileCheck {
  ok: boolean;
  reason: string;
  hostname?: string;
}

export interface TurnstileVerifier {
  verify(token: string, requestHost: string, remoteIp: string, action: string): Promise<TurnstileCheck>;
}

interface SiteverifyResponse {
  success?: boolean;
  hostname?: string;
  action?: string;
  "error-codes"?: string[];
}

const LOCAL_HOSTS = new Set(["localhost", "127.0.0.1"]);

export function createTurnstileVerifier(config: Config, fetchFn: typeof fetch = fetch): TurnstileVerifier {
  return {
    async verify(token, requestHost, remoteIp, action) {
      const host = requestHost.toLowerCase();
      if (config.turnstileMode === "development" && config.allowDevVerification && LOCAL_HOSTS.has(host)) {
        if (token === "dev-verification") return { ok: true, reason: "development", hostname: host };
        return { ok: false, reason: "invalid_token" };
      }
      if (token === "dev-verification" || config.turnstileMode !== "production") {
        return { ok: false, reason: "verification_unavailable" };
      }
      if (!config.turnstileSecret || !config.expectedHostname) {
        return { ok: false, reason: "configuration" };
      }
      const body = new URLSearchParams({
        secret: config.turnstileSecret,
        response: token,
        remoteip: remoteIp,
      });
      let payload: SiteverifyResponse;
      try {
        const response = await fetchFn("https://challenges.cloudflare.com/turnstile/v0/siteverify", {
          method: "POST",
          headers: { "content-type": "application/x-www-form-urlencoded" },
          body,
        });
        payload = (await response.json()) as SiteverifyResponse;
      } catch {
        return { ok: false, reason: "upstream" };
      }
      if (!payload.success) return { ok: false, reason: "rejected" };
      if (payload.action !== action) return { ok: false, reason: "action" };
      if (payload.hostname?.toLowerCase() !== config.expectedHostname || host !== config.expectedHostname) {
        return { ok: false, reason: "hostname" };
      }
      return { ok: true, reason: "verified", hostname: payload.hostname };
    },
  };
}

export async function hashTurnstileToken(token: string): Promise<string> {
  return sha256Hex(`turnstile:${token}`);
}
