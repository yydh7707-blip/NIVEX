import { copy, type Copy } from "./i18n.ts";
import { mountDevicePage } from "./devicePage.ts";
import { enterPage } from "./motion.ts";
import { mountTurnstile } from "./turnstileUi.ts";
import { api } from "./api.ts";

interface PublicConfig {
  version: string;
  turnstileSiteKey: string;
  devVerification: boolean;
  downloadUrl: string;
  offline?: boolean;
}

const view = document.querySelector<HTMLElement>("#view")!;
const nav = document.querySelector<HTMLElement>(".nav")!;
const footer = document.querySelector<HTMLElement>(".foot")!;
const INSTALLER_PATH = "/downloads/NIVEX-Setup.exe";
let config: PublicConfig = { version: "0.1.0", turnstileSiteKey: "", devVerification: false, downloadUrl: INSTALLER_PATH, offline: true };

function t(): Copy {
  return copy;
}

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className?: string, text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

async function loadConfig(): Promise<void> {
  try {
    const response = await fetch(api("/api/v1/public/config"));
    if (!response.ok) return;
    const loaded = await response.json() as PublicConfig;
    config = { ...loaded, downloadUrl: loaded.downloadUrl || INSTALLER_PATH, offline: false };
  } catch {
    config = { ...config, offline: true };
  }
}

function paintChrome(): void {
  document.documentElement.lang = "en";
  nav.replaceChildren();
  for (const [href, label] of t().nav) {
    const link = el("a", undefined, label);
    link.href = href;
    link.dataset.link = "";
    if (location.pathname === href) link.setAttribute("aria-current", "page");
    nav.append(link);
  }
  const links = el("nav");
  for (const [href, label] of t().footer) {
    const link = el("a", undefined, label);
    link.href = href;
    link.dataset.link = "";
    links.append(link);
  }
  footer.replaceChildren(el("span", undefined, t().rights), links);
}

function section(title: string): HTMLElement {
  const block = el("section", "section");
  block.append(el("h2", undefined, title));
  return block;
}

function draftBanner(): HTMLElement {
  return el("p", "draft", t().draft);
}

function renderHome(): void {
  const hero = el("section", "hero");
  const copy = el("div", "hero-copy");
  const title = el("h1");
  t().homeTitle.split("\n").forEach((line, index) => {
    if (index > 0) title.append(document.createElement("br"));
    title.append(line);
  });
  const actions = el("div", "actions");
  const create = el("a", "btn primary", t().create);
  create.href = "/device";
  create.dataset.link = "";
  const download = el("a", "btn ghost", t().download);
  download.href = "/download";
  download.dataset.link = "";
  actions.append(create, download);
  copy.append(el("p", "kicker", t().homeKicker), title, el("p", "lede", t().homeLede), actions);
  const strip = el("div", "specimen-strip");
  strip.append(el("p", "meta", t().plateLabel), el("p", "specimen", "NIV-••••-••••-••••-••••"), el("p", "meta", t().plateNote));
  hero.append(copy, strip);

  const what = section(t().what);
  const list = el("ol", "cap-list");
  for (const [index, name, detail] of t().rows) {
    const item = el("li");
    const body = el("div");
    body.append(el("h3", undefined, name), el("p", undefined, detail));
    item.append(el("span", undefined, index), body);
    list.append(item);
  }
  what.append(list);

  const req = section(t().requirements);
  const reqList = el("ul", "list");
  for (const item of t().req) reqList.append(el("li", undefined, item));
  req.append(reqList, el("p", "meta", `${t().version} ${config.offline ? "—" : config.version}`));
  view.append(hero, what, req);
}

function renderDownload(): void {
  const block = section(t().downloadTitle);
  block.append(el("p", "lede", `${t().version} ${config.offline ? "—" : config.version}`));
  if (config.downloadUrl) {
    const link = el("a", "btn primary", t().downloadReady);
    link.href = config.downloadUrl;
    block.append(link);
  } else {
    block.append(el("p", "note", t().downloadMissing));
  }
  view.append(block);
}

function renderGuide(): void {
  const block = section(t().guideTitle);
  const list = el("ol", "list");
  for (const step of t().guide) list.append(el("li", undefined, step));
  block.append(list);
  view.append(block);
}

function renderReleases(): void {
  const block = section(t().releaseTitle);
  block.append(el("p", "kicker", config.offline ? "0.1.0" : config.version), el("p", undefined, t().releaseBody));
  view.append(block);
}

function renderPrivacy(): void {
  const block = section(t().privacyTitle);
  const lines = [
    "Collected: Device ID, hashes of the activation and recovery codes, the installation public key, a random installation ID, refresh-token hashes, authentication events, and a hashed IP used only for rate limits.",
    "Not collected: a password, an email for the device user, card numbers, MAC addresses, hardware serial numbers, or the raw IP address.",
    "Purpose: authentication, session continuity, revocation, abuse prevention, and later entitlement checks.",
    "Retention: refresh tokens last until expiry or revocation, authentication events for 180 days, abuse events for 30 days, and idempotency responses for 24 hours. A daily job deletes expired rows.",
    "Deletion: the support page can delete a device with its recovery code and a new security check. An operator audit row may still contain the Device ID and time.",
  ];
  block.append(draftBanner());
  for (const line of lines) block.append(el("p", undefined, line));
  view.append(block);
}

function renderTerms(): void {
  const block = section(t().termsTitle);
  const lines = [
    "NIVEX provides device authentication and PC status. It does not promise a performance increase.",
    "You are responsible for storing the Device ID and recovery code. Without the recovery code, the same installation cannot be restored except by operator deletion.",
    "FREE is the default. Payment is not connected in this version, and a local flag cannot grant PRO.",
    "Do not abuse the service or attempt to take over another installation.",
  ];
  block.append(draftBanner());
  for (const line of lines) block.append(el("p", undefined, line));
  view.append(block);
}

function renderDevice(): void {
  mountDevicePage(view, config, navigate);
}

function renderSupport(): void {
  const block = section(t().supportTitle);
  block.append(el("p", undefined, t().supportLede));
  const mail = el("a", undefined, t().supportMail);
  mail.href = "mailto:support@example.com";
  block.append(mail);
  block.append(el("h3", undefined, t().recoverTitle), el("p", undefined, t().recoverLede));
  block.append(ownerForm("device_recover", "/api/v1/devices/recover", t().recoverAction));
  block.append(el("h3", undefined, t().deleteTitle), el("p", undefined, t().deleteLede));
  block.append(ownerForm("device_delete", "/api/v1/devices/delete", t().deleteAction));
  view.append(block);
}

function ownerForm(action: string, path: string, label: string): HTMLElement {
  const form = el("form", "panel");
  const device = field(t().deviceId);
  const recovery = field(t().recoveryCode);
  const status = el("p", "status");
  const widget = el("div", "turnstile-slot");
  const button = el("button", "btn primary", label);
  button.type = "submit";
  button.disabled = true;
  let verificationId = "";
  form.append(device.label, recovery.label, widget, button, status);
  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (!verificationId) return;
    button.disabled = true;
    const key = crypto.randomUUID().replaceAll("-", "");
    const response = await fetch(api(path), {
      method: "POST",
      headers: { "content-type": "application/json", "idempotency-key": key },
      body: JSON.stringify({ deviceId: device.input.value, recoveryCode: recovery.input.value, verificationId }),
    });
    const body = await response.json() as { activationCode?: string; recoveryCode?: string; deleted?: boolean; error?: { message: string } };
    status.className = response.ok ? "status" : "status error is-shake";
    if (!response.ok) {
      status.textContent = body.error?.message ?? t().failed;
      button.disabled = false;
      return;
    }
    status.textContent = body.deleted ? t().deleteAction : `${t().activation} ${body.activationCode ?? ""} / ${t().recovery} ${body.recoveryCode ?? ""}`;
  });
  if (!config.offline) {
    void mountTurnstile(widget, status, action, config, (id) => {
      verificationId = id;
      button.disabled = false;
    });
  } else status.textContent = t().apiDown;
  return form;
}

function field(label: string): { label: HTMLLabelElement; input: HTMLInputElement } {
  const wrap = el("label", "field");
  wrap.append(el("span", undefined, label));
  const input = el("input");
  input.required = true;
  input.autocomplete = "off";
  wrap.append(input);
  return { label: wrap, input };
}

function renderAdmin(): void {
  const block = section(t().adminTitle);
  block.append(el("p", undefined, t().adminLede));
  const status = el("p", "status");
  const output = el("pre", "specimen-strip");
  const token = sessionStorage.getItem("nivex.admin") ?? "";
  if (!token) {
    const email = field(t().email);
    const password = field(t().password);
    password.input.type = "password";
    const button = el("button", "btn primary", t().login);
    button.type = "button";
    button.addEventListener("click", async () => {
      const response = await fetch(api("/api/v1/admin/login"), {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ email: email.input.value, password: password.input.value }),
      });
      const body = await response.json() as { token?: string; error?: { message: string } };
      if (!response.ok || !body.token) {
        status.textContent = body.error?.message ?? t().failed;
        status.className = "status error is-shake";
        return;
      }
      sessionStorage.setItem("nivex.admin", body.token);
      navigate("/admin");
    });
    block.append(email.label, password.label, button, status);
  } else {
    const search = field(t().deviceId);
    const row = el("div", "actions");
    const buttons: [string, () => Promise<void>][] = [
      [t().search, async () => show(await admin("/api/v1/admin/devices?deviceId=" + encodeURIComponent(search.input.value)))],
      [t().revoke, async () => show(await admin("/api/v1/admin/devices/revoke", { deviceId: search.input.value }))],
      [t().restore, async () => show(await admin("/api/v1/admin/devices/restore", { deviceId: search.input.value }))],
      [t().grant, async () => show(await admin("/api/v1/admin/entitlements", { deviceId: search.input.value, plan: "PRO", action: "grant" }))],
      [t().removePro, async () => show(await admin("/api/v1/admin/entitlements", { deviceId: search.input.value, plan: "PRO", action: "revoke" }))],
    ];
    for (const [label, action] of buttons) {
      const button = el("button", "btn ghost", label);
      button.type = "button";
      button.addEventListener("click", () => void action().catch(() => { status.textContent = t().apiDown; }));
      row.append(button);
    }
    const logout = el("button", "btn text", t().logout);
    logout.type = "button";
    logout.addEventListener("click", async () => {
      await admin("/api/v1/admin/logout", {});
      sessionStorage.removeItem("nivex.admin");
      navigate("/admin");
    });
    block.append(search.label, row, logout, status, output);
  }
  view.append(block);

  async function admin(path: string, body?: unknown): Promise<unknown> {
    const response = await fetch(api(path), {
      method: body ? "POST" : "GET",
      headers: { authorization: `Bearer ${sessionStorage.getItem("nivex.admin") ?? ""}`, "content-type": "application/json" },
      body: body ? JSON.stringify(body) : undefined,
    });
    const payload = await response.json() as { error?: { message: string } };
    status.className = response.ok ? "status" : "status error is-shake";
    status.textContent = response.ok ? "" : payload.error?.message ?? t().failed;
    return payload;
  }
  function show(payload: unknown): void {
    output.textContent = JSON.stringify(payload, null, 2);
  }
}

const pages: Record<string, () => void> = {
  "/": renderHome,
  "/download": renderDownload,
  "/device": renderDevice,
  "/guide": renderGuide,
  "/releases": renderReleases,
  "/privacy": renderPrivacy,
  "/terms": renderTerms,
  "/support": renderSupport,
  "/admin": renderAdmin,
};

function navigate(path: string): void {
  history.pushState({}, "", path);
  paint();
}

function paint(): void {
  paintChrome();
  view.replaceChildren();
  view.classList.remove("page-enter", "page-enter-done");
  (pages[location.pathname] ?? renderHome)();
  enterPage(view);
  window.scrollTo(0, 0);
  view.focus({ preventScroll: true });
}

document.addEventListener("click", (event) => {
  const link = (event.target as Element | null)?.closest("a[data-link]");
  if (!(link instanceof HTMLAnchorElement)) return;
  const url = new URL(link.href);
  if (url.origin !== location.origin) return;
  event.preventDefault();
  navigate(url.pathname);
});
window.addEventListener("popstate", paint);
void loadConfig().then(paint);
