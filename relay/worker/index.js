import {webAssets} from './web-assets.js';
const ID=/^[a-f0-9]{32}$/;const KEY=/^[A-Za-z0-9_-]{43}$/;const MAX_BODY=90000;
const json=(value,status=200)=>new Response(JSON.stringify(value),{status,headers:{'content-type':'application/json','cache-control':'no-store','x-content-type-options':'nosniff'}});
const hash=async value=>Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(value))),x=>x.toString(16).padStart(2,'0')).join('');
async function canRegister(env,body,host,now){
 const proof=body.registration;
 if(!env.LANTERN_REGISTRATION_KEY||!proof||!Number.isSafeInteger(proof.issuedAt)||Math.abs(now-proof.issuedAt)>300||!/^[a-f0-9]{64}$/.test(proof.signature??''))return false;
 const key=await crypto.subtle.importKey('raw',new TextEncoder().encode(env.LANTERN_REGISTRATION_KEY),{name:'HMAC',hash:'SHA-256'},false,['verify']);
 const bytes=Uint8Array.from(proof.signature.match(/../g),x=>parseInt(x,16));
 return crypto.subtle.verify('HMAC',key,bytes,new TextEncoder().encode(['Lantern-route-v1',body.id,host,body.clientKey,String(proof.issuedAt)].join('\n')));
}
// Anonymous installers self-enroll with independent random capabilities. This grants
// only a bounded set of opaque relay routes, never access to another PC or its chats.
async function enroll(request,env,keyHash,now){
 const old=await env.DB.prepare('SELECT key_hash FROM relay_installations WHERE key_hash=?').bind(keyHash).first();
 if(old){await env.DB.prepare('UPDATE relay_installations SET last_seen=? WHERE key_hash=?').bind(now,keyHash).run();return json({ok:true});}
 const day=Math.floor(now/86400)*86400;
 // Only the edge-provided IP is used; never trust caller-supplied X-Forwarded-For.
 // The day-scoped digest avoids storing raw IP addresses. No IP means global quota only.
 const ip=request.headers.get('cf-connecting-ip');
 const source=ip?await hash([env.LANTERN_REGISTRATION_KEY??'',day,ip].join('\n')):'';
 const results=await env.DB.batch([
  env.DB.prepare('DELETE FROM relay_installations WHERE last_seen<? AND NOT EXISTS(SELECT 1 FROM relay_routes WHERE installation_hash=key_hash AND expires>?)').bind(now-7776000,now),
  env.DB.prepare('INSERT OR IGNORE INTO relay_installations(key_hash,created,last_seen,source_hash) SELECT ?,?,?,? WHERE (SELECT COUNT(*) FROM relay_installations)<2000 AND (SELECT COUNT(*) FROM relay_installations WHERE created>=?)<500 AND (?=\'\' OR (SELECT COUNT(*) FROM relay_installations WHERE source_hash=? AND created>=?)<100)').bind(keyHash,now,now,source,day,source,source,day),
  env.DB.prepare('SELECT key_hash FROM relay_installations WHERE key_hash=?').bind(keyHash)
 ]);
 return results[2].results?.length?json({ok:true}):json({error:'enrollment_limit'},429);
}
async function register(request,env,body,bearer,authHash,now){
 if(!ID.test(body.id??'')||!KEY.test(body.clientKey??''))return json({error:'invalid_route'},400);
 const old=await env.DB.prepare('SELECT * FROM relay_routes WHERE id=?').bind(body.id).first();
 if(old&&old.host_hash!==authHash)return json({error:'unauthorized'},401);
 let owner=old?.installation_hash??null;
 if(!old){
  const installation=request.headers.get('x-lantern-installation');
  if(installation){
   if(!KEY.test(installation))return json({error:'registration_denied'},403);
   owner=await hash(installation);
   const found=await env.DB.prepare('SELECT key_hash FROM relay_installations WHERE key_hash=?').bind(owner).first();
   if(!found)return json({error:'registration_denied'},403);
  }else if(!await canRegister(env,body,bearer,now))return json({error:'registration_denied'},403);
 }
 const day=Math.floor(now/86400),clientHash=await hash(body.clientKey);
 const statements=[env.DB.prepare('DELETE FROM relay_packets WHERE created<?').bind(now-120),env.DB.prepare('DELETE FROM relay_routes WHERE expires<?').bind(now)];
 if(owner&&!old)statements.push(env.DB.prepare('UPDATE relay_installations SET last_seen=?,route_day=?,route_count=CASE WHEN route_day=? THEN MIN(route_count+1,61) ELSE 1 END WHERE key_hash=?').bind(now,day,day,owner));
 // Quotas and insertion share one atomic statement; a concurrent request cannot
 // overwrite another host's route or allocate past its installation's route limit.
 const quota=owner&&!old?' AND (SELECT COUNT(*) FROM relay_routes WHERE installation_hash=? AND expires>?)<12 AND EXISTS(SELECT 1 FROM relay_installations WHERE key_hash=? AND route_day=? AND route_count<=60)':'';
 statements.push(env.DB.prepare('INSERT INTO relay_routes(id,host_hash,client_hash,expires,installation_hash) SELECT ?,?,?,?,? WHERE (EXISTS(SELECT 1 FROM relay_routes WHERE id=?) OR (SELECT COUNT(*) FROM relay_routes WHERE expires>?)<4096)'+quota+' ON CONFLICT(id) DO UPDATE SET client_hash=excluded.client_hash,expires=excluded.expires WHERE relay_routes.host_hash=excluded.host_hash').bind(body.id,authHash,clientHash,now+604800,owner,body.id,now,...(owner&&!old?[owner,now,owner,day]:[])));
 statements.push(env.DB.prepare('SELECT host_hash,client_hash FROM relay_routes WHERE id=?').bind(body.id));
 const results=await env.DB.batch(statements),saved=results.at(-1).results?.[0];
 if(!saved)return json({error:'route_limit'},429);
 if(saved.host_hash!==authHash||saved.client_hash!==clientHash)return json({error:'unauthorized'},401);
 return json({ok:true});
}
async function exchange(env,id,authHash,body,now){
 if(!ID.test(body.session??'')||!Number.isSafeInteger(body.after)||body.after<0||body.after>1e9)return json({error:'invalid_ack'},400);
 if(body.data&&(typeof body.data!=='string'||body.data.length>65536||!/^[A-Za-z0-9+/]+={0,2}$/.test(body.data)||body.data.length%4!==0||!Number.isSafeInteger(body.seq)||body.seq<1||body.seq>1e9))return json({error:'invalid_packet'},400);
 // Authenticate and exchange atomically in ONE database round trip. Every mutation and
 // packet read repeats the authorization predicate; invalid credentials cannot change queues.
 const auth='EXISTS(SELECT 1 FROM relay_routes r WHERE r.id=? AND r.session=? AND r.expires>? AND (r.host_hash=? OR r.client_hash=?))';
 const credentials=[id,body.session,now,authHash,authHash];
 const side="(SELECT CASE WHEN host_hash=? THEN 'host' ELSE 'client' END FROM relay_routes WHERE id=?)";
 const other="(SELECT CASE WHEN host_hash=? THEN 'client' ELSE 'host' END FROM relay_routes WHERE id=?)";
 const statements=[
  env.DB.prepare('SELECT session,expires,(host_hash=? OR client_hash=?) AS authorized FROM relay_routes WHERE id=?').bind(authHash,authHash,id),
  env.DB.prepare(`DELETE FROM relay_packets WHERE ${auth} AND route=? AND session=? AND side=${other} AND seq<=?`).bind(...credentials,id,body.session,authHash,id,body.after),
  env.DB.prepare(`DELETE FROM relay_packets WHERE ${auth} AND route=? AND created<?`).bind(...credentials,id,now-120)
 ];
 if(body.data){
  statements.push(env.DB.prepare(`INSERT OR IGNORE INTO relay_packets(route,session,side,seq,body,created) SELECT ?,?,${side},?,?,? WHERE ${auth} AND (SELECT COUNT(*) FROM relay_packets WHERE route=? AND session=? AND side=${side})<32`).bind(id,body.session,authHash,id,body.seq,body.data,now,...credentials,id,body.session,authHash,id));
  statements.push(env.DB.prepare(`SELECT body FROM relay_packets WHERE ${auth} AND route=? AND session=? AND side=${side} AND seq=?`).bind(...credentials,id,body.session,authHash,id,body.seq));
 }
 statements.push(env.DB.prepare(`SELECT seq,body AS data FROM relay_packets WHERE ${auth} AND route=? AND session=? AND side=${other} AND seq>? ORDER BY seq LIMIT ?`).bind(...credentials,id,body.session,authHash,id,body.after,body.pushOnly===true?0:body.batch===4?4:1));
 const results=await env.DB.batch(statements),route=results[0].results?.[0];
 if(!route||route.expires<=now)return json({error:'route_unavailable'},404);
 if(!route.authorized)return json({error:'unauthorized'},401);
 if(route.session!==body.session)return json({error:'session_changed'},409);
 if(body.data){const stored=results[4].results?.[0];if(!stored)return json({error:'backpressure'},429);if(stored.body!==body.data)return json({error:'conflicting_packet'},409);}
 return json({session:body.session,packets:results.at(-1).results});
}
export default {async fetch(request,env){
 try{
 const url=new URL(request.url);if(url.pathname==='/health')return json({service:'Lantern encrypted relay',protocol:1,web:1,enrollment:1});
 if(request.method==='GET'&&webAssets[url.pathname]){const asset=webAssets[url.pathname];const bytes=Uint8Array.from(atob(asset.data),c=>c.charCodeAt(0));return new Response(bytes,{headers:{'content-type':asset.type,'cache-control':url.pathname==='/sw.js'?'no-cache':'private, max-age=0, must-revalidate','x-content-type-options':'nosniff','referrer-policy':'no-referrer','permissions-policy':'camera=(self), microphone=(), geolocation=()','content-security-policy':"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data:; media-src 'self' blob:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'"}});}
 if(url.pathname==='/')return new Response('<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Lantern · Relay riêng</title><style>body{background:#101a21;color:#ebf2f5;font:18px system-ui;max-width:640px;margin:12vh auto;padding:28px}h1{color:#2daa9c}p{line-height:1.7;color:#a5b6c2}</style><h1>Lantern</h1><h2>Kênh kết nối thiết bị riêng</h2><p>Relay chuyển tiếp các gói dữ liệu mã hóa giữa PC và điện thoại đã ghép đôi. Mật khẩu, khóa riêng và lịch sử chat không được lưu ở đây.</p><p>Quản lý thiết bị và tạo mã QR trong ứng dụng Lantern trên PC.</p></html>',{headers:{'content-type':'text/html; charset=utf-8','cache-control':'no-store'}});
 const bearer=request.headers.get('authorization')?.replace(/^Bearer /,'')??'';if(!KEY.test(bearer))return json({error:'unauthorized'},401);
 if(Number(request.headers.get('content-length')??0)>MAX_BODY)return json({error:'too_large'},413);
 let body={};if(request.method!=='GET'){let raw='',size=0;const reader=request.body?.getReader(),decoder=new TextDecoder();if(reader){while(true){const chunk=await reader.read();if(chunk.done)break;size+=chunk.value.byteLength;if(size>MAX_BODY){await reader.cancel();return json({error:'too_large'},413)}raw+=decoder.decode(chunk.value,{stream:true})}raw+=decoder.decode()}try{body=JSON.parse(raw||'{}')}catch{return json({error:'invalid_json'},400)}}
 const now=Math.floor(Date.now()/1000);const authHash=await hash(bearer);
 if(url.pathname==='/v1/installations'&&request.method==='POST')return await enroll(request,env,authHash,now);
 if(url.pathname==='/v1/routes'&&request.method==='POST'){
   return await register(request,env,body,bearer,authHash,now);
 }
 const match=url.pathname.match(/^\/v1\/routes\/([a-f0-9]{32})\/(state|open|exchange|close|revoke)$/);if(!match)return json({error:'not_found'},404);
 const [,id,action]=match;if(action==='exchange'&&request.method==='POST')return await exchange(env,id,authHash,body,now);
 const route=await env.DB.prepare('SELECT * FROM relay_routes WHERE id=? AND expires>?').bind(id,now).first();if(!route)return json({error:'route_unavailable'},404);
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
 return json({error:'method'},405);
 }catch{return json({error:'relay_unavailable'},503)}
}};
