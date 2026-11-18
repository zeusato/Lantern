import { Calculator, calculate, normalized } from "./calculator.js";
import { b64, un64, random, utf8 } from "./crypto.js";

const STORAGE_KEY = "lantern-calculator-lock-v1";
const canonical = value => normalized(value).replace(/,/g, ".").replace(/=$/, "");

export function validateCalculatorExpression(value) {
  const expression = canonical(value);
  if (expression.length > 80 || (expression.match(/\d/g) || []).length < 6 ||
      !/\d[+*/\-]/.test(expression) || /[^\d.+*/\-]/.test(expression))
    throw Error("Dùng ít nhất 6 chữ số và một phép cộng, trừ, nhân hoặc chia (tối đa 80 ký tự).");
  try { calculate(expression); }
  catch { throw Error("Phép tính chưa hợp lệ. Hãy kiểm tra lại."); }
  // Only accept expressions that can be entered on the actual mobile keypad.
  const calculator = new Calculator();
  for (const key of expression) calculator.press(key);
  if (calculator.expression !== expression)
    throw Error("Dùng phép tính gõ được bằng các phím số và + − × ÷.");
  return expression;
}

function validRecord(value) {
  try { return value?.version === 1 && un64(value.salt).length === 16 && un64(value.hash).length === 32; }
  catch { return false; }
}
function readRecord() {
  const raw = localStorage.getItem(STORAGE_KEY);
  if (!raw) return null;
  try { const record = JSON.parse(raw); return validRecord(record) ? record : null; }
  catch { return null; }
}
async function digest(expression, salt) {
  const key = await crypto.subtle.importKey("raw", utf8(expression), "PBKDF2", false, ["deriveBits"]);
  return new Uint8Array(await crypto.subtle.deriveBits(
    {name: "PBKDF2", hash: "SHA-256", salt, iterations: 310000}, key, 256,
  ));
}
export const hasCalculatorLock = () => !!readRecord();
export async function createCalculatorLock(value) {
  const expression = validateCalculatorExpression(value), salt = random(16);
  return {version: 1, salt: b64(salt), hash: b64(await digest(expression, salt))};
}
export function saveCalculatorLock(record) {
  if (!validRecord(record)) throw Error("Không lưu được phép tính. Hãy thử lại.");
  localStorage.setItem(STORAGE_KEY, JSON.stringify(record));
}
export async function matchesCalculatorLock(value) {
  try {
    const record = readRecord();
    if (!record) return false;
    const hash = await digest(canonical(value), un64(record.salt)), expected = un64(record.hash);
    let difference = 0;
    for (let i = 0; i < hash.length; i++) difference |= hash[i] ^ expected[i];
    // A concurrent change in another tab must invalidate the old expression immediately.
    return difference === 0 && JSON.stringify(readRecord()) === JSON.stringify(record);
  } catch { return false; }
}
