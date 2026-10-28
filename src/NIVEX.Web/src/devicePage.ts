import { copy } from "./i18n.ts";
import { deviceGlyph, nivexMark, successCheck } from "./icons.ts";
import { transitionPanels, wait } from "./motion.ts";
import { mountTurnstile } from "./turnstileUi.ts";
import { api } from "./api.ts";
import { mountIpLookup } from "./ipLookup.ts";
import { buildConsentPanel, clearConsent, consentBody, ct, hasConsent, isTimeout, REQUEST_TIMEOUT_MS } from "./consent.ts";

export interface PublicConfig {
  version: string;
  turnstileSiteKey: string;
  devVerification: boolean;
  downloadUrl: string;
  offline?: boolean;
}

export interface CreatedDevice {
  deviceId: string;
  activationCode: string;
  recoveryCode: string;
  createdAt: string;
}

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className?: string, text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function footerLinks(): HTMLElement {
  const row = el("p", "flow-foot");
  const privacy = el("a", undefined, copy.privacyTitle);
  privacy.href = "/privacy";
  privacy.dataset.link = "";
  const support = el("a", undefined, copy.supportTitle);
  support.href = "/support";
  support.dataset.link = "";
  row.append(privacy, el("span", undefined, " · "), support);
  return row;
}

function wordmarkRow(): HTMLElement {
  const row = el("div", "wordmark");
  row.append(nivexMark(), el("span", undefined, "NIVEX"));
  return row;
}

export function mountDevicePage(
  root: HTMLElement,
  config: PublicConfig,
  onNavigate: (path: string) => void,
): void {
  root.className = "device-page";
  root.append(wordmarkRow());

  if (config.offline) {
    const offline = el("div", "flow-panel is-active");
    offline.append(el("h1", undefined, copy.deviceTitle), el("p", "lede", copy.apiDown));
    root.append(offline);
    return;
  }

  const stored = sessionStorage.getItem("nivex.created");
  if (stored) {
    const panel = successPanel(JSON.parse(stored) as CreatedDevice, true, config, onNavigate);
    panel.classList.add("flow-panel", "is-active");
    root.append(panel);
    mountIpLookup(root);
    return;
  }

  const stage = el("div", "flow-stage");
  root.append(stage);
  if (!hasConsent()) {
    const consent = buildConsentPanel(
      () => {
        stage.replaceChildren();
        mountVerifyStage(stage, config, onNavigate);
        mountIpLookup(root);
      },
      () => onNavigate("/"),
    );
    consent.classList.add("flow-panel", "is-active", "flow-enter");
    requestAnimationFrame(() => consent.classList.add("flow-enter-done"));
    stage.append(consent);
    return;
  }
  mountVerifyStage(stage, config, onNavigate);
  mountIpLookup(root);
}

function mountVerifyStage(stage: HTMLElement, config: PublicConfig, onNavigate: (path: string) => void): void {
  const panel = el("div");
  const status = el("p", "status");
  const widget = el("div", "turnstile-slot");
  panel.append(
    el("h1", undefined, copy.deviceVerifyTitle),
    el("p", "lede", copy.deviceVerifyLead),
    widget,
    status,
    footerLinks(),
  );
  panel.classList.add("flow-panel", "is-active", "flow-enter");
  requestAnimationFrame(() => panel.classList.add("flow-enter-done"));
  stage.replaceChildren(panel);

  const goCreate = (id: string) => {
    const next = buildCreatePanel(stage, config, id, onNavigate);
    transitionPanels(stage, next);
  };

  void mountTurnstile(widget, status, "device_create", config, goCreate);
}

function buildCreatePanel(
  stage: HTMLElement,
  config: PublicConfig,
  verificationId: string,
  onNavigate: (path: string) => void,
): HTMLElement {
  const panel = el("div");
  const glyph = el("div", "glyph");
  glyph.append(deviceGlyph());
  const generate = el("button", "btn primary", copy.generate);
  generate.type = "button";
  generate.disabled = true;
  const status = el("p", "status");
  status.setAttribute("aria-live", "polite");

  const field = el("label", "field");
  const input = el("input");
  input.type = "text";
  input.inputMode = "text";
  input.autocomplete = "off";
  input.spellcheck = false;
  input.maxLength = 45;
  input.placeholder = copy.ipPlaceholder;
  field.append(el("span", undefined, copy.ipLabel), input);
  const check = el("button", "btn ghost", copy.ipCheck);
  check.type = "button";

  const findIp = el("a", "btn ghost", copy.ipFindButton) as HTMLAnchorElement;
  findIp.href = "https://2ip.io/";
  findIp.target = "_blank";
  findIp.rel = "noopener noreferrer";

  let confirmedIp = "";
  const resetConfirmation = () => {
    confirmedIp = "";
    generate.disabled = true;
  };
  input.addEventListener("input", resetConfirmation);

  const runCheck = async () => {
    const typed = input.value.trim();
    status.classList.remove("error", "is-shake");
    if (!typed) {
      status.textContent = copy.ipEmpty;
      return;
    }
    check.disabled = true;
    status.textContent = copy.ipChecking;
    try {
      const response = await fetch(api("/api/v1/ip/check"), {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ ip: typed, consent: consentBody() }),
        signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
      });
      const body = await response.json() as { remaining?: number; limit?: number; error?: { code?: string; message: string } };
      if (!response.ok) {
        if (body.error?.code === "consent_required") {
          clearConsent();
          status.textContent = ct().consentLost;
          status.classList.add("error", "is-shake");
          window.setTimeout(() => location.reload(), 1200);
          return;
        }
        status.textContent = body.error?.message ?? copy.failed;
        status.classList.add("error", "is-shake");
        return;
      }
      if ((body.remaining ?? 0) <= 0) {
        status.textContent = copy.ipLimit;
        status.classList.add("error", "is-shake");
        return;
      }
      confirmedIp = typed;
      generate.disabled = false;
      status.textContent = copy.ipRemaining
        .replace("{remaining}", String(body.remaining))
        .replace("{limit}", String(body.limit));
    } catch (error) {
      status.textContent = isTimeout(error) ? ct().timedOut : copy.apiDown;
      status.classList.add("error", "is-shake");
    } finally {
      check.disabled = false;
    }
  };
  check.addEventListener("click", () => void runCheck());
  input.addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
      event.preventDefault();
      void runCheck();
    }
  });

  const actions = el("div", "id-row");
  actions.append(check, generate);
  panel.append(
    glyph,
    el("h1", undefined, copy.deviceCreateTitle),
    el("p", "lede", copy.deviceCreateLead),
    el("p", "lede", copy.ipHint),
    el("p", "lede", copy.ipFind),
    findIp,
    field,
    actions,
    status,
    footerLinks(),
  );

  generate.addEventListener("click", () => {
    if (!confirmedIp) return;
    void runCreate(
      stage,
      verificationId,
      confirmedIp,
      config,
      onNavigate,
      () => mountVerifyStage(stage, config, onNavigate),
      () => transitionPanels(stage, buildCreatePanel(stage, config, verificationId, onNavigate)),
    );
  });
  return panel;
}

async function runCreate(
  stage: HTMLElement,
  verificationId: string,
  ip: string,
  config: PublicConfig,
  onNavigate: (path: string) => void,
  onRetryVerify: () => void,
  onRetryCreate: () => void,
): Promise<void> {
  const progress = buildProgressPanel();
  transitionPanels(stage, progress);

  const markSteps = (active: number, doneUntil: number) => {
    progress.querySelectorAll<HTMLElement>(".step").forEach((item, i) => {
      item.classList.remove("is-active", "is-done");
      if (i < doneUntil) item.classList.add("is-done");
      else if (i === active) item.classList.add("is-active");
    });
  };

  markSteps(0, 0);
  await wait(120);
  markSteps(1, 1);

  let key = sessionStorage.getItem("nivex.idempotency");
  if (!key) {
    key = crypto.randomUUID().replaceAll("-", "");
    sessionStorage.setItem("nivex.idempotency", key);
  }

  try {
    const response = await fetch(api("/api/v1/devices"), {
      method: "POST",
      headers: { "content-type": "application/json", "idempotency-key": key },
      body: JSON.stringify({ verificationId, ip, consent: consentBody() }),
      signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
    });
    markSteps(2, 2);
    const body = await response.json() as CreatedDevice & { error?: { code: string; message: string } };
    if (!response.ok) {
      if (body.error?.code === "consent_required") clearConsent();
      const consumed = body.error?.code === "verification_invalid";
      const ipRejected = body.error?.code === "ip_mismatch" || body.error?.code === "ip_limit" || body.error?.code === "ip_invalid";
      if (consumed || ipRejected) sessionStorage.removeItem("nivex.idempotency");
      const retry = consumed ? onRetryVerify : ipRejected && body.error?.code !== "ip_limit" ? onRetryCreate : undefined;
      const err = buildErrorPanel(body.error?.message ?? copy.failed, retry);
      transitionPanels(stage, err);
      return;
    }
    markSteps(3, 4);
    sessionStorage.setItem("nivex.created", JSON.stringify(body));
    await wait(180);
    const done = successPanel(body, false, config, onNavigate);
    transitionPanels(stage, done);
  } catch (error) {
    const err = buildErrorPanel(isTimeout(error) ? ct().timedOut : copy.apiDown, onRetryCreate);
    transitionPanels(stage, err);
  }
}

function buildProgressPanel(): HTMLElement {
  const panel = el("div");
  const list = el("ol", "steps");
  for (const label of [copy.stepInit, copy.stepCreate, copy.stepRegister, copy.stepDone]) {
    const item = el("li", "step");
    item.append(el("span", "step-dot"), el("span", undefined, label));
    list.append(item);
  }
  list.firstElementChild?.classList.add("is-active");
  panel.append(el("h1", undefined, copy.deviceProgressTitle), list);
  return panel;
}

function buildErrorPanel(message: string, onRetry?: () => void): HTMLElement {
  const panel = el("div");
  const status = el("p", "status error is-shake");
  status.textContent = message;
  panel.append(el("h1", undefined, copy.failed), status);
  if (onRetry) {
    const retry = el("button", "btn ghost", copy.retry);
    retry.type = "button";
    retry.addEventListener("click", onRetry);
    panel.append(retry);
  }
  return panel;
}

function successPanel(
  device: CreatedDevice,
  stored: boolean,
  config: PublicConfig,
  onNavigate: (path: string) => void,
): HTMLElement {
  const panel = el("div", stored ? "success-panel is-static" : "success-panel");
  panel.append(successCheck(), el("h1", undefined, copy.created));
  panel.append(codeRow(copy.deviceId, device.deviceId));
  panel.append(codeRow(copy.activation, device.activationCode));
  panel.append(codeRow(copy.recovery, device.recoveryCode));
  panel.append(el("p", "meta", `${copy.createdAt} ${new Date(device.createdAt).toLocaleString()}`));
  panel.append(el("p", "lede", copy.next));
  const actions = el("div", "actions");
  if (config.downloadUrl) {
    const link = el("a", "btn primary", copy.downloadReady);
    link.href = config.downloadUrl;
    actions.append(link);
  }
  const guide = el("a", "btn ghost", copy.guideTitle);
  guide.href = "/guide";
  guide.dataset.link = "";
  const again = el("button", "btn text", copy.another);
  again.type = "button";
  again.addEventListener("click", () => {
    sessionStorage.removeItem("nivex.created");
    sessionStorage.removeItem("nivex.idempotency");
    onNavigate("/device");
  });
  actions.append(guide, again);
  panel.append(actions);
  return panel;
}

function codeRow(label: string, value: string): HTMLElement {
  const row = el("label", "field");
  row.append(el("span", undefined, label));
  const line = el("div", "id-row");
  const input = el("input");
  input.readOnly = true;
  input.value = value;
  const button = el("button", "btn ghost", copy.copy);
  button.type = "button";
  button.addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(value);
      button.textContent = copy.copied;
      button.classList.add("is-copied");
      window.setTimeout(() => {
        button.textContent = copy.copy;
        button.classList.remove("is-copied");
      }, 2000);
    } catch {
      input.focus();
      input.select();
      button.textContent = copy.copyFailed;
    }
  });
  line.append(input, button);
  row.append(line);
  return row;
}
