import { policies, policyMeta, policyUi, type Block, type PolicyKey, type PolicyLang } from "./policyCopy.ts";
import { operator } from "./operator.ts";
import { nivexMark } from "./icons.ts";

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className?: string, text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function internalLink(href: string, text: string, className?: string): HTMLAnchorElement {
  const link = el("a", className, text);
  link.href = href;
  link.dataset.link = "";
  return link;
}

function scrollToId(id: string): void {
  const target = document.getElementById(id);
  if (!target) return;
  const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  target.scrollIntoView({ behavior: reduced ? "auto" : "smooth", block: "start" });
  target.focus({ preventScroll: true });
  history.replaceState(history.state, "", `#${id}`);
}

function anchor(text: string, id: string, className?: string): HTMLAnchorElement {
  const link = el("a", className, text);
  link.href = `#${id}`;
  link.addEventListener("click", (event) => {
    event.preventDefault();
    scrollToId(id);
  });
  return link;
}

function formatDate(iso: string, lang: PolicyLang): string {
  const [y, m, d] = iso.split("-").map(Number);
  if (!y || !m || !d) return iso;
  return new Intl.DateTimeFormat(lang === "ko" ? "ko-KR" : "en-US", { year: "numeric", month: "long", day: "numeric", timeZone: "UTC" })
    .format(new Date(Date.UTC(y, m - 1, d)));
}

function fill(value: string, missing: string): { node: Node; empty: boolean } {
  if (value === "{{operator}}" || value === "{{email}}") {
    const real = (value === "{{operator}}" ? operator.operatorName : operator.supportEmail).trim();
    if (!real) return { node: el("span", "is-empty", missing), empty: true };
    if (value === "{{email}}") {
      const mail = el("a", undefined, real);
      mail.href = `mailto:${real}`;
      return { node: mail, empty: false };
    }
    return { node: document.createTextNode(real), empty: false };
  }
  if (value.startsWith("/")) return { node: internalLink(value, value), empty: false };
  return { node: document.createTextNode(value), empty: false };
}

function renderBlock(block: Block, missing: string, supportLabel: string): HTMLElement {
  if (block.t === "p") return el("p", undefined, block.x);
  if (block.t === "ul") {
    const list = el("ul");
    block.x.forEach((item) => list.append(el("li", undefined, item)));
    return list;
  }
  const wrap = el("div", "policy-table-wrap");
  const table = el("table", "policy-table");
  const head = el("thead");
  const headRow = el("tr");
  block.head.forEach((cell) => {
    const th = el("th", undefined, cell);
    th.scope = "col";
    headRow.append(th);
  });
  head.append(headRow);
  const body = el("tbody");
  for (const [term, detail] of block.rows) {
    const row = el("tr");
    const th = el("th", undefined, term);
    th.scope = "row";
    const td = el("td");
    const filled = fill(detail, missing);
    if (detail === "/support") td.append(internalLink("/support", supportLabel));
    else td.append(filled.node);
    row.append(th, td);
    body.append(row);
  }
  table.append(head, body);
  wrap.append(table);
  return wrap;
}

export function documentTitle(key: PolicyKey, lang: PolicyLang): string {
  return `${policies[key][lang].title} · NIVEX`;
}

export function renderPolicy(view: HTMLElement, key: PolicyKey, lang: PolicyLang): void {
  const doc = policies[key][lang];
  const ui = policyUi[lang];
  const meta = policyMeta[key];
  const other: PolicyKey = key === "privacy" ? "terms" : "privacy";
  const supportLabel = ui.supportLink;

  const page = el("article", "policy");
  page.lang = lang;

  const head = el("header", "policy-head");
  const brand = internalLink("/", "", "policy-brand");
  brand.setAttribute("aria-label", ui.backHome);
  brand.append(nivexMark(), el("span", undefined, "NIVEX"));
  const title = el("h1", undefined, doc.title);
  title.id = "policy-title";
  const dates = el("dl", "policy-dates");
  const updated = el("div");
  updated.append(el("dt", undefined, ui.updated), el("dd", undefined, formatDate(meta.updated, lang)));
  const effective = el("div");
  effective.append(
    el("dt", undefined, ui.effective),
    el("dd", meta.effective ? undefined : "is-empty", meta.effective ? formatDate(meta.effective, lang) : ui.effectivePending),
  );
  dates.append(updated, effective);
  head.append(brand, title, el("p", "lede", doc.lead), dates);

  const incomplete = !operator.operatorName.trim() || !operator.supportEmail.trim() || !meta.effective;
  const banner = incomplete ? el("aside", "policy-banner") : null;
  if (banner) {
    banner.setAttribute("role", "note");
    banner.append(el("strong", undefined, ui.draftTitle), el("span", undefined, ui.draftBody));
  }

  const layout = el("div", "policy-layout");
  const toc = el("nav", "policy-toc");
  toc.setAttribute("aria-label", ui.toc);
  toc.append(el("p", "meta", ui.toc));
  const tocList = el("ol");
  doc.sections.forEach((section) => {
    const item = el("li");
    item.append(anchor(section.title.replace(/^\d+\.\s*/, ""), `${key}-${section.id}`));
    tocList.append(item);
  });
  toc.append(tocList);

  const body = el("div", "policy-body");
  doc.sections.forEach((section, index) => {
    const block = el("section", "policy-section");
    block.id = `${key}-${section.id}`;
    block.tabIndex = -1;
    block.setAttribute("aria-labelledby", `${block.id}-h`);
    const heading = el("h2", undefined, section.title);
    heading.id = `${block.id}-h`;
    block.append(heading);
    section.blocks.forEach((b) => block.append(renderBlock(b, ui.notPublished, supportLabel)));

    const nav = el("div", "policy-step");
    const prev = doc.sections[index - 1];
    const next = doc.sections[index + 1];
    if (prev) nav.append(anchor(`← ${ui.prev}: ${prev.title.replace(/^\d+\.\s*/, "")}`, `${key}-${prev.id}`));
    else nav.append(anchor(`↑ ${ui.top}`, "policy-title"));
    if (next) nav.append(anchor(`${ui.next}: ${next.title.replace(/^\d+\.\s*/, "")} →`, `${key}-${next.id}`));
    block.append(nav);
    body.append(block);
  });

  const support = el("aside", "policy-support");
  support.append(el("h2", undefined, ui.supportTitle), el("p", undefined, ui.supportBody));
  const actions = el("div", "actions");
  actions.append(internalLink("/support", ui.supportLink, "btn primary"), internalLink(`/${other}`, ui.other[other], "btn ghost"));
  support.append(actions);
  body.append(support);

  layout.append(toc, body);
  page.append(head);
  if (banner) page.append(banner);
  page.append(layout);
  view.replaceChildren(page);
}
