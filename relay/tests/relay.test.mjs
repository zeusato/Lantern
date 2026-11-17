import test from 'node:test';import assert from 'node:assert/strict';import {DatabaseSync} from 'node:sqlite';import {readFileSync} from 'node:fs';import worker from '../worker/index.js';
const db=new DatabaseSync(':memory:');db.exec(readFileSync(new URL('../drizzle/0000_relay.sql',import.meta.url),'utf8'));
db.exec(readFileSync(new URL('../drizzle/0001_installations.sql',import.meta.url),'utf8'));
const wrap=(sql,args=[])=>({bind(...values){return wrap(sql,values)},async first(){return db.prepare(sql).get(...args)??null},async all(){return {results:db.prepare(sql).all(...args)}},async run(){return /^SELECT/.test(sql)?{results:db.prepare(sql).all(...args)}:db.prepare(sql).run(...args)}});
const env={DB:{prepare:wrap,async batch(list){db.exec('BEGIN');try{const results=[];for(const stmt of list)results.push(await stmt.run());db.exec('COMMIT');return results}catch(e){db.exec('ROLLBACK');throw e}}}};
env.LANTERN_REGISTRATION_KEY='synthetic-registration-key-only';
const host='h'.repeat(43),client='c'.repeat(43),route='a'.repeat(32),session='b'.repeat(32);
async function proof(id,key,clientKey,issuedAt=Math.floor(Date.now()/1000)){const hmac=await crypto.subtle.importKey('raw',new TextEncoder().encode(env.LANTERN_REGISTRATION_KEY),{name:'HMAC',hash:'SHA-256'},false,['sign']);const signature=Array.from(new Uint8Array(await crypto.subtle.sign('HMAC',hmac,new TextEncoder().encode(['Lantern-route-v1',id,key,clientKey,String(issuedAt)].join('\n')))),x=>x.toString(16).padStart(2,'0')).join('');return {issuedAt,signature};}
async function call(path,key=host,body=null,sign=true){if(path==='/v1/routes'&&body&&sign)body={...body,registration:await proof(body.id,key,body.clientKey)};const request=new Request('https://relay.test'+path,{method:body?'POST':'GET',headers:{authorization:'Bearer '+key},...(body?{body:JSON.stringify(body)}:{})});const r=await worker.fetch(request,env);return {status:r.status,body:await r.json()}}
test('public visitors cannot create routes, forge registration or reuse stale proof',async()=>{
 const id='7'.repeat(32),body={id,clientKey:client};
 assert.equal((await call('/v1/routes',host,body,false)).status,403);
 assert.equal((await call('/v1/routes',host,{...body,registration:{issuedAt:Math.floor(Date.now()/1000),signature:'0'.repeat(64)}},false)).status,403);
 assert.equal((await call('/v1/routes',host,{...body,registration:await proof(id,host,client,1)},false)).status,403);
 const valid=await proof(id,host,client);
 assert.equal((await call('/v1/routes',host,{...body,clientKey:'k'.repeat(43),registration:valid},false)).status,403);
 assert.equal((await call('/v1/routes',host,{...body,registration:valid},false)).status,200);
 assert.equal((await call('/v1/routes',host,body,false)).status,200);
 await call('/v1/routes/'+id+'/revoke',host,{});
});
test('authenticated routes carry opaque packets, reject replay conflicts, separate roles and revoke',async()=>{
 assert.equal((await call('/v1/routes','bad',{id:route,clientKey:client})).status,401);
 assert.equal((await call('/v1/routes',host,{id:route,clientKey:client})).status,200);
 assert.equal((await call('/v1/routes','x'.repeat(43),{id:route,clientKey:client})).status,401);
 const prefix='/v1/routes/'+route;
 assert.equal((await call(prefix+'/state',client)).status,403);
 assert.equal((await call(prefix+'/open',client,{session})).status,200);
 assert.equal((await call(prefix+'/state')).body.session,session);
 let packet={session,after:0,seq:1,data:Buffer.from('encrypted TLS bytes').toString('base64')};
 assert.equal((await call(prefix+'/exchange',client,packet)).status,200);
 assert.equal((await call(prefix+'/exchange',client,packet)).status,200);
 assert.equal((await call(prefix+'/exchange',client,{...packet,data:'YQ=='})).status,409);
 let received=await call(prefix+'/exchange',host,{session,after:0});assert.deepEqual(received.body.packets,[{seq:1,data:packet.data}]);
 assert.equal((await call(prefix+'/exchange',host,{session,after:1})).body.packets.length,0);
 assert.equal((await call(prefix+'/exchange',client,{session,after:-1})).status,400);
 assert.equal((await call(prefix+'/exchange',client,{...packet,seq:2,data:'a'.repeat(65540)})).status,400);
 assert.equal((await call(prefix+'/revoke',client,{})).status,403);
 const next='d'.repeat(32);await call(prefix+'/open',client,{session:next});assert.equal((await call(prefix+'/exchange',host,{session,after:0})).status,409);
 assert.equal((await call(prefix+'/revoke',host,{})).status,200);assert.equal((await call(prefix+'/state')).status,404);
});
test('relay drops expired bytes and cannot read plaintext or private keys',async()=>{
 await call('/v1/routes',host,{id:route,clientKey:client});await call('/v1/routes/'+route+'/open',client,{session});
 db.prepare('INSERT INTO relay_packets VALUES(?,?,?,?,?,?)').run(route,session,'client',1,'YQ==',0);
 assert.equal((await call('/v1/routes/'+route+'/exchange',host,{session,after:0})).body.packets.length,0);
 assert.deepEqual(Object.keys(db.prepare('SELECT * FROM relay_routes').get()).sort(),['client_hash','expires','host_hash','id','installation_hash','session']);
});
async function automatic(path,key,body={},installation,ip='192.0.2.1'){
 const response=await worker.fetch(new Request('https://relay.test'+path,{method:'POST',headers:{authorization:'Bearer '+key,'cf-connecting-ip':ip,...(installation?{'x-lantern-installation':installation}:{})},body:JSON.stringify(body)}),env);
 return {status:response.status,body:await response.json()};
}
test('fresh installations enroll without a service credential, retry safely and own separate routes',async()=>{
 const first='A'.repeat(43),second='B'.repeat(43),id='1'.repeat(32),id2='2'.repeat(32);
 assert.equal((await automatic('/v1/routes',host,{id,clientKey:client},first)).status,403);
 assert.equal((await automatic('/v1/installations',first)).status,200);
 assert.equal((await automatic('/v1/installations',first)).status,200);
 assert.equal(db.prepare('SELECT COUNT(*) AS n FROM relay_installations').get().n,1);
 assert.equal((await automatic('/v1/installations',second)).status,200);
 assert.equal((await automatic('/v1/routes',host,{id,clientKey:client},first)).status,200);
 assert.equal((await automatic('/v1/routes','z'.repeat(43),{id,clientKey:client},second)).status,401);
 assert.equal((await automatic('/v1/routes','z'.repeat(43),{id:id2,clientKey:'y'.repeat(43)},second)).status,200);
 assert.equal((await call('/v1/routes/'+id+'/state','z'.repeat(43))).status,401);
 assert.equal((await automatic('/v1/routes',host,{id,clientKey:client})).status,200);
 const stored=JSON.stringify(db.prepare('SELECT * FROM relay_installations').all());
 assert.ok(!stored.includes(first)&&!stored.includes(second)&&!stored.includes('192.0.2.1'));
 await call('/v1/routes/'+id+'/revoke',host,{});await call('/v1/routes/'+id2+'/revoke','z'.repeat(43),{});
});
test('installation active-route and daily limits cannot block other PCs or existing renewals',async()=>{
 const installation='C'.repeat(43);await automatic('/v1/installations',installation);
 for(let n=0;n<12;n++)assert.equal((await automatic('/v1/routes',host,{id:(100+n).toString(16).padStart(32,'0'),clientKey:client},installation)).status,200);
 assert.equal((await automatic('/v1/routes',host,{id:'3'.repeat(32),clientKey:client},installation)).status,429);
 const digest=Buffer.from(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(installation))).toString('hex');
 db.prepare('UPDATE relay_installations SET route_count=60 WHERE key_hash=?').run(digest);
 assert.equal((await automatic('/v1/routes',host,{id:'3'.repeat(32),clientKey:client},installation)).status,429);
 assert.equal((await automatic('/v1/routes',host,{id:'3'.repeat(32),clientKey:client},installation)).status,429);
 assert.equal((await automatic('/v1/routes',host,{id:(100).toString(16).padStart(32,'0'),clientKey:client},installation)).status,200);
 assert.equal((await automatic('/v1/routes',host,{id:'4'.repeat(32),clientKey:client},'A'.repeat(43))).status,200);
 for(let n=0;n<12;n++)await call('/v1/routes/'+(100+n).toString(16).padStart(32,'0')+'/revoke',host,{});
 await call('/v1/routes/'+'4'.repeat(32)+'/revoke',host,{});
 db.prepare('UPDATE relay_installations SET route_day=0 WHERE key_hash=?').run(digest);
 assert.equal((await automatic('/v1/routes',host,{id:'3'.repeat(32),clientKey:client},installation)).status,200);
 await call('/v1/routes/'+'3'.repeat(32)+'/revoke',host,{});
});
test('enrollment quotas bound new registrations while allowing idempotent retry and another network',async()=>{
 for(let n=0;n<100;n++)assert.equal((await automatic('/v1/installations','Q'+n.toString().padStart(42,'0'),{},null,'192.0.2.2')).status,200);
 assert.equal((await automatic('/v1/installations','D'.repeat(43),{},null,'192.0.2.2')).status,429);
 assert.equal((await automatic('/v1/installations','Q'+'0'.repeat(42),{},null,'192.0.2.2')).status,200);
 assert.equal((await automatic('/v1/installations','D'.repeat(43),{},null,'192.0.2.3')).status,200);
 const now=Math.floor(Date.now()/1000),day=Math.floor(now/86400)*86400;
 for(let n=db.prepare('SELECT COUNT(*) AS n FROM relay_installations WHERE created>=?').get(day).n;n<500;n++)db.prepare('INSERT INTO relay_installations(key_hash,created,last_seen,source_hash) VALUES(?,?,?,?)').run('fixture-'+n,now,now,'fixture');
 assert.equal((await automatic('/v1/installations','E'.repeat(43),{},null,'192.0.2.4')).status,429);
 assert.equal((await automatic('/v1/installations','D'.repeat(43),{},null,'192.0.2.3')).status,200);
});
test('batched polling preserves order, bounded queues and acknowledgements under pressure',async()=>{
 await call('/v1/routes',host,{id:route,clientKey:client});await call('/v1/routes/'+route+'/open',client,{session:'e'.repeat(32)});const active='e'.repeat(32),path='/v1/routes/'+route+'/exchange';
 for(let seq=1;seq<=32;seq++)assert.equal((await call(path,client,{session:active,after:0,seq,data:'YQ==',pushOnly:true})).status,200);
 assert.equal((await call(path,client,{session:active,after:0,seq:33,data:'YQ=='})).status,429);
 assert.equal((await call(path,client,{session:active,after:0,seq:32,data:'YQ=='})).status,200);
 assert.equal((await call(path,client,{session:active,after:0,seq:32,data:'Yg=='})).status,409);
 const received=await call(path,host,{session:active,after:0,batch:4});assert.deepEqual(received.body.packets.map(p=>p.seq),[1,2,3,4]);
 assert.equal((await call(path,'z'.repeat(43),{session:active,after:32,seq:1,data:'Yg=='})).status,401);
 assert.equal((await call(path,host,{session:'f'.repeat(32),after:32,seq:1,data:'Yg=='})).status,409);
 assert.deepEqual((await call(path,host,{session:active,after:0,batch:4})).body.packets.map(p=>p.seq),[1,2,3,4]);
 const sent=await call(path,host,{session:active,after:4,seq:1,data:'Yg==',pushOnly:true});assert.equal(sent.body.packets.length,0);
 assert.equal((await call(path,client,{session:active,after:0,seq:33,data:'YQ=='})).status,200);
 assert.deepEqual((await call(path,host,{session:active,after:4,batch:4})).body.packets.map(p=>p.seq),[5,6,7,8]);await call('/v1/routes/'+route+'/revoke',host,{});
});
