import { copy } from "./i18n.ts";
import { api } from "./api.ts";

interface LookupDevice {
  deviceId: string;
  createdAt: string;
  status: string;
  activationCode: string | null;
}

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className?: string, text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function valueRow(label: string, value: string): HTMLElement {
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
    } catch {
      input.focus();
      input.select();
      button.textContent = copy.copyFailed;
    }
    window.setTimeout(() => { button.textContent = copy.copy; }, 2000);
  });
  line.append(input, button);
  row.append(line);
  return row;
}

function deviceItem(device: LookupDevice): HTMLElement {
  const item = el("li", "lookup-item");
  item.append(valueRow(copy.deviceId, device.deviceId));
  if (device.activationCode) item.append(valueRow(copy.activation, device.activationCode));
  else item.append(el("p", "note", device.status === "unclaimed" ? copy.lookupUnavailable : copy.lookupActivated));
  item.append(el("p", "meta", `${copy.createdAt} ${new Date(device.createdAt).toLocaleString()}`));
  return item;
}

export function mountIpLookup(root: HTMLElement): void {
  const block = el("section", "section lookup");
  const input = el("input");
  input.type = "text";
  input.autocomplete = "off";
  input.spellcheck = false;
  input.maxLength = 45;
  input.placeholder = copy.ipPlaceholder;
  const field = el("label", "field");
  field.append(el("span", undefined, copy.ipLabel), input);

  const find = el("a", "btn ghost", copy.ipFindButton);
  find.href = "https://2ip.io/";
  find.target = "_blank";
  find.rel = "noopener noreferrer";
  const run = el("button", "btn primary", copy.lookupButton);
  run.type = "button";
  const actions = el("div", "id-row");
  actions.append(find, run);

  const status = el("p", "status");
  status.setAttribute("aria-live", "polite");
  const results = el("ul", "lookup-list");
  block.append(el("h2", undefined, copy.lookupTitle), el("p", "lede", copy.lookupLede), el("p", "note", copy.lookupWarn), field, actions, status, results);

  let busy = false;
  const lookup = async () => {
    if (busy) return;
    const typed = input.value.trim();
    status.className = "status";
    results.replaceChildren();
    if (!typed) {
      status.textContent = copy.ipEmpty;
      return;
    }
    busy = true;
    run.disabled = true;
    status.textContent = copy.ipChecking;
    try {
      const response = await fetch(api("/api/v1/ip/devices"), {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ ip: typed }),
      });
      const body = await response.json() as { devices?: LookupDevice[]; used?: number; limit?: number; error?: { message: string } };
      if (!response.ok) {
        status.className = "status error is-shake";
        status.textContent = body.error?.message ?? copy.failed;
        return;
      }
      const devices = body.devices ?? [];
      status.textContent = devices.length === 0
        ? copy.lookupEmpty
        : copy.lookupCount.replace("{used}", String(body.used ?? devices.length)).replace("{limit}", String(body.limit ?? 3));
      results.replaceChildren(...devices.map(deviceItem));
    } catch {
      status.className = "status error is-shake";
      status.textContent = copy.apiDown;
    } finally {
      busy = false;
      run.disabled = false;
    }
  };
  run.addEventListener("click", () => void lookup());
  input.addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
      event.preventDefault();
      void lookup();
    }
  });
  root.append(block);
}
