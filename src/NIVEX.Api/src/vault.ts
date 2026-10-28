import { base64ToBytes, bytesToBase64, randomBytes, sha256Bytes, utf8 } from "./crypto.js";

// Reversible storage for a short-lived activation code, so its owner can look it up again
// until the installation is activated. Recovery codes are never stored this way.
const buf = (bytes: Uint8Array): Uint8Array<ArrayBuffer> => new Uint8Array(bytes);

async function key(secret: string): Promise<CryptoKey> {
  const raw = await sha256Bytes(`${secret}|activation-vault`);
  return crypto.subtle.importKey("raw", buf(raw), "AES-GCM", false, ["encrypt", "decrypt"]);
}

export async function sealCode(secret: string, aad: string, code: string): Promise<string> {
  const iv = buf(randomBytes(12));
  const sealed = new Uint8Array(
    await crypto.subtle.encrypt({ name: "AES-GCM", iv, additionalData: buf(utf8(aad)) }, await key(secret), buf(utf8(code))),
  );
  return `${bytesToBase64(iv)}.${bytesToBase64(sealed)}`;
}

export async function openCode(secret: string, aad: string, value: string | null): Promise<string | null> {
  if (!value) return null;
  const [iv, sealed] = value.split(".");
  if (!iv || !sealed) return null;
  try {
    const plain = await crypto.subtle.decrypt(
      { name: "AES-GCM", iv: buf(base64ToBytes(iv)), additionalData: buf(utf8(aad)) },
      await key(secret),
      buf(base64ToBytes(sealed)),
    );
    return new TextDecoder().decode(plain);
  } catch {
    return null;
  }
}
