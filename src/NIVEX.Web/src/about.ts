import { en, ko, type AboutCopy } from "./aboutCopy.ts";
import { getLang, type Lang } from "./lang.ts";
import { operator } from "./operator.ts";

export interface AboutConfig {
  version: string;
  downloadUrl: string;
  offline?: boolean;
}

export function aboutCopy(lang: Lang): AboutCopy {
  return lang === "ko" ? ko : en;
}

export function currentLang(): Lang {
  return getLang();
}

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className?: string, text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function scrollToId(id: string): void {
  const target = document.getElementById(id);
  if (!target) return;
  const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  target.scrollIntoView({ behavior: reduced ? "auto" : "smooth", block: "start" });
  target.focus({ preventScroll: true });
}

function anchor(className: string, text: string, id: string): HTMLAnchorElement {
  const link = el("a", className, text);
  link.href = `#${id}`;
  link.addEventListener("click", (event) => {
    event.preventDefault();
    scrollToId(id);
  });
  return link;
}

function section(id: string, title: string): HTMLElement {
  const block = el("section", "about-section");
  block.id = id;
  block.tabIndex = -1;
  const heading = el("h2", undefined, title);
  block.append(heading);
  return block;
}

function hero(c: AboutCopy, config: AboutConfig): HTMLElement {
  const block = el("section", "about-hero");
  const copy = el("div", "about-hero-copy");
  const actions = el("div", "actions");
  const download = el("a", "btn primary", c.ctaDownload);
  download.href = "/download";
  download.dataset.link = "";
  actions.append(anchor("btn ghost", c.ctaDiscover, "about"), anchor("btn ghost", c.ctaFeatures, "features"), download);
  copy.append(el("p", "kicker", c.kicker), el("h1", undefined, c.heroTitle), el("p", "lede", c.heroLede), actions);

  const facts = el("dl", "facts");
  facts.append(el("p", "meta", c.factsTitle));
  const rows: [string, string][] = [
    c.facts[0]!,
    [c.factVersionLabel, config.offline ? c.factOffline : config.version],
    ...c.facts.slice(1),
  ];
  for (const [label, value] of rows) {
    const row = el("div", "fact");
    row.append(el("dt", undefined, label), el("dd", undefined, value));
    facts.append(row);
  }
  block.append(copy, facts);
  return block;
}

function purpose(c: AboutCopy): HTMLElement {
  const block = section("about", c.purposeTitle);
  const body = el("div", "prose");
  c.purposeBody.forEach((line) => body.append(el("p", undefined, line)));
  block.append(body);
  return block;
}

function features(c: AboutCopy): HTMLElement {
  const block = section("features", c.featuresTitle);
  block.append(el("p", "lede", c.featuresLede));
  const list = el("ol", "feature-list");
  c.features.forEach((feature, index) => {
    const item = el("li", "feature");
    const head = el("div", "feature-head");
    head.append(el("span", "feature-index", String(index + 1).padStart(2, "0")), el("h3", undefined, feature.name));
    const text = el("div", "feature-body");
    text.append(el("p", "feature-lead", feature.body), el("p", "feature-detail", feature.detail), el("span", "tag", c.statusAvailable));
    item.append(head, text);
    list.append(item);
  });
  block.append(list);
  return block;
}

function definitionList(items: [string, string][]): HTMLElement {
  const list = el("dl", "def-list");
  for (const [term, detail] of items) {
    const row = el("div", "def-row");
    row.append(el("dt", undefined, term), el("dd", undefined, detail));
    list.append(row);
  }
  return list;
}

function distribution(c: AboutCopy): HTMLElement {
  const block = section("distribution", c.distTitle);
  block.append(el("p", "lede", c.distBody), definitionList(c.distItems));
  return block;
}

function privacy(c: AboutCopy): HTMLElement {
  const block = section("device-data", c.privacyTitle);
  block.append(el("p", "lede", c.privacyLede), definitionList(c.privacyItems));
  const link = el("a", "btn ghost", c.privacyLink);
  link.href = "/privacy";
  link.dataset.link = "";
  const actions = el("div", "actions");
  actions.append(link);
  block.append(actions);
  return block;
}

function documents(c: AboutCopy): HTMLElement {
  const block = section("documents", c.docsTitle);
  block.append(el("p", "lede", c.docsLede));
  const grid = el("ul", "doc-list");
  for (const doc of c.docs) {
    const item = el("li", "doc");
    const head = el("div", "doc-head");
    let title: HTMLElement;
    if (!doc.href) title = el("span", "doc-title is-missing", doc.title);
    else if (doc.href.startsWith("#")) title = anchor("doc-title", doc.title, doc.href.slice(1));
    else {
      const link = el("a", "doc-title", doc.title);
      link.href = doc.href;
      link.dataset.link = "";
      title = link;
    }
    head.append(title, el("span", `tag is-${doc.status}`, c.docStatus[doc.status]));
    item.append(head, el("p", undefined, doc.body));
    grid.append(item);
  }
  block.append(grid);
  return block;
}

function operatorRows(c: AboutCopy): [string, string | null][] {
  const f = c.operatorFields;
  const text = (value: string) => value.trim() || null;
  return [
    [f.name, text(operator.operatorName)],
    [f.contact, text(operator.publicContact)],
    [f.email, text(operator.supportEmail)],
    [f.business, operator.businessStatus ? c.businessValues[operator.businessStatus] : null],
    [f.businessNumber, operator.businessStatus === "registered" ? text(operator.businessNumber) : null],
    [f.mailOrder, operator.mailOrderStatus ? c.mailOrderValues[operator.mailOrderStatus] : null],
    [f.mailOrderNumber, operator.mailOrderStatus === "reported" ? text(operator.mailOrderNumber) : null],
    [f.sales, text(operator.salesNotice)],
  ];
}

function operatorSection(c: AboutCopy): HTMLElement {
  const block = section("operator", c.operatorTitle);
  block.append(el("p", "lede", c.operatorLede));
  const rows = operatorRows(c);
  if (rows.every(([, value]) => value === null)) block.append(el("p", "note", c.operatorEmpty));
  const list = el("dl", "def-list");
  for (const [term, value] of rows) {
    const row = el("div", "def-row");
    row.append(el("dt", undefined, term), el("dd", value === null ? "is-empty" : undefined, value ?? c.operatorNone));
    list.append(row);
  }
  block.append(list);
  return block;
}

function faq(c: AboutCopy): HTMLElement {
  const block = section("faq", c.faqTitle);
  const list = el("div", "faq-list");
  c.faq.forEach(([question, answer], index) => {
    const item = el("details", "faq-item");
    if (index === 0) item.open = true;
    item.append(el("summary", undefined, question), el("p", undefined, answer));
    list.append(item);
  });
  block.append(list);
  return block;
}

export function renderAbout(view: HTMLElement, config: AboutConfig): void {
  const lang = getLang();
  const c = aboutCopy(lang);
  document.title = `${c.pageTitle} · NIVEX`;
  const page = el("div", "about");
  page.lang = lang;
  page.append(
    hero(c, config),
    purpose(c),
    features(c),
    distribution(c),
    privacy(c),
    documents(c),
    operatorSection(c),
    faq(c),
    el("p", "disclaimer", c.disclaimer),
  );
  view.replaceChildren(page);
}
