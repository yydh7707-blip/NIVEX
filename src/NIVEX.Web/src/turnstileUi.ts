import { copy } from "./i18n.ts";
import { api } from "./api.ts";

interface PublicConfig {
  turnstileSiteKey: string;
  devVerification: boolean;
  offline?: boolean;
}

interface TurnstileApi {
  render(element: HTMLElement, options: Record<string, unknown>): string;
}

declare global {
  interface Window {
    turnstile?: TurnstileApi;
  }
}

function loadTurnstile(): Promise<void> {
  if (window.turnstile) return Promise.resolve();
  return new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit";
    script.async = true;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error("turnstile"));
    document.head.append(script);
  });
}

export async function mountTurnstile(
  widget: HTMLElement,
  status: HTMLElement,
  action: string,
  config: PublicConfig,
  onOk: (verificationId: string) => void,
): Promise<void> {
  const finish = async (token: string) => {
    status.replaceChildren(el("span", "spinner"), document.createTextNode(copy.verifying));
    status.className = "status";
    const response = await fetch(api("/api/v1/verification/turnstile"), {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ token, action }),
    });
    const body = await response.json() as { verificationId?: string; error?: { message: string } };
    if (!response.ok || !body.verificationId) {
      status.textContent = body.error?.message ?? copy.failed;
      status.className = "status error is-shake";
      return;
    }
    onOk(body.verificationId);
  };

  if (config.devVerification && !config.turnstileSiteKey) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "btn ghost";
    button.textContent = copy.devVerify;
    widget.append(button, Object.assign(document.createElement("p"), { className: "meta", textContent: copy.devNote }));
    button.addEventListener("click", () => void finish("dev-verification"));
    return;
  }
  if (!config.turnstileSiteKey) {
    status.textContent = copy.apiDown;
    status.className = "status error";
    return;
  }
  await loadTurnstile();
  window.turnstile?.render(widget, {
    sitekey: config.turnstileSiteKey,
    action,
    callback: (token: string) => void finish(token),
    "error-callback": () => {
      status.textContent = copy.failed;
      status.className = "status error is-shake";
    },
  });
}

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className?: string, text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}
