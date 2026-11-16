const ID=/^[a-f0-9]{32}$/;const KEY=/^[A-Za-z0-9_-]{43}$/;const MAX_BODY=90000;
const json=(value,status=200)=>new Response(JSON.stringify(value),{status,headers:{'content-type':'application/json','cache-control':'no-store','x-content-type-options':'nosniff'}});
const hash=async value=>Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(value))),x=>x.toString(16).padStart(2,'0')).join('');
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
