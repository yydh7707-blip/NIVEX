import type { D1Like } from "./config.js";
import type { Db, SqlValue, StatementResult } from "./db.js";

export function createD1Db(d1: D1Like): Db {
  const bind = (sql: string, params: SqlValue[] = []) => {
    const statement = d1.prepare(sql);
    return params.length > 0 ? statement.bind(...params) : statement;
  };

  return {
    async get<T>(sql: string, params: SqlValue[] = []) {
      const row = await bind(sql, params).first<T>();
      return row ?? undefined;
    },
    async all<T>(sql: string, params: SqlValue[] = []) {
      const result = await bind(sql, params).all<T>();
      return result.results ?? [];
    },
    async run(sql: string, params: SqlValue[] = []) {
      const result = await bind(sql, params).run();
      return { changes: result.meta?.changes ?? 0 };
    },
    async batch(statements) {
      if (statements.length === 0) return [];
      const prepared = statements.map((statement) => bind(statement.sql, statement.params));
      const results = await d1.batch(prepared);
      return results.map((result): StatementResult => ({
        changes: result.meta?.changes ?? 0,
        rows: (result.results ?? []) as Record<string, unknown>[],
      }));
    },
  };
}
