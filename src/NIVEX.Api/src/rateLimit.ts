import type { Config } from "./config.js";
import { sha256Hex } from "./crypto.js";
import type { Db } from "./db.js";
import { ApiError } from "./errors.js";

export function clientIp(request: Request): string {
  return request.headers.get("cf-connecting-ip") ?? "local";
}

export async function enforceRateLimit(
  db: Db,
  config: Config,
  request: Request,
  scope: string,
  limit: number,
  windowSeconds: number,
  now: Date,
): Promise<void> {
  const bucket = await sha256Hex(`${config.ipHashPepper}|${scope}|${clientIp(request)}`);
  const nowSeconds = Math.floor(now.getTime() / 1000);
  const windowStart = nowSeconds - (nowSeconds % windowSeconds);
  await db.run(
    `INSERT INTO rate_limits (bucket_key, window_start, count)
     VALUES (?, ?, 1)
     ON CONFLICT(bucket_key, window_start) DO UPDATE SET count = count + 1`,
    [bucket, windowStart],
  );
  const row = await db.get<{ count: number }>(
    "SELECT count FROM rate_limits WHERE bucket_key = ? AND window_start = ?",
    [bucket, windowStart],
  );
  await db.run("DELETE FROM rate_limits WHERE window_start < ?", [windowStart - 172_800]);
  if (Number(row?.count ?? 0) > limit) {
    await db.run("INSERT INTO abuse_events (id, scope, reason, created_at) VALUES (?, ?, 'rate_limited', ?)", [
      crypto.randomUUID(),
      scope,
      now.toISOString(),
    ]);
    throw new ApiError(429, "rate_limited", "요청이 너무 많습니다. 잠시 후 다시 시도하세요.");
  }
}
