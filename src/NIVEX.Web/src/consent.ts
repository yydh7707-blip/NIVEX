import { getLang, type Lang } from "./lang.ts";

export const CONSENT_VERSION = "2026-10-30";
let activeConsentVersion = CONSENT_VERSION;
const STORE = "nivex.consent";

export function configureConsentVersion(version: string): void {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(version)) {
    throw new Error("The API returned an invalid consent version.");
  }
  activeConsentVersion = version;
}

export interface ConsentBody {
  accepted: true;
  version: string;
}

export function hasConsent(): boolean {
  try {
    return sessionStorage.getItem(STORE) === activeConsentVersion;
  } catch {
    return false;
  }
}

export function grantConsent(): void {
  try {
    sessionStorage.setItem(STORE, activeConsentVersion);
  } catch {
    // Without storage the consent screen is simply shown again.
  }
}

export function clearConsent(): void {
  try {
    sessionStorage.removeItem(STORE);
  } catch {
    // Nothing to clear.
  }
}

export function consentBody(): ConsentBody {
  return { accepted: true, version: activeConsentVersion };
}

export function isTimeout(error: unknown): boolean {
  return error instanceof DOMException && (error.name === "TimeoutError" || error.name === "AbortError");
}

export const REQUEST_TIMEOUT_MS = 15000;

interface ConsentText {
  title: string;
  lead: string;
  purposeTitle: string;
  purposes: string[];
  itemsTitle: string;
  items: string[];
  retentionTitle: string;
  retention: string[];
  infraTitle: string;
  infra: string;
  refuseTitle: string;
  refuse: string;
  policy: string;
  checkbox: string;
  proceed: string;
  cancel: string;
  needCheck: string;
  agreed: string;
  timedOut: string;
  consentLost: string;
  lookupNeeds: string;
  lookupGo: string;
}

const text: Record<Lang, ConsentText> = {
  ko: {
    title: "IP 주소 수집 및 이용 동의",
    lead: "NIVEX는 Device ID 발급과 IP 인증 기능을 제공하기 위해 서비스 이용 과정에서 확인되는 IP 주소를 처리할 수 있습니다.",
    purposeTitle: "수집 및 이용 목적",
    purposes: [
      "사용자가 입력한 IP 주소와 실제 접속 IP 주소의 일치 여부 확인",
      "IP 주소별 Device ID 발급 한도 관리",
      "비정상적인 반복 요청 및 서비스 악용 방지",
      "인증 과정의 오류 확인 및 서비스 보안 유지",
    ],
    itemsTitle: "처리 항목",
    items: [
      "서버가 확인한 실제 접속 IP 주소 (원본은 저장하지 않고 솔트가 적용된 해시로만 저장)",
      "사용자가 입력한 IP 주소 (접속 IP와 비교하는 데만 사용하며 저장하지 않음)",
      "Device ID 발급 요청 및 처리 결과에 관한 정보, 동의한 약관 버전과 동의 시각",
    ],
    retentionTitle: "보유 및 이용 기간",
    retention: [
      "요청 횟수 제한 기록: 약 2일",
      "남용 방지 이벤트: 30일",
      "인증 이벤트(Device ID와 결과): 180일",
      "IP 해시 발급 기록과 동의 기록: 보유 기간이 아직 확정되지 않았습니다. 운영자가 확정해 이 문서에 게시하기 전까지는 임의의 기간을 약속하지 않습니다.",
    ],
    infraTitle: "접속 로그와의 구분",
    infra: "웹사이트에 접속하면 이 동의와 별개로 호스팅·Cloudflare 등 인프라에서 접속 IP가 전송·처리될 수 있습니다. 위 항목은 Device ID 발급과 IP 인증을 위해 NIVEX가 처리하는 정보입니다. 인프라 로그의 보관 기간과 위치는 확인되지 않았습니다.",
    refuseTitle: "동의 거부 권리 및 불이익",
    refuse: "이용자는 IP 주소 수집 및 이용에 대한 동의를 거부할 수 있습니다. 동의하지 않아도 일반 웹사이트 이용과 프로그램 다운로드는 가능합니다. 다만 IP 인증에 필요한 정보 처리에 동의하지 않으면 Device ID 생성 기능을 이용할 수 없습니다.",
    policy: "개인정보 처리방침 보기",
    checkbox: "[필수] Device ID 발급을 위한 IP 주소 수집 및 이용 내용을 확인했으며 이에 동의합니다.",
    proceed: "계속하기",
    cancel: "취소하고 돌아가기",
    needCheck: "계속하려면 필수 동의 항목을 직접 선택해야 합니다.",
    agreed: "동의가 확인되었습니다. 다음 단계에서 IP 일치 여부와 발급 한도를 확인합니다.",
    timedOut: "요청 시간이 초과되었습니다. 잠시 후 다시 시도하세요.",
    consentLost: "동의 정보가 만료되었습니다. 다시 동의해 주세요.",
    lookupNeeds: "IP 인증과 조회에는 IP 주소 수집·이용 동의가 필요합니다. 동의하지 않아도 다운로드는 그대로 이용할 수 있습니다.",
    lookupGo: "동의 화면으로 이동",
  },
  en: {
    title: "Consent to the Collection and Use of IP Addresses",
    lead: "NIVEX may process IP address information during the Device ID issuance and IP verification process.",
    purposeTitle: "Purpose of Processing",
    purposes: [
      "Checking whether the IP address entered by the user matches the IP address observed by the server",
      "Managing the Device ID issuance limit per IP address",
      "Preventing repeated abusive requests and misuse of the service",
      "Troubleshooting authentication issues and maintaining service security",
    ],
    itemsTitle: "Information Processed",
    items: [
      "IP address observed by the server (the raw address is not stored; only a salted hash is kept)",
      "IP address entered by the user (used only for comparison with the observed address and not stored)",
      "Information about Device ID issuance requests and their processing results, plus the accepted policy version and the time of consent",
    ],
    retentionTitle: "Retention Period",
    retention: [
      "Rate-limit counters: about 2 days",
      "Abuse-prevention events: 30 days",
      "Authentication events (Device ID and result): 180 days",
      "Hashed IP issuance record and consent record: the retention period has not been decided yet. No period is promised until the operator confirms and publishes one.",
    ],
    infraTitle: "Difference from access logs",
    infra: "When you visit the website, your IP address can also be transmitted to and processed by the hosting and Cloudflare infrastructure, independently of this consent. The items above are what NIVEX itself processes for Device ID issuance and IP verification. The retention period and location of infrastructure logs have not been verified.",
    refuseTitle: "Right to Refuse Consent",
    refuse: "You may refuse consent to the collection and use of IP address information. You can continue to browse the website and download the software without granting this consent. However, you cannot use the Device ID generation feature if the required processing is not permitted.",
    policy: "Read the Privacy Policy",
    checkbox: "[Required] I have read and agree to the collection and use of IP address information for Device ID issuance.",
    proceed: "Continue",
    cancel: "Cancel and go back",
    needCheck: "To continue, you must select the required consent box yourself.",
    agreed: "Consent recorded. The next step checks that the IP matches and the issuance limit.",
    timedOut: "The request timed out. Please try again in a moment.",
    consentLost: "Your consent has expired. Please agree again.",
    lookupNeeds: "IP verification and lookup require consent to the collection and use of your IP address. Downloads remain available without consent.",
    lookupGo: "Go to the consent screen",
  },
};

export function ct(): ConsentText {
  return text[getLang()];
}

let draftChecked = false;

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className?: string, content?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (content !== undefined) node.textContent = content;
  return node;
}

function list(items: string[]): HTMLUListElement {
  const ul = el("ul", "consent-list");
  for (const item of items) ul.append(el("li", undefined, item));
  return ul;
}

export function buildConsentPanel(onAgree: () => void, onCancel: () => void): HTMLElement {
  const t = ct();
  const panel = el("section", "consent");
  panel.setAttribute("aria-labelledby", "consent-title");

  const title = el("h1", undefined, t.title);
  title.id = "consent-title";

  const groups = el("div", "consent-groups");
  const group = (heading: string, body: HTMLElement) => {
    const block = el("div", "consent-group");
    block.append(el("h2", undefined, heading), body);
    groups.append(block);
  };
  group(t.purposeTitle, list(t.purposes));
  group(t.itemsTitle, list(t.items));
  group(t.retentionTitle, list(t.retention));
  group(t.infraTitle, el("p", undefined, t.infra));
  group(t.refuseTitle, el("p", undefined, t.refuse));

  const policy = el("a", "consent-policy", t.policy);
  policy.href = "/privacy";
  policy.dataset.link = "";

  const box = el("input");
  box.type = "checkbox";
  box.id = "consent-check";
  box.checked = draftChecked;
  const label = el("label", "consent-check");
  label.htmlFor = "consent-check";
  label.append(box, el("span", undefined, t.checkbox));

  const status = el("p", "status");
  status.setAttribute("aria-live", "polite");
  const proceed = el("button", "btn primary", t.proceed);
  proceed.type = "button";
  proceed.disabled = !box.checked;
  const cancel = el("button", "btn ghost", t.cancel);
  cancel.type = "button";
  const actions = el("div", "id-row");
  actions.append(cancel, proceed);

  box.addEventListener("change", () => {
    draftChecked = box.checked;
    proceed.disabled = !box.checked;
    status.className = "status";
    status.textContent = box.checked ? "" : t.needCheck;
  });
  proceed.addEventListener("click", () => {
    if (!box.checked) {
      status.className = "status error";
      status.textContent = t.needCheck;
      return;
    }
    draftChecked = false;
    grantConsent();
    onAgree();
  });
  cancel.addEventListener("click", () => {
    draftChecked = false;
    onCancel();
  });

  panel.append(title, el("p", "lede", t.lead), groups, policy, label, status, actions);
  return panel;
}
