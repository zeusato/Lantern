import test from 'node:test';import assert from 'node:assert/strict';import {DatabaseSync} from 'node:sqlite';import {readFileSync} from 'node:fs';import worker from '../worker/index.js';
const db=new DatabaseSync(':memory:');db.exec(readFileSync(new URL('../drizzle/0000_relay.sql',import.meta.url),'utf8'));
const wrap=(sql,args=[])=>({bind(...values){return wrap(sql,values)},async first(){return db.prepare(sql).get(...args)??null},async all(){return {results:db.prepare(sql).all(...args)}},async run(){return /^SELECT/.test(sql)?{results:db.prepare(sql).all(...args)}:db.prepare(sql).run(...args)}});
const env={DB:{prepare:wrap,async batch(list){db.exec('BEGIN');try{const results=[];for(const stmt of list)results.push(await stmt.run());db.exec('COMMIT');return results}catch(e){db.exec('ROLLBACK');throw e}}}};
const host='h'.repeat(43),client='c'.repeat(43),route='a'.repeat(32),session='b'.repeat(32);
async function call(path,key=host,body=null){const request=new Request('https://relay.test'+path,{method:body?'POST':'GET',headers:{authorization:'Bearer '+key},...(body?{body:JSON.stringify(body)}:{})});const r=await worker.fetch(request,env);return {status:r.status,body:await r.json()}}
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
 assert.deepEqual(Object.keys(db.prepare('SELECT * FROM relay_routes').get()).sort(),['client_hash','expires','host_hash','id','session']);
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
