import { DatabaseSync } from "node:sqlite";
import type { Db, SqlValue, Statement, StatementResult } from "./db.js";

export function createSqliteDb(filename = ":memory:"): Db {
  const database = new DatabaseSync(filename);
  database.exec("PRAGMA foreign_keys = ON");
  let queue: Promise<unknown> = Promise.resolve();

  const enqueue = <T>(work: () => T): Promise<T> => {
    const run = queue.then(() => work());
    queue = run.then(
      () => undefined,
      () => undefined,
    );
    return run;
  };

  const read = (sql: string, params: SqlValue[] = []): Record<string, unknown>[] => {
    return database.prepare(sql).all(...params) as Record<string, unknown>[];
  };

  const write = (sql: string, params: SqlValue[] = []): number => {
    const result = database.prepare(sql).run(...params);
    return Number(result.changes ?? 0);
  };

  return {
    get: <T>(sql: string, params?: SqlValue[]) => enqueue(() => read(sql, params)[0] as T | undefined),
    all: <T>(sql: string, params?: SqlValue[]) => enqueue(() => read(sql, params) as T[]),
    run: (sql, params) => enqueue(() => ({ changes: write(sql, params) })),
    batch: (statements) =>
      enqueue(() => {
        database.exec("BEGIN IMMEDIATE");
        try {
          const results: StatementResult[] = statements.map((statement) => execute(statement));
          database.exec("COMMIT");
          return results;
        } catch (error) {
          database.exec("ROLLBACK");
          throw error;
        }
      }),
  };

  function execute(statement: Statement): StatementResult {
    if (/^\s*(select|with)\b/i.test(statement.sql)) {
      return { changes: 0, rows: read(statement.sql, statement.params) };
    }
    return { changes: write(statement.sql, statement.params), rows: [] };
  }
}
