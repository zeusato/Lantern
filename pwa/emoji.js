import { catalog, shortcodes } from './emoji-data.js';
export { catalog };
const tokens = new Map();
const codes = new Map(Object.entries(shortcodes).map(([key,value]) => [key.toLowerCase(),value]));
for (const item of catalog) for (const key of [item.Emoji, item.Shortcode, ...(item.Aliases || [])]) {
  for (const variant of [key, key.replace(/\uFE0F/g, '')]) if (!tokens.has(variant.toLowerCase())) tokens.set(variant.toLowerCase(), item);
}
for (const [key,value] of Object.entries({':-)':'🙂',':-d':'😃',';)':'😉',':-(':'🙁',':-p':'😛',':-o':'😮',":'(":'😢',':-|':'😐','<3':'❤️','</3':'💔'})) {
  if (!codes.has(key)) codes.set(key,value);
  if (!tokens.has(key) && tokens.has(value.toLowerCase())) tokens.set(key,tokens.get(value.toLowerCase()));
}
const quote = value => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const keys = [...tokens.keys()].sort((a,b) => b.length-a.length);
const ascii = keys.filter(x => /^[\x00-\x7f]+$/.test(x)).map(quote).join('|');
const unicode = keys.filter(x => !/^[\x00-\x7f]+$/.test(x)).map(quote).join('|');
const pattern = new RegExp('(```[\\s\\S]*?(?:```|$)|`[^`\\r\\n]*(?:`|$)|(?:https?://|www\\.|[a-z]:\\\\)\\S+)|((?<!\\S)(?:'+ascii+')(?=$|\\s|[!?,.])|(?:'+unicode+'))', 'giu');
export const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
export const emojiUrl = item => '/assets/emoji/' + item.File;
export function replaceEmoticons(value, trailing = true) {
  return value.replace(pattern, (match,literal,emoji,offset) => literal || (!trailing && offset+match.length===value.length) ? match : codes.get(match.toLowerCase()) || match);
}
export function emojiHtml(value) {
  value = String(value ?? ''); let output='', last=0;
  const alone = tokens.has(value.trim().toLowerCase());
  for (const match of value.matchAll(pattern)) {
    output += escapeHtml(value.slice(last,match.index));
    const item = match[2] && tokens.get(match[0].toLowerCase());
    output += item ? `<img class="chat-emoji${alone ? ' emoji-alone' : ''}" src="${emojiUrl(item)}" alt="${escapeHtml(match[0])}" title="${escapeHtml(item.Name)}" draggable="false">` : escapeHtml(match[0]);
    last = match.index+match[0].length;
  }
  return output + escapeHtml(value.slice(last));
}
export const emojiSearchKey = value => value.toLowerCase().replace(/đ/g,'d').normalize('NFD').replace(/\p{M}/gu,'');
export function insertEmoji(input, token) {
  const a=input.selectionStart??input.value.length, b=input.selectionEnd??a;
  if (/^[\x00-\x7f]+$/.test(token)) {
    if (a && !/\s/.test(input.value[a-1])) token=' '+token;
    if (b<input.value.length && !/[\s!?,.]/.test(input.value[b])) token+=' ';
  }
  input.setRangeText(token,a,b,'end');
  input.dispatchEvent(new Event('input',{bubbles:true}));
}
