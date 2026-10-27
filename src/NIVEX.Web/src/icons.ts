export function nivexMark(): SVGSVGElement {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 32 32");
  svg.setAttribute("class", "mark-svg");
  svg.setAttribute("aria-hidden", "true");
  const rect = document.createElementNS("http://www.w3.org/2000/svg", "rect");
  rect.setAttribute("x", "1.25");
  rect.setAttribute("y", "1.25");
  rect.setAttribute("width", "29.5");
  rect.setAttribute("height", "29.5");
  rect.setAttribute("fill", "none");
  rect.setAttribute("stroke", "currentColor");
  rect.setAttribute("stroke-width", "1.4");
  const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
  path.setAttribute("d", "M9 23V9h2.4l9.2 10.8V9H23v14h-2.4L11.4 12.2V23H9z");
  path.setAttribute("fill", "currentColor");
  svg.append(rect, path);
  return svg;
}

export function deviceGlyph(): SVGSVGElement {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 48 48");
  svg.setAttribute("class", "glyph-svg");
  svg.setAttribute("aria-hidden", "true");
  const outer = document.createElementNS("http://www.w3.org/2000/svg", "rect");
  outer.setAttribute("x", "8");
  outer.setAttribute("y", "6");
  outer.setAttribute("width", "32");
  outer.setAttribute("height", "36");
  outer.setAttribute("rx", "4");
  outer.setAttribute("fill", "none");
  outer.setAttribute("stroke", "currentColor");
  outer.setAttribute("stroke-width", "1.5");
  const inner = document.createElementNS("http://www.w3.org/2000/svg", "path");
  inner.setAttribute("d", "M16 18h16M16 24h10M16 30h14");
  inner.setAttribute("stroke", "currentColor");
  inner.setAttribute("stroke-width", "1.5");
  inner.setAttribute("stroke-linecap", "round");
  const dot = document.createElementNS("http://www.w3.org/2000/svg", "circle");
  dot.setAttribute("cx", "24");
  dot.setAttribute("cy", "12");
  dot.setAttribute("r", "2");
  dot.setAttribute("fill", "currentColor");
  svg.append(outer, inner, dot);
  return svg;
}

export function successCheck(): SVGSVGElement {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 32 32");
  svg.setAttribute("class", "check");
  const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
  path.setAttribute("d", "M7 16.5 13 22.5 25 10");
  svg.append(path);
  return svg;
}
