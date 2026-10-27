const configured = (import.meta as { env?: { VITE_API_BASE?: string } }).env?.VITE_API_BASE ?? "";
const base = configured.replace(/\/$/, "");

export function api(path: string): string {
  return `${base}${path}`;
}
