// Opt-in live QA: fresh .NET PC self-enrollment + production PWA protocol, no admin credential.
import net from 'node:net';
import {readFile} from 'node:fs/promises';
import assert from 'node:assert/strict';
import {RelayTransport} from '../channel.js';
import {ByteReader,createIdentity,secureChannel,frame,utf8,uid} from '../crypto.js';
const metadata=JSON.parse(await readFile(process.argv[2],'utf8'));
const origin=new URL(metadata.qr.relay.url);
assert.equal(origin.protocol,'https:');assert.equal(metadata.qr.relay.token,'');
assert.ok(!JSON.stringify(metadata.qr).includes('installationKey'));
const identity=await createIdentity();
const fetcher=(url,options)=>fetch(new URL(url,origin),options);
async function command(value){
 const reader=new ByteReader(),socket=net.connect(metadata.port,'127.0.0.1');
 socket.on('data',b=>reader.push(b));socket.on('error',e=>reader.close(e));
 try{await new Promise((y,n)=>{socket.once('connect',y);socket.once('error',n)});socket.write(frame(utf8(JSON.stringify(value))));await reader.frame();}finally{socket.destroy();}
}
let transport,channel;
async function connect(nonce){
 transport=new RelayTransport(metadata.qr,fetcher);await transport.open();
 channel=await secureChannel(transport,identity,metadata.qr);
 await channel.write({type:'hello',name:'Automatic enrollment QA',nonce});
 assert.equal((await channel.read()).type,'welcome');
}
async function rpc(packet){const start=performance.now();await channel.write({...packet,id:packet.id||uid()});const reply=await channel.read();assert.equal(reply.type,'ok');console.log(JSON.stringify({stage:packet.type,milliseconds:Math.round(performance.now()-start)}));return reply;}
const deadline=setTimeout(()=>{transport?.close(Error('Live QA timeout'));process.exitCode=1;},90000);
try{
 const start=performance.now();await connect(metadata.qr.nonce);
 console.log(JSON.stringify({stage:'fresh-PC-pairing',milliseconds:Math.round(performance.now()-start)}));
 let history=await rpc({type:'sync',conversation:metadata.conversation});assert.ok(history.messages.length>=30);
 const message={type:'send',conversation:metadata.conversation,id:uid(),text:'Automatic relay QA '+uid()};
 const sent=await rpc(message),retry=await rpc(message);assert.equal(sent.messageId,retry.messageId);
 history=await rpc({type:'sync',conversation:metadata.conversation});assert.equal(history.messages.filter(m=>m.id===sent.messageId).length,1);
 const incoming='Incoming QA '+uid();await command({type:'fixture-message',text:incoming});
 history=await rpc({type:'sync',conversation:metadata.conversation});assert.ok(history.messages.some(m=>m.text===incoming));
 channel.close();await connect();await rpc({type:'sync',conversation:metadata.conversation});
 console.log('PASS: Fresh PC pairs, loads history, sends/receives, deduplicates and reconnects without service credentials.');
}finally{
 transport?.close();clearTimeout(deadline);await command({type:'fixture-revoke'});
}
