// A small arithmetic parser: user input is never evaluated as JavaScript.
export const normalized = value => String(value).replace(/\s/g, "").replace(/×/g, "*").replace(/÷/g, "/").replace(/−/g, "-");
export function calculate(value) {
  const source = normalized(value);
  if (!source || source.length > 160 || /[^0-9eE.+*/()%\-]/.test(source)) throw Error("Phép tính không hợp lệ");
  let at = 0;
  function number() {
    let sign = 1;
    while (source[at] === "+" || source[at] === "-") if (source[at++] === "-") sign *= -1;
    let value;
    if (source[at] === "(") { at++; value = sum(); if (source[at++] !== ")") throw Error("Thiếu dấu ngoặc"); }
    else { const match = source.slice(at).match(/^(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?/i); if (!match) throw Error("Thiếu số"); at += match[0].length; value = Number(match[0]); }
    let percent = false;
    if (source[at] === "%") { at++; value /= 100; percent = true; }
    return { value: value * sign, percent };
  }
  function product() {
    let result = number();
    while (source[at] === "*" || source[at] === "/") { const op = source[at++], next = number(); result = {value: op === "*" ? result.value * next.value : result.value / next.value, percent: false}; }
    return result;
  }
  function sum() {
    let value = product().value;
    while (source[at] === "+" || source[at] === "-") { const op = source[at++], next = product(), operand = next.percent ? value * next.value : next.value; value += op === "+" ? operand : -operand; }
    return value;
  }
  const result = sum();
  if (at !== source.length || !Number.isFinite(result)) throw Error("Không thể tính");
  return Number(result.toPrecision(12));
}
export class Calculator {
  expression = ""; history = ""; result = false; error = false;
  press(key) {
    key = normalized(key);
    if (key === "AC") { this.expression = this.history = ""; this.result = this.error = false; return false; }
    if (key === "=") {
      try { const value = calculate(this.expression || "0"); this.history = this.expression; this.expression = String(value); this.result = true; this.error = false; }
      catch { this.error = true; }
      return false;
    }
    if (this.error || this.result && /[\d.()]/.test(key)) this.press("AC");
    this.result = false;
    if (key === "backspace") { this.expression = this.expression.slice(0, -1); return false; }
    if (key === "±") {
      const number = /(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?%?/i.source;
      const negative = new RegExp(`\\(-(${number})\\)$`, "i"), trailing = new RegExp(`(${number})$`, "i");
      if (negative.test(this.expression)) this.expression = this.expression.replace(negative, "$1");
      else this.expression = this.expression.replace(trailing, "(-$1)");
      return false;
    }
    if (!/^[\d.+*/()%\-]$/.test(key) || this.expression.length >= 80) return false;
    if (/^[+*/\-]$/.test(key)) {
      if (!this.expression) { if (key === "-") this.expression = "-"; return false; }
      if (/[+*/\-]$/.test(this.expression) && !(key === "-" && /[*\/]$/.test(this.expression))) this.expression = this.expression.slice(0, -1);
    }
    if (key === ".") {
      if ((this.expression.match(/[\d.]*$/)?.[0] || "").includes(".")) return false;
      if (!/\d$/.test(this.expression)) this.expression += "0";
    }
    this.expression += key;
    return false;
  }
  get display() { return this.error ? "Lỗi" : (this.expression || "0").replace(/\*/g, "×").replace(/\//g, "÷").replace(/-/g, "−"); }
}
