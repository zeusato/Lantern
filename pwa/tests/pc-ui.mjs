const { chromium } = await import(process.env.LANTERN_PLAYWRIGHT || "playwright");
import http from "node:http";
import { readFile } from "node:fs/promises";
import assert from "node:assert/strict";
const html = `<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/style.css"><body><dialog id="sheet"></dialog><script type="module">
import {openPcPanel} from '/pc.js';
window.calls=[];
const data=new TextEncoder().encode('Tài liệu thử nghiệm Lantern');
const hash=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',data)),b=>b.toString(16).padStart(2,'0')).join('').toUpperCase();
const client={ready:true,request:async p=>{window.calls.push(p.pc);const r=p.pc;let pc={};
if(r.action==='status')pc={power:true,ai:true,roots:[{id:'root',name:'Tài liệu'}]};
else if(r.action==='files.list')pc={entries:r.path?[{name:'giấy tờ.txt',directory:false,size:data.length}]:[{name:'Hồ sơ',directory:true}],more:false};
else if(r.action==='files.open')pc={ticket:'file',name:'giấy tờ.txt',size:data.length,hash};
else if(r.action==='files.read')pc={data:btoa(String.fromCharCode(...data)),offset:r.offset,size:data.length};
else if(r.action==='ai.inspect')pc={targets:[{id:'claude',app:'Claude',conversation:'Chat thử',state:'idle',version:'v1',detail:'Sẵn sàng nhận yêu cầu.'},{id:'openai',app:'ChatGPT',conversation:'Công việc',state:'busy',version:'v2',detail:'Đang làm việc.'}]};
else if(r.action==='prepare')pc={ticket:'command',expires:Date.now()+45000};
else if(r.action==='execute'||r.action==='result')pc={state:'done',message:'Đã thực hiện lệnh thử.'};
return {type:'pc',pc};}};
let clean;const sheet=document.querySelector('#sheet');
const esc=s=>String(s).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;');
window.openPanel=()=>openPcPanel({getClient:()=>client,available:()=>true,esc,cleanup:f=>clean=f,showSheet:(title,body)=>{clean?.();sheet.innerHTML='<div class="sheet-head"><h2>'+title+'</h2></div><div class="sheet-body">'+body+'</div>';if(!sheet.open)sheet.showModal();return sheet;}});
window.openPanel();
</script></body></html>`;
const server=http.createServer(async(req,res)=>{
 try{if(req.url==='/'){res.setHeader('Content-Type','text/html; charset=utf-8');res.end(html);return;}const allowed=['/style.css','/pc.js','/crypto.js'];if(!allowed.includes(req.url)){res.writeHead(404).end();return;}res.setHeader('Content-Type',req.url.endsWith('.css')?'text/css':'application/javascript');res.end(await readFile('pwa'+req.url));}catch(e){res.writeHead(500).end(String(e));}
});
await new Promise(r=>server.listen(0,'127.0.0.1',r));
const browser=await chromium.launch({channel:process.env.LANTERN_BROWSER_CHANNEL || 'chrome',headless:true});
try{
 const page=await browser.newPage({viewport:{width:390,height:844},deviceScaleFactor:1});const errors=[];page.on('pageerror',e=>errors.push(String(e)));
 await page.goto('http://127.0.0.1:'+server.address().port);await page.getByRole('button',{name:'Khóa Windows',exact:true}).waitFor();
 if(process.env.LANTERN_UI_SCREENSHOT)await page.screenshot({path:process.env.LANTERN_UI_SCREENSHOT});
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 await page.getByRole('button',{name:'📁 Tài liệu',exact:true}).click();await page.getByRole('button',{name:'📁 Hồ sơ',exact:true}).click();await page.getByRole('button',{name:/giấy tờ.txt/}).click();
 await page.getByRole('link',{name:'Lưu tệp',exact:true}).waitFor();assert.match(await page.locator('#pc-status').innerText(),/toàn vẹn/);
 await page.evaluate(()=>window.openPanel());await page.getByRole('button',{name:'AI · Hiện cửa sổ và kiểm tra'}).click();
 await page.getByRole('button',{name:'Gửi yêu cầu vào Claude'}).waitFor();assert.equal(await page.getByRole('button',{name:/Gửi yêu cầu vào/}).count(),1);
 await page.getByRole('button',{name:'Gửi yêu cầu vào Claude'}).click();await page.getByRole('textbox',{name:'Yêu cầu cho AI'}).fill('Tiếp tục công việc <test>');await page.getByRole('button',{name:'Gửi yêu cầu',exact:true}).click();
 await page.getByRole('button',{name:'Xác nhận',exact:true}).click();await page.getByRole('button',{name:'Kiểm tra kết quả lệnh'}).waitFor();
 const calls=await page.evaluate(()=>window.calls);assert.equal(calls.filter(c=>c.action==='execute').length,1);assert.equal(calls.find(c=>c.command==='ai.send').prompt,'Tiếp tục công việc <test>');assert.deepEqual(errors,[]);
 console.log('PASS PC UI: 390px layout, lazy tree, checksum download, busy AI disabled, escaped prompt and one-shot execution');
}finally{await browser.close();await new Promise(r=>server.close(r));}

