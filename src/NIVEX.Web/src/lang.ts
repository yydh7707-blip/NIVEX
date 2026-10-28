export type Lang = "ko" | "en";

const KEY = "nivex.lang";

export function getLang(): Lang {
  try {
    const stored = localStorage.getItem(KEY);
    if (stored === "ko" || stored === "en") return stored;
  } catch {
    // Storage can be blocked; fall back to the browser language.
  }
  return navigator.language.toLowerCase().startsWith("ko") ? "ko" : "en";
}

export function setLang(lang: Lang): void {
  try {
    localStorage.setItem(KEY, lang);
  } catch {
    // Preference simply is not persisted.
  }
}
