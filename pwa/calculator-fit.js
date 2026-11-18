// Measure the rendered font, rather than guessing a breakpoint from character count.
export function fitCalculatorLine(element) {
  const text = element.firstElementChild;
  if (!text || !element.clientWidth) return;
  element.style.fontSize = "";
  const maximum = parseFloat(getComputedStyle(element).fontSize);
  const width = text.getBoundingClientRect().width;
  if (width > element.clientWidth - 1) {
    element.style.fontSize = `${Math.max(1, Math.floor(maximum * (element.clientWidth - 1) / width * 100) / 100)}px`;
  }
}
