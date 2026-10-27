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
  const finish = () => {
    current.removeEventListener("transitionend", finish);
    container.replaceChildren(next);
    next.classList.add("flow-panel", "is-active", "flow-enter");
    requestAnimationFrame(() => {
      next.classList.add("flow-enter-done");
      onDone?.();
    });
  };
  current.addEventListener("transitionend", finish, { once: true });
  window.setTimeout(finish, 320);
}

export async function wait(ms: number): Promise<void> {
  if (reduced()) return;
  await new Promise((resolve) => window.setTimeout(resolve, ms));
}
