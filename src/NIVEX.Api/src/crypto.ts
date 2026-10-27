const textEncoder = new TextEncoder();

export function utf8(value: string): Uint8Array {
  return textEncoder.encode(value);
}

function asBuffer(bytes: Uint8Array): ArrayBuffer {
  return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
}

export function bytesToBase64Url(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/g, "");
}

export function base64UrlToBytes(value: string): Uint8Array {
  const padded = value.replaceAll("-", "+").replaceAll("_", "/") + "=".repeat((4 - (value.length % 4)) % 4);
  const binary = atob(padded);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
  return bytes;
}

export function base64ToBytes(value: string): Uint8Array {
  const binary = atob(value);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
  return bytes;
}

export function bytesToBase64(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}

export async function sha256Hex(value: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", asBuffer(utf8(value)));
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}

export async function sha256Bytes(value: string): Promise<Uint8Array> {
  return new Uint8Array(await crypto.subtle.digest("SHA-256", asBuffer(utf8(value))));
}

export async function safeEqual(left: string, right: string): Promise<boolean> {
  const [a, b] = await Promise.all([sha256Bytes(left), sha256Bytes(right)]);
  let diff = 0;
  for (let index = 0; index < a.length; index += 1) diff |= a[index]! ^ b[index]!;
  return diff === 0;
}

export function randomBytes(size: number): Uint8Array {
  const bytes = new Uint8Array(size);
  crypto.getRandomValues(bytes);
  return bytes;
}

export async function hmacSha256(secret: string, value: string): Promise<Uint8Array> {
  const key = await crypto.subtle.importKey("raw", asBuffer(utf8(secret)), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return new Uint8Array(await crypto.subtle.sign("HMAC", key, asBuffer(utf8(value))));
}

export interface AccessClaims {
  sub: string;
  sid: string;
  iat: number;
  exp: number;
  typ: "access";
}

export async function signAccessToken(secret: string, claims: AccessClaims): Promise<string> {
  const header = bytesToBase64Url(utf8(JSON.stringify({ alg: "HS256", typ: "JWT" })));
  const payload = bytesToBase64Url(utf8(JSON.stringify(claims)));
  const signature = bytesToBase64Url(await hmacSha256(secret, `${header}.${payload}`));
  return `${header}.${payload}.${signature}`;
}

export async function verifyAccessToken(secret: string, token: string, nowSeconds: number): Promise<AccessClaims | undefined> {
  const parts = token.split(".");
  if (parts.length !== 3) return undefined;
  const [header, payload, signature] = parts;
  if (!header || !payload || !signature) return undefined;
  const expected = bytesToBase64Url(await hmacSha256(secret, `${header}.${payload}`));
  if (!(await safeEqual(signature, expected))) return undefined;
  try {
    const claims = JSON.parse(new TextDecoder().decode(base64UrlToBytes(payload))) as Partial<AccessClaims>;
    if (claims.typ !== "access" || typeof claims.sub !== "string" || typeof claims.sid !== "string") return undefined;
    if (typeof claims.exp !== "number" || claims.exp < nowSeconds - 30) return undefined;
    if (typeof claims.iat !== "number") return undefined;
    return { sub: claims.sub, sid: claims.sid, iat: claims.iat, exp: claims.exp, typ: "access" };
  } catch {
    return undefined;
  }
}

export async function hashSecret(pepper: string, purpose: string, value: string): Promise<string> {
  return sha256Hex(`${pepper}:${purpose}:${value}`);
}

export async function pbkdf2(password: string, salt: Uint8Array, iterations: number): Promise<string> {
  const material = await crypto.subtle.importKey("raw", asBuffer(utf8(password)), "PBKDF2", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits(
    { name: "PBKDF2", salt: asBuffer(salt), iterations, hash: "SHA-256" },
    material,
    256,
  );
  return bytesToBase64(new Uint8Array(bits));
}

export async function importVerifyKey(spkiBase64: string): Promise<CryptoKey> {
  return crypto.subtle.importKey("spki", asBuffer(base64ToBytes(spkiBase64)), { name: "ECDSA", namedCurve: "P-256" }, false, ["verify"]);
}

export async function verifySignature(spkiBase64: string, payload: string, signatureBase64: string): Promise<boolean> {
  const key = await importVerifyKey(spkiBase64);
  const signature = base64ToBytes(signatureBase64);
  if (signature.length !== 64) return false;
  return crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" }, key, asBuffer(signature), asBuffer(utf8(payload)));
}
