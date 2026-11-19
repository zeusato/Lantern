// Test-only fixture around the production app module. No hooks enter shipped assets.
import http from "node:http";
import { readFile, mkdir } from "node:fs/promises";
import { resolve } from "node:path";
import assert from "node:assert/strict";
const { chromium } = await import(process.env.LANTERN_PLAYWRIGHT || "playwright");
const root = resolve(import.meta.dirname, "..");
const stickerPack=JSON.parse(await readFile(resolve(root,"../src/Lantern.App/Assets/Stickers/zalo-10529/pack.json"),"utf8"));
const stickerFile=stickerPack.Stickers.find(x=>x.Id==="zalo-24525").File;
const stickerBytes=(await readFile(resolve(root,"../src/Lantern.App/Assets/Stickers/zalo-10529",stickerFile))).toString("base64");
const fixture = `
initializeCalculator();
vault = { key: null };
const qaId = 'a'.repeat(64), qaOther = 'c'.repeat(64);
state = { identity: { id: 'b'.repeat(64) }, name: 'Synthetic QA', settings: {}, conversations: [
  { id: qaId, name: 'Hội thoại thử', presence: 'online', captureProtected: true },
  { id: qaOther, name: 'Hội thoại khác', presence: 'online' }
], histories: { [qaId]: [{ id: '1'.repeat(32), conversationId: qaId, senderId: qaId, senderName: 'Người gửi thử', text: 'Nội dung vẫn đọc được', kind: 'text', time: new Date().toISOString() }] }, drafts: {}, boxes: {}, outbox: [] };
shell();
window.qa = {
  open: () => openConversation(qaId),
  capture: value => update({ type: 'ok', conversationSnapshot: true, conversations: state.conversations.map(c => c.id === qaId ? {...c, captureProtected: value} : c) }),
  lock: () => { contact(qaId).protected = true; sessionKeys.delete(qaId); renderMessages(); },
  unlock: () => { sessionKeys.set(qaId, {}); renderMessages(); },
  outbox: () => state.outbox,
  group: value => {contact(qaId).isGroup=value;lastRender='';renderMessages();},
  stickers: () => { const image=new Blob([un64('${stickerBytes}')],{type:'image/gif'});state.histories[qaId]=[false,false,true].map((outgoing,i)=>{const id=String(i+3).repeat(32);media.set(id,{url:URL.createObjectURL(image),blob:image,conversation:qaId});return {id,conversationId:qaId,senderId:outgoing?'b'.repeat(64):qaId,senderName:'Người gửi thử',kind:'image',fileName:'sticker-test.gif',sticker:{name:'Ngủ ngon'},outgoing,time:new Date().toISOString(),status:'received'};});lastRender='';renderMessages(true); },
  emoji: () => {state.histories[qaId][0].text='Chào :) ❤️👍';lastRender='';renderMessages();},
};
`;
const server = http.createServer(async (req, res) => {
  try {
    const name = new URL(req.url, "http://localhost").pathname;
    const path = resolve(root, "." + (name === "/" ? "/index.html" : name));
    if (!path.startsWith(root + "/") && !path.startsWith(root + "\\")) { res.writeHead(403).end(); return; }
    let data = await readFile(path);
    if (name === "/app.js") data = data.toString().replace("queueMicrotask(boot);", "") + fixture;
    res.setHeader("Content-Type", name.endsWith(".js") ? "text/javascript" : name.endsWith(".css") ? "text/css" : name.endsWith(".svg") ? "image/svg+xml" : name === "/" ? "text/html" : "application/octet-stream");
    res.end(data);
  } catch { res.writeHead(404).end(); }
});
await new Promise(r => server.listen(0, "127.0.0.1", r));
const browser = await chromium.launch({ channel: process.env.LANTERN_BROWSER_CHANNEL || "chrome", headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  const errors = []; page.on("pageerror", e => errors.push(String(e)));
  await page.goto("http://127.0.0.1:" + server.address().port);
  await page.waitForFunction(() => !!window.qa);
  await page.evaluate(() => qa.open());
  await page.locator("#timeline").getByText("Nội dung vẫn đọc được", { exact: true }).waitFor();
  assert.equal(await page.locator("#capture-note").isVisible(), true);
  assert.equal(await page.locator("#info").isEnabled(), true);
  await page.getByRole("textbox", { name: "Nhập tin nhắn" }).fill("Nháp được giữ");
  await page.evaluate(() => qa.capture(false));
  assert.equal(await page.locator("#capture-note").isVisible(), false);
  await page.evaluate(() => qa.capture(true));
  assert.equal(await page.locator("#draft").inputValue(), "Nháp được giữ");
  await page.getByRole("button", { name: "Quay lại", exact: true }).click();
  assert.equal(await page.locator("#list").isVisible(), true);
  await page.evaluate(() => qa.open());
  assert.equal(await page.locator("#draft").inputValue(), "Nháp được giữ");
  await page.getByRole("button", { name: "Gửi tin nhắn", exact: true }).click();
  await page.locator("#timeline").getByText("Nháp được giữ", { exact: true }).waitFor();
  assert.equal(await page.evaluate(() => qa.outbox().length), 1);
  await page.getByRole("button", { name: "Thông tin hội thoại", exact: true }).click();
  assert.equal(await page.locator("#sheet").isVisible(), true);
  await page.keyboard.press("Escape");
  await page.evaluate(() => qa.lock());
  assert.equal(await page.locator("#composer-area").isVisible(), false);
  await page.getByRole("heading", { name: "Hội thoại đang khóa" }).waitFor();
  await page.evaluate(() => qa.unlock());
  await page.locator("#timeline").getByText("Nội dung vẫn đọc được", { exact: true }).waitFor();
  assert.equal(await page.locator("#composer-area").isVisible(), true);
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  if (process.env.LANTERN_UI_SCREENSHOT) {
    await mkdir(resolve(process.env.LANTERN_UI_SCREENSHOT, ".."), { recursive: true });
    await page.screenshot({ path: process.env.LANTERN_UI_SCREENSHOT });
  }
  assert.equal(await page.locator('#timeline .sender').count(),0);
  await page.evaluate(()=>qa.group(true));
  assert.equal(await page.locator('#timeline .sender').innerText(),'Người gửi thử');
  await page.evaluate(()=>{qa.group(false);qa.emoji();});
  assert.equal(await page.locator('#timeline .body img.chat-emoji').count(),3);
  await page.waitForFunction(()=>[...document.querySelectorAll('#timeline .chat-emoji')].every(x=>x.complete&&x.naturalWidth>0));
  await page.locator('#draft').fill('abc def');
  await page.locator('#draft').evaluate(x=>x.setSelectionRange(3,3));
  await page.getByRole('button',{name:'Đính kèm',exact:true}).click();
  await page.locator('#choose-emoji').click();
  await page.locator('#emoji-search').fill('Mỉm cười');
  await page.locator('#emoji-grid button').filter({has:page.locator('img[alt=":)"]')}).first().click();
  assert.equal(await page.locator('#draft').inputValue(),'abc :) def');
  await page.evaluate(()=>{qa.group(true);qa.stickers();});
  await page.waitForFunction(()=>[...document.querySelectorAll('.chat-sticker')].length===3&&[...document.querySelectorAll('.chat-sticker')].every(x=>x.complete&&x.naturalWidth>0));
  assert.equal(await page.locator('#timeline .message-avatar').count(),1);
  assert.equal(await page.locator('#timeline .mine .message-avatar').count(),0);
  assert.equal(await page.locator('#timeline .sender').count(),1);
  assert.equal(await page.locator('#timeline .file-card, #timeline .file-action').count(),0);
  assert.ok((await page.locator('#timeline').innerText()).includes('sticker-test.gif')===false);
  assert.deepEqual(await page.locator('.sticker-message .bubble').first().evaluate(x=>[getComputedStyle(x).backgroundColor,getComputedStyle(x).borderWidth]),['rgba(0, 0, 0, 0)','0px']);
  assert.equal(await page.locator('.chat-sticker').first().evaluate(x=>x.getBoundingClientRect().width),160);
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
  if(process.env.LANTERN_UI_SCREENSHOT)await page.screenshot({path:process.env.LANTERN_UI_SCREENSHOT.replace('.png','-stickers.png')});
  assert.deepEqual(errors, []);
  console.log("PASS PWA chat UI: readable capture-protected chat, editable persistent draft, back/reopen, send, info, password lock/unlock redraw and 390px layout");
} finally { await browser.close(); await new Promise(r => server.close(r)); }
