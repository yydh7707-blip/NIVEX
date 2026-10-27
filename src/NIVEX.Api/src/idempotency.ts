import type { Db, Statement } from "./db.js";
import { ApiError } from "./errors.js";

interface IdempotencyRow {
  request_hash: string;
  state: string;
  status_code: number | null;
  response_body: string | null;
  created_at: string;
}

const STALE_PENDING_MS = 30_000;

export async function withIdempotency(
  db: Db,
  now: Date,
  key: string,
  requestHash: string,
  build: () => Promise<{ status: number; body: unknown; statements: Statement[] }>,
  attempt = 0,
): Promise<{ status: number; body: unknown }> {
  const existing = await db.get<IdempotencyRow>(
    "SELECT request_hash, state, status_code, response_body, created_at FROM idempotency_records WHERE idempotency_key = ?",
    [key],
  );
  if (existing) return replay(db, now, key, requestHash, existing, build, attempt);

  try {
    await db.run(
      `INSERT INTO idempotency_records (idempotency_key, request_hash, state, status_code, response_body, created_at)
       VALUES (?, ?, 'pending', NULL, NULL, ?)`,
      [key, requestHash, now.toISOString()],
    );
  } catch (error) {
    if (!uniqueConflict(error)) throw error;
    const raced = await db.get<IdempotencyRow>(
      "SELECT request_hash, state, status_code, response_body, created_at FROM idempotency_records WHERE idempotency_key = ?",
      [key],
    );
    if (!raced) throw error;
    return replay(db, now, key, requestHash, raced, build, attempt);
  }

  try {
    const outcome = await build();
    await db.batch([
      ...outcome.statements,
      {
        sql: `UPDATE idempotency_records
              SET state = 'complete', status_code = ?, response_body = ?
              WHERE idempotency_key = ? AND state = 'pending'`,
        params: [outcome.status, JSON.stringify(outcome.body), key],
      },
    ]);
    return { status: outcome.status, body: outcome.body };
  } catch (error) {
    await db.run("DELETE FROM idempotency_records WHERE idempotency_key = ? AND state = 'pending'", [key]);
    throw error;
  }
}

async function replay(
  db: Db,
  now: Date,
  key: string,
  requestHash: string,
  row: IdempotencyRow,
  build: () => Promise<{ status: number; body: unknown; statements: Statement[] }>,
  attempt: number,
): Promise<{ status: number; body: unknown }> {
  if (row.request_hash !== requestHash) {
    throw new ApiError(409, "idempotency_conflict", "같은 Idempotency-Key로 다른 요청을 보낼 수 없습니다.");
  }
  if (row.state === "complete" && row.response_body && row.status_code) {
    return { status: Number(row.status_code), body: JSON.parse(row.response_body) as unknown };
  }
  const age = now.getTime() - Date.parse(row.created_at);
  if (age < STALE_PENDING_MS || attempt > 0) {
    throw new ApiError(409, "request_in_progress", "같은 요청이 처리 중입니다. 잠시 후 다시 시도하세요.");
  }
  await db.run("DELETE FROM idempotency_records WHERE idempotency_key = ? AND state = 'pending'", [key]);
  return withIdempotency(db, now, key, requestHash, build, attempt + 1);
}

export function uniqueConflict(error: unknown): boolean {
  return error instanceof Error && /unique/i.test(error.message);
}

export function constraintMessage(error: unknown): string {
  return error instanceof Error ? error.message : "";
}
