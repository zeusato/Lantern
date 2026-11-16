const ID=/^[a-f0-9]{32}$/;const KEY=/^[A-Za-z0-9_-]{43}$/;const MAX_BODY=90000;
const json=(value,status=200)=>new Response(JSON.stringify(value),{status,headers:{'content-type':'application/json','cache-control':'no-store','x-content-type-options':'nosniff'}});
const hash=async value=>Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(value))),x=>x.toString(16).padStart(2,'0')).join('');
export default {async fetch(request,env){
 try{
 const url=new URL(request.url);if(url.pathname==='/health')return json({service:'Lantern private encrypted relay',protocol:1});
 if(url.pathname==='/')return new Response('<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Lantern · Relay riêng</title><style>body{background:#101a21;color:#ebf2f5;font:18px system-ui;max-width:640px;margin:12vh auto;padding:28px}h1{color:#2daa9c}p{line-height:1.7;color:#a5b6c2}</style><h1>Lantern</h1><h2>Kênh kết nối thiết bị riêng</h2><p>Relay chuyển tiếp các gói dữ liệu mã hóa giữa PC và điện thoại đã ghép đôi. Mật khẩu, khóa riêng và lịch sử chat không được lưu ở đây.</p><p>Quản lý thiết bị và tạo mã QR trong ứng dụng Lantern trên PC.</p></html>',{headers:{'content-type':'text/html; charset=utf-8','cache-control':'no-store'}});
 const bearer=request.headers.get('authorization')?.replace(/^Bearer /,'')??'';if(!KEY.test(bearer))return json({error:'unauthorized'},401);
 if(Number(request.headers.get('content-length')??0)>MAX_BODY)return json({error:'too_large'},413);
 let body={};if(request.method!=='GET'){let raw='',size=0;const reader=request.body?.getReader(),decoder=new TextDecoder();if(reader){while(true){const chunk=await reader.read();if(chunk.done)break;size+=chunk.value.byteLength;if(size>MAX_BODY){await reader.cancel();return json({error:'too_large'},413)}raw+=decoder.decode(chunk.value,{stream:true})}raw+=decoder.decode()}try{body=JSON.parse(raw||'{}')}catch{return json({error:'invalid_json'},400)}}
 const now=Math.floor(Date.now()/1000);const authHash=await hash(bearer);
 if(url.pathname==='/v1/routes'&&request.method==='POST'){
   if(!ID.test(body.id??'')||!KEY.test(body.clientKey??''))return json({error:'invalid_route'},400);
   const old=await env.DB.prepare('SELECT * FROM relay_routes WHERE id=?').bind(body.id).first();
   if(old&&old.host_hash!==authHash)return json({error:'unauthorized'},401);
   if(!old){const count=await env.DB.prepare('SELECT COUNT(*) AS n FROM relay_routes WHERE expires>?').bind(now).first();if(count.n>=32)return json({error:'route_limit'},429)}
   await env.DB.batch([env.DB.prepare('DELETE FROM relay_packets WHERE created<?').bind(now-120),env.DB.prepare('DELETE FROM relay_routes WHERE expires<?').bind(now),env.DB.prepare('INSERT INTO relay_routes(id,host_hash,client_hash,expires) VALUES(?,?,?,?) ON CONFLICT(id) DO UPDATE SET client_hash=excluded.client_hash,expires=excluded.expires').bind(body.id,authHash,await hash(body.clientKey),now+604800)]);
   return json({ok:true});
 }
 const match=url.pathname.match(/^\/v1\/routes\/([a-f0-9]{32})\/(state|open|exchange|close|revoke)$/);if(!match)return json({error:'not_found'},404);
 const [,id,action]=match;const route=await env.DB.prepare('SELECT * FROM relay_routes WHERE id=? AND expires>?').bind(id,now).first();if(!route)return json({error:'route_unavailable'},404);
 const isHost=authHash===route.host_hash,isClient=authHash===route.client_hash;if(!isHost&&!isClient)return json({error:'unauthorized'},401);
 if(action==='state'&&request.method==='GET'){if(!isHost)return json({error:'forbidden'},403);return json({session:route.session})}
 if(request.method!=='POST')return json({error:'method'},405);
 if(action==='revoke'){if(!isHost)return json({error:'forbidden'},403);await env.DB.batch([env.DB.prepare('DELETE FROM relay_routes WHERE id=?').bind(id),env.DB.prepare('DELETE FROM relay_packets WHERE route=?').bind(id)]);return json({ok:true})}
 if(action==='open'){
   if(!isClient||!ID.test(body.session??''))return json({error:'invalid_session'},400);
   await env.DB.batch([env.DB.prepare('DELETE FROM relay_packets WHERE route=? AND session<>?').bind(id,body.session),env.DB.prepare('UPDATE relay_routes SET session=? WHERE id=?').bind(body.session,id)]);return json({session:body.session});
 }
 if(!ID.test(body.session??'')||body.session!==route.session)return json({error:'session_changed'},409);
 if(action==='close'){await env.DB.batch([env.DB.prepare("UPDATE relay_routes SET session='' WHERE id=? AND session=?").bind(id,body.session),env.DB.prepare('DELETE FROM relay_packets WHERE route=? AND session=?').bind(id,body.session)]);return json({ok:true})}
 const side=isHost?'host':'client',other=isHost?'client':'host';
 if(!Number.isSafeInteger(body.after)||body.after<0||body.after>1e9)return json({error:'invalid_ack'},400);
 // Apply acknowledgements even under backpressure, so simultaneous writers cannot deadlock.
 await env.DB.prepare('DELETE FROM relay_packets WHERE route=? AND session=? AND side=? AND seq<=?').bind(id,body.session,other,body.after).run();
 const statements=[];
 if(body.data){
   if(typeof body.data!=='string'||body.data.length>65536||!/^[A-Za-z0-9+/]+={0,2}$/.test(body.data)||body.data.length%4!==0||!Number.isSafeInteger(body.seq)||body.seq<1||body.seq>1e9)return json({error:'invalid_packet'},400);
   const existing=await env.DB.prepare('SELECT body FROM relay_packets WHERE route=? AND session=? AND side=? AND seq=?').bind(id,body.session,side,body.seq).first();if(existing&&existing.body!==body.data)return json({error:'conflicting_packet'},409);
   const pending=await env.DB.prepare('SELECT COUNT(*) AS n FROM relay_packets WHERE route=? AND session=? AND side=?').bind(id,body.session,side).first();if(pending.n>=32&&!existing)return json({error:'backpressure'},429);
   statements.push(env.DB.prepare('INSERT OR IGNORE INTO relay_packets(route,session,side,seq,body,created) SELECT ?,?,?,?,?,? WHERE EXISTS(SELECT 1 FROM relay_routes WHERE id=? AND session=?)').bind(id,body.session,side,body.seq,body.data,now,id,body.session));
 }
 statements.push(env.DB.prepare('DELETE FROM relay_packets WHERE route=? AND created<?').bind(id,now-120));await env.DB.batch(statements);
 const packets=await env.DB.prepare('SELECT seq,body AS data FROM relay_packets WHERE route=? AND session=? AND side=? AND seq>? ORDER BY seq LIMIT 1').bind(id,body.session,other,body.after).all();
 return json({session:body.session,packets:packets.results});
 }catch{return json({error:'relay_unavailable'},503)}
}};
