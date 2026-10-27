import type { Config } from "./config.js";
import { ApiError } from "./errors.js";

export async function readJsonObject(request: Request): Promise<Record<string, unknown>> {
  const advertised = request.headers.get("content-length");
  if (advertised && Number(advertised) > 8192) {
    throw new ApiError(413, "payload_too_large", "요청이 너무 큽니다.");
  }
  const text = await request.text();
  if (text.length > 8192) throw new ApiError(413, "payload_too_large", "요청이 너무 큽니다.");
  if (!text.trim()) return {};
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    throw new ApiError(400, "invalid_json", "JSON 형식이 아닙니다.");
  }
  if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
    throw new ApiError(400, "invalid_json", "JSON 객체가 필요합니다.");
  }
  return parsed as Record<string, unknown>;
}

export function textField(body: Record<string, unknown>, key: string): string {
  const value = body[key];
  return typeof value === "string" ? value.trim() : "";
}

export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json; charset=utf-8" },
  });
}

export function errorBody(error: ApiError): Response {
  return json({ error: { code: error.code, message: error.message } }, error.status);
}

export function requestHost(request: Request): string {
  return new URL(request.url).hostname.toLowerCase();
}

export function idempotencyKey(request: Request): string {
  const key = request.headers.get("idempotency-key")?.trim() ?? "";
  if (!/^[A-Za-z0-9_-]{16,80}$/.test(key)) {
    throw new ApiError(400, "invalid_request", "Idempotency-Key 헤더가 필요합니다.");
  }
  return key;
}

const SECURITY_HEADERS: [string, string][] = [
  ["x-content-type-options", "nosniff"],
  ["referrer-policy", "no-referrer"],
  ["x-frame-options", "DENY"],
  ["permissions-policy", "camera=(), microphone=(), geolocation=()"],
];

export const CONTENT_SECURITY_POLICY = [
  "default-src 'self'",
  "script-src 'self' https://challenges.cloudflare.com",
  "frame-src https://challenges.cloudflare.com",
  "style-src 'self'",
  "img-src 'self' data:",
  "connect-src 'self' https://challenges.cloudflare.com",
  "base-uri 'self'",
  "form-action 'self'",
  "frame-ancestors 'none'",
].join("; ");

export function withHeaders(request: Request, response: Response, config: Config, html: boolean): Response {
  const headers = new Headers(response.headers);
  for (const [name, value] of SECURITY_HEADERS) headers.set(name, value);
  headers.set("cache-control", html ? "no-cache" : headers.get("cache-control") ?? "no-store");
  if (!html) headers.set("cache-control", "no-store");
  if (html) headers.set("content-security-policy", CONTENT_SECURITY_POLICY);
  const origin = request.headers.get("origin");
  const allowed = config.webOrigin.split(",").map((item) => item.trim()).filter((item) => item.length > 0);
  if (origin && allowed.includes(origin)) {
    headers.set("access-control-allow-origin", origin);
    headers.set("vary", "Origin");
    headers.set("access-control-allow-headers", "content-type, authorization, idempotency-key");
    headers.set("access-control-allow-methods", "GET, POST, OPTIONS");
  }
  return new Response(response.body, { status: response.status, statusText: response.statusText, headers });
}
