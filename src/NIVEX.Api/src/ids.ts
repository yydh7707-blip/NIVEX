import { bytesToBase64Url, randomBytes } from "./crypto.js";

export const DEVICE_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
const DEVICE_ID_PATTERN = /^NIV-[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{4}(?:-[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{4}){3}$/;
const CODE_PATTERN = /^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{4}(?:-[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{4}){7}$/;

export function isDeviceId(value: string): boolean {
  return DEVICE_ID_PATTERN.test(value);
}

export function isOwnerCode(value: string): boolean {
  return CODE_PATTERN.test(value);
}

export function normalizeDeviceId(value: string): string {
  return value.trim().toUpperCase();
}

function group(chars: string[], size: number): string {
  const parts: string[] = [];
  for (let index = 0; index < chars.length; index += size) parts.push(chars.slice(index, index + size).join(""));
  return parts.join("-");
}

/** Unbiased: alphabet length is 32, so each character consumes 5 bits. */
export function encodeBits(bytes: Uint8Array, charCount: number): string {
  let reservoir = 0;
  let bits = 0;
  const chars: string[] = [];
  for (const byte of bytes) {
    reservoir = (reservoir << 8) | byte;
    bits += 8;
    while (bits >= 5 && chars.length < charCount) {
      bits -= 5;
      chars.push(DEVICE_ALPHABET[(reservoir >> bits) & 31]!);
    }
  }
  if (chars.length !== charCount) throw new Error("insufficient_entropy");
  return chars.join("");
}

export function generateDeviceId(source: Uint8Array = randomBytes(10)): string {
  return `NIV-${group(encodeBits(source, 16).split(""), 4)}`;
}

export function generateOwnerCode(source: Uint8Array = randomBytes(20)): string {
  return group(encodeBits(source, 32).split(""), 4);
}

export function generateOpaqueToken(prefix: "nrt" | "nad"): string {
  return `${prefix}_${bytesToBase64Url(randomBytes(32))}`;
}

export type Plan = "FREE" | "PRO";

export interface EntitlementRecord {
  plan: Plan;
  status: "active" | "revoked";
  startsAt: string;
  expiresAt: string | null;
}

export function resolvePlan(records: EntitlementRecord[], now: Date): Plan | "NONE" {
  const instant = now.getTime();
  const usable = records.filter((record) => {
    if (record.status !== "active") return false;
    if (Date.parse(record.startsAt) > instant) return false;
    if (record.expiresAt && Date.parse(record.expiresAt) <= instant) return false;
    return true;
  });
  if (usable.some((record) => record.plan === "PRO")) return "PRO";
  if (usable.some((record) => record.plan === "FREE")) return "FREE";
  return "NONE";
}

export function hasPlan(records: EntitlementRecord[], required: Plan, now: Date): boolean {
  const current = resolvePlan(records, now);
  if (required === "FREE") return current === "FREE" || current === "PRO";
  return current === "PRO";
}
