export type SqlValue = string | number | null;

export interface Statement {
  sql: string;
  params: SqlValue[];
}

export interface StatementResult {
  changes: number;
  rows: Record<string, unknown>[];
}

export interface Db {
  get<T>(sql: string, params?: SqlValue[]): Promise<T | undefined>;
  all<T>(sql: string, params?: SqlValue[]): Promise<T[]>;
  run(sql: string, params?: SqlValue[]): Promise<{ changes: number }>;
  batch(statements: Statement[]): Promise<StatementResult[]>;
}

const SECRET_KEY = /token|password|secret|authorization|activation|recovery|signature|publickey|pepper|key/i;

export function redact(fields: Record<string, unknown>): Record<string, unknown> {
  const output: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(fields)) {
    output[key] = SECRET_KEY.test(key) ? "[redacted]" : value;
  }
  return output;
}

export function logInfo(event: string, fields: Record<string, unknown> = {}): void {
  console.log(JSON.stringify({ level: "info", event, ...redact(fields) }));
}

export function logError(event: string, fields: Record<string, unknown> = {}): void {
  console.error(JSON.stringify({ level: "error", event, ...redact(fields) }));
}
