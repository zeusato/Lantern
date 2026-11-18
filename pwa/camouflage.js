import { Calculator } from "./calculator.js";
import { hasCalculatorLock, matchesCalculatorLock } from "./calculator-lock.js";
import { fitCalculatorLine } from "./calculator-fit.js";
let covered = true;
let onboarding = false, revision = 0;
const calculator = new Calculator();
const panel = document.querySelector("#calculator");
const display = document.querySelector("#calculator-display");
const expression = document.querySelector("#calculator-expression");
const calculations = [];
const pretty = value => String(value).replace(/\*/g, "×").replace(/\//g, "÷").replace(/-/g, "−").replace(/\./g, ",");
function showHistory(value) {
  document.querySelector("#calculator-history").hidden = !value;
  document.querySelector("#calculator-screen").hidden = value;
  document.querySelector(".calculator-keys").hidden = value;
  document.querySelector("#calculator-history-button").setAttribute("aria-expanded", String(value));
  document.querySelector("#calculator-history-empty").hidden = calculations.length > 0;
  // These values only come from the arithmetic parser, never chat or user HTML.
  document.querySelector("#calculator-history-list").innerHTML = calculations.map((item, index) => `<button data-calculation="${index}"><span>${pretty(item.expression)}</span><strong>${pretty(item.result)}</strong></button>`).join("");
}
export const isCalculator = () => covered;
let noticeTimer;
export function showNotice(message) {
  if (covered) return;
  const notice = document.querySelector("#toast");
  notice.textContent = message;
  notice.classList.add("visible");
  clearTimeout(noticeTimer);
  noticeTimer = setTimeout(() => notice.classList.remove("visible"), 4200);
}
function render() {
  display.firstElementChild.textContent = pretty(calculator.display);
  expression.firstElementChild.textContent = pretty(calculator.history);
  fitLines();
}
function fitLines() { fitCalculatorLine(display); fitCalculatorLine(expression); }
function setCovered(value) {
  revision++;
  covered = value;
  document.documentElement.dataset.calculator = value ? "on" : "off";
  document.title = value ? "Máy tính" : "Lantern";
  document.querySelector('meta[name="theme-color"]').content = value ? "#000000" : "#f5f8f7";
  panel.hidden = !value;
  const app = document.querySelector("#app"); app.inert = value; app.hidden = value;
  // Clear visible and pending notices. They must never surface after reveal either.
  clearTimeout(noticeTimer);
  const notice = document.querySelector("#toast"); notice.classList.remove("visible"); notice.textContent = "";
  if (value) {
    document.activeElement?.blur?.();
    for (const media of document.querySelectorAll("audio,video")) media.pause();
    navigator.clearAppBadge?.().catch(() => {});
    const sheet = document.querySelector("#sheet"); if (sheet.open) sheet.close();
    calculator.press("AC"); showHistory(false); render();
  }
  document.dispatchEvent(new CustomEvent("calculator-mode", {detail: {covered: value}}));
}
export function showCalculator() { setCovered(true); }
// With no personal expression, enter the normal password gate. An existing vault
// must be authenticated before saving its first expression; there is no shared fallback.
export function initializeCalculator() {
  onboarding = !hasCalculatorLock();
  if (onboarding) setCovered(false);
}
export function calculatorSetupComplete() {
  onboarding = false;
  revision++;
  calculations.length = 0;
  calculator.press("AC"); showHistory(false); render();
}
async function press(key) {
  const operation = ++revision;
  const input = calculator.expression;
  calculator.press(key);
  if (key === "=") {
    // Calculate immediately so a slow password derivation never drops rapid
    // arithmetic input. Hold the expression out of history until it is checked.
    calculator.history = "";
    showHistory(false); render();
    const matches = await matchesCalculatorLock(input);
    if (operation !== revision || !covered || document.hidden) return;
    if (matches) { calculator.press("AC"); render(); setCovered(false); return; }
  }
  if (key === "=" && input && !calculator.error) {
    calculator.history = input;
    calculations.unshift({expression: input, result: calculator.expression});
    calculations.length = Math.min(calculations.length, 30);
  }
  showHistory(false); render();
}
panel.addEventListener("click", event => { const button = event.target.closest("button[data-key]"); if (button) press(button.dataset.key); });
document.querySelector("#calculator-history-button").addEventListener("click", () => showHistory(document.querySelector("#calculator-history").hidden));
document.querySelector("#calculator-basic-button").addEventListener("click", () => showHistory(false));
document.querySelector("#calculator-history-list").addEventListener("click", event => {
  const button = event.target.closest("button[data-calculation]");
  const entry = button && calculations[Number(button.dataset.calculation)];
  if (!entry) return;
  revision++;
  calculator.expression = entry.result; calculator.history = entry.expression; calculator.result = true; calculator.error = false;
  showHistory(false); render();
});
document.addEventListener("keydown", event => {
  if (!covered || event.metaKey || event.ctrlKey || event.altKey) return;
  let key = event.key;
  if (key === "Enter") key = "=";
  if (key === "Escape" || key === "Delete") key = "AC";
  if (key === "Backspace") key = "backspace";
  if (key === "x" || key === "X") key = "*";
  if (key === ",") key = ".";
  if (/^[\d.+*/()%\-=×÷]$/.test(key) || ["AC", "backspace"].includes(key)) { event.preventDefault(); press(key); }
});
document.addEventListener("visibilitychange", () => {
  if (document.hidden) showCalculator();
  else if (onboarding) setCovered(false);
});
window.addEventListener("pagehide", showCalculator);
window.addEventListener("pageshow", event => {
  if (event.persisted) { showCalculator(); if (onboarding && !document.hidden) setCovered(false); }
});
window.addEventListener("resize", fitLines);
window.visualViewport?.addEventListener("resize", fitLines);
if (typeof ResizeObserver !== "undefined") new ResizeObserver(fitLines).observe(panel);
document.fonts?.ready.then(fitLines);
setCovered(true);
