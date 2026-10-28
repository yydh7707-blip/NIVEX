const reduced = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;

export function enterPage(node: HTMLElement): void {
  node.classList.add("page-enter");
  if (reduced()) node.classList.add("page-enter-done");
  else {
    requestAnimationFrame(() => node.classList.add("page-enter-done"));
  }
}

export function transitionPanels(
  container: HTMLElement,
  next: HTMLElement,
  onDone?: () => void,
): void {
  const current = container.querySelector<HTMLElement>(".flow-panel.is-active");
  if (!current || reduced()) {
    container.replaceChildren(next);
    next.classList.add("flow-panel", "is-active", "flow-enter");
    requestAnimationFrame(() => next.classList.add("flow-enter-done"));
    onDone?.();
    return;
  }
  current.classList.add("flow-exit");
  let finished = false;
  let fallback = 0;
  const finish = () => {
    if (finished) return;
    finished = true;
    window.clearTimeout(fallback);
    current.removeEventListener("transitionend", finish);
    container.replaceChildren(next);
    next.classList.add("flow-panel", "is-active", "flow-enter");
    requestAnimationFrame(() => {
      next.classList.add("flow-enter-done");
      onDone?.();
    });
  };
  current.addEventListener("transitionend", finish, { once: true });
  fallback = window.setTimeout(finish, 320);
}

export async function wait(ms: number): Promise<void> {
  if (reduced()) return;
  await new Promise((resolve) => window.setTimeout(resolve, ms));
}
