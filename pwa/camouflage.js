import { Calculator } from "./calculator.js";
let covered = true;
const calculator = new Calculator();
const panel = document.querySelector("#calculator");
const display = document.querySelector("#calculator-display");
const expression = document.querySelector("#calculator-expression");
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
  display.textContent = calculator.display;
  expression.textContent = calculator.history.replace(/\*/g, "×").replace(/\//g, "÷");
  display.dataset.long = String(calculator.display.length > 12);
}
function setCovered(value) {
  covered = value;
  document.documentElement.dataset.calculator = value ? "on" : "off";
  document.title = value ? "Máy tính" : "Lantern";
  document.querySelector('meta[name="theme-color"]').content = value ? "#101114" : "#f5f8f7";
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
    calculator.press("AC"); render();
  }
  document.dispatchEvent(new CustomEvent("calculator-mode", {detail: {covered: value}}));
}
export function showCalculator() { setCovered(true); }
function press(key) { if (calculator.press(key)) setCovered(false); else render(); }
panel.addEventListener("click", event => { const button = event.target.closest("button[data-key]"); if (button) press(button.dataset.key); });
document.addEventListener("keydown", event => {
  if (!covered || event.metaKey || event.ctrlKey || event.altKey) return;
  let key = event.key;
  if (key === "Enter") key = "=";
  if (key === "Escape" || key === "Delete") key = "AC";
  if (key === "Backspace") key = "backspace";
  if (key === "x" || key === "X") key = "*";
  if (/^[\d.+*/()%\-=×÷]$/.test(key) || ["AC", "backspace"].includes(key)) { event.preventDefault(); press(key); }
});
document.addEventListener("visibilitychange", () => { if (document.hidden) showCalculator(); });
window.addEventListener("pagehide", showCalculator);
window.addEventListener("pageshow", event => { if (event.persisted) showCalculator(); });
setCovered(true);
