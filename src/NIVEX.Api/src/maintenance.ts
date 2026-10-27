import type { Db } from "./db.js";

export async function purgeExpired(db: Db, now: Date): Promise<void> {
  const instant = now.toISOString();
  const eventsBefore = new Date(now.getTime() - 180 * 24 * 60 * 60 * 1000).toISOString();
  const abuseBefore = new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000).toISOString();
  const dayAgo = new Date(now.getTime() - 24 * 60 * 60 * 1000).toISOString();
  await db.batch([
    { sql: "DELETE FROM auth_events WHERE created_at < ?", params: [eventsBefore] },
    { sql: "DELETE FROM abuse_events WHERE created_at < ?", params: [abuseBefore] },
    { sql: "DELETE FROM challenges WHERE expires_at < ?", params: [instant] },
    { sql: "DELETE FROM verifications WHERE expires_at < ? AND id NOT IN (SELECT verification_id FROM verification_redemptions)", params: [instant] },
    { sql: "DELETE FROM idempotency_records WHERE created_at < ?", params: [dayAgo] },
    { sql: "DELETE FROM admin_sessions WHERE expires_at < ? OR revoked_at IS NOT NULL AND revoked_at < ?", params: [instant, dayAgo] },
    { sql: "DELETE FROM rate_limits WHERE window_start < ?", params: [Math.floor(now.getTime() / 1000) - 172_800] },
  ]);
}
