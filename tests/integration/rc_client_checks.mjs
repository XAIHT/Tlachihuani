// Tlachihuani — Angela López Mendoza · XAIHT.
// Phone client checks without a browser or servers. The canonical
// Tlamatini-RC/public client and its packaged mirror must stay identical, and
// the hosted transport must keep one bad reply, a busy link or a cloud refusal
// from turning into a lost connection or a misleading message.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import {pathToFileURL} from 'node:url';
import {root} from './rc_installed_driver.mjs';

const mirror=path.join(root,'src/Tlachihuani.RC.Server/wwwroot');
const canonical=process.env.TL_RC_CANONICAL_PUBLIC||path.resolve(root,'../../Tlamatini-RC/public');
const out=process.env.TL_RC_HOSTED_TEST_OUTPUT||path.join(root,'artifacts/rc-client-checks',new Date().toISOString().replace(/[:.]/g,'-'));
await fs.mkdir(out,{recursive:true});
const checks=[];
async function test(id,name,action){
  console.log('RUN '+id+' '+name);
  try{await action();checks.push({id,name,status:'PASS'});console.log('PASS '+id);}
  catch(error){checks.push({id,name,status:'FAIL',error:error.stack});console.error('FAIL '+id+' '+error.stack);}
}
async function files(directory,prefix=''){
  const list=[];
  for(const entry of await fs.readdir(directory,{withFileTypes:true})){
    const relative=prefix+entry.name;
    if(entry.isDirectory())list.push(...await files(path.join(directory,entry.name),relative+'/'));else list.push(relative);
  }
  return list;
}

// ------------------------------------------------------------- source checks
let canonicalPresent=true;try{await fs.access(canonical);}catch{canonicalPresent=false;}
if(canonicalPresent){
  await test('CLIENT01','Every packaged phone file is byte-identical to its canonical Tlamatini-RC copy',async()=>{
    const differences=[];
    for(const relative of await files(mirror)){
      let original;try{original=await fs.readFile(path.join(canonical,relative));}catch{differences.push(relative+' (missing from canonical)');continue;}
      if(!original.equals(await fs.readFile(path.join(mirror,relative))))differences.push(relative);
    }
    assert.deepEqual(differences,[],'Change both copies together: '+differences.join(', '));
  });
}else checks.push({id:'CLIENT01',name:'Canonical copy parity',status:'SKIPPED',reason:'Canonical Tlamatini-RC/public folder not found at '+canonical});
await test('CLIENT02','Step by step keeps Multiturn, Self-Modify stays untouched until changed, receipt errors are readable',async()=>{
  const source=await fs.readFile(path.join(mirror,'remote.js'),'utf8');
  assert.match(source,/const needsMulti=\[[^\]]*"step_by_step_enabled"[^\]]*\]/);
  assert.match(source,/e\.dataset\.mode!=="self_modify_enabled"\|\|selfModifyTouched/);
  assert.match(source,/if\(value&&!selfModifyTouched\)/);
  assert.match(source,/textContent=receiptError\(receipt\)/);
  assert.match(source,/multiturn_required:\[/);
  // A request's 10-second reply clock starts only once it is sent, not while it waits.
  assert.match(source,/let timeout=null;const sent=\(\)=>\{if\(timeout===null\)timeout=setTimeout\(\(\)=>controller\.abort\(\),10000\);\};/);
  assert.match(source,/signal:controller\.signal,onSent:sent\}\);/);
});
await test('CLIENT03','The offline shell lists every phone module the page imports',async()=>{
  const shell=await fs.readFile(path.join(mirror,'remote-sw.js'),'utf8');
  for(const name of ['remote.js','remote-hosted.js']){
    const source=await fs.readFile(path.join(mirror,name),'utf8');
    for(const [,module] of source.matchAll(/from '\.\/([^']+)'/g))assert.ok(shell.includes("'/"+module+"'"),module+' is missing from the offline shell');
  }
});
await test('CLIENT04','Every connection message has a Spanish translation',async()=>{
  const source=await fs.readFile(path.join(mirror,'remote-transport.js'),'utf8'),dictionary=await fs.readFile(path.join(mirror,'remote-i18n.js'),'utf8');
  const messages=new Set();
  for(const name of ['cloudMessages','relayMessages']){const match=source.match(new RegExp('const '+name+'=(\\{.*?\\});'));assert.ok(match,name+' table');for(const text of Object.values(JSON.parse(match[1])))messages.add(text);}
  for(const [,text] of source.matchAll(/fail\('[a-z_]+','([^']+)'/g))messages.add(text);
  const missing=[...messages].filter(text=>!dictionary.includes("'"+text+"':")&&!dictionary.includes(JSON.stringify(text)+':'));
  assert.deepEqual(missing,[]);
});

// ---------------------------------------------------------- transport checks
for(const language of ['en','es']){
  await test('CLIENT-UNCERTAIN-'+language,'Unknown outcomes always require verification before another request ('+language+')',async()=>{
    const source=await fs.readFile(path.join(mirror,'remote.js'),'utf8');
    const start=source.indexOf('const receiptErrors='),end=source.indexOf('const activeDownloads=',start);
    assert.ok(start>=0&&end>start,'Actual client receipt formatter is present');
    for(const error of ['account_disabled','authorization_changed','application_unavailable','permissions_changed','adapter_disconnected','host_restarted','']){
      const receipt={state:'outcome_unknown',error};
      const text=vm.runInNewContext(source.slice(start,end)+';receiptError(receipt)',{receipt,responseText:(en,es)=>language==='es'?es:en});
      assert.equal(text,language==='es'?'No se conoce el resultado de esta solicitud. Revisa Tlamatini antes de enviarla de nuevo.':'The result of this request is unknown. Check Tlamatini before sending it again.',error);
    }
    const text=vm.runInNewContext(source.slice(start,end)+';receiptError(receipt)',{receipt:{state:'cancelled',error:'account_disabled'},responseText:(en,es)=>language==='es'?es:en});
    assert.ok(text.includes(language==='es'?'antes de ejecutar':'before this request ran'),'Queued cancellation retains the confirmed non-execution explanation');
  });
}

const ORIGIN='https://127.0.0.1:12491',INSTALL='a'.repeat(32),BINDING='b'.repeat(32),KEY='k'.repeat(124);
const sockets=[];
class FakeSocket{
  static CONNECTING=0;static OPEN=1;static CLOSING=2;static CLOSED=3;
  constructor(url,protocol){this.url=url;this.protocol=protocol;this.readyState=0;this.sent=[];this.closed=false;sockets.push(this);setTimeout(()=>{this.readyState=1;this.onopen?.();},0);}
  send(text){const frame=JSON.parse(text);this.sent.push(frame);if(frame.ticket)setTimeout(()=>this.receive({type:'ready',binding_id:BINDING,profile:'development-loopback'}),0);}
  receive(frame){this.onmessage?.({data:JSON.stringify(frame)});}
  close(){this.closed=true;this.readyState=3;}
}
const routes=new Map();
const json=(status,value)=>new Response(value===undefined?'':JSON.stringify(value),{status,headers:{'Content-Type':'application/json'}});
Object.defineProperty(globalThis,'location',{value:{protocol:'https:',hostname:'127.0.0.1',origin:ORIGIN},configurable:true,writable:true});
Object.defineProperty(globalThis,'WebSocket',{value:FakeSocket,configurable:true,writable:true});
Object.defineProperty(globalThis,'fetch',{value:async(url,init={})=>{const key=(init.method||'GET')+' '+String(url);const handler=routes.get(key);if(!handler)throw new TypeError('Unexpected test request '+key);return handler(init);},configurable:true,writable:true});
routes.set('GET /api/hosted/bootstrap',()=>json(200,{mode:'hosted',profile:'development-loopback',post_quantum:false,public_remote_enabled:false,authentication_mode:'fixture',csrf_token:'c'.repeat(32),user:{id:'u'.repeat(32),username:'QA'}}));
// The module has no imports; an .mjs copy loads it as the browser does.
const copy=path.join(out,'remote-transport.mjs');await fs.copyFile(path.join(mirror,'remote-transport.js'),copy);
const {transport}=await import(pathToFileURL(copy).href);
const requests=socket=>socket.sent.filter(frame=>frame.type==='request');
const finish=(socket,frame,status=204)=>{socket.receive({type:'response',id:frame.id,status,headers:{}});socket.receive({type:'end',id:frame.id});};
const turn=()=>new Promise(resolve=>setTimeout(resolve,0));
let lost=0,socket;
try{
  await transport.initialize();transport.onLost(()=>lost++);
  await test('CLIENT10','A wrong test password is named as such, not as an unavailable service',async()=>{
    routes.set('POST /api/hosted/login',()=>json(401,{error:{code:'invalid_credentials'}}));
    await assert.rejects(()=>transport.login('QA','wrong'),error=>error.code==='invalid_credentials'&&error.message==='Sign-in failed. Check the test username and password.');
  });
  await test('CLIENT11','A rate-limited reply without a body asks the person to wait',async()=>{
    routes.set('POST /api/hosted/login',()=>new Response('',{status:429}));
    await assert.rejects(()=>transport.login('QA','wrong'),error=>error.code==='rate_limited'&&error.message==='Too many attempts. Wait a minute and try again.');
  });
  await test('CLIENT12','An unknown or inherited code keeps the generic message',async()=>{
    routes.set('POST /api/hosted/login',()=>json(400,{error:{code:'constructor'}}));
    await assert.rejects(()=>transport.login('QA','wrong'),error=>error.message==='The connection service is unavailable.');
  });
  const computer={installation_id:INSTALL,name:'QA PC',online:true,accounts:[{binding_id:BINDING,edition:'english',display_name:'QA'}]};
  transport.select(computer,computer.accounts[0]);
  await test('CLIENT13','An offline computer says to connect Remote-Controller on it',async()=>{
    routes.set('POST /api/hosted/connections',()=>json(409,{error:{code:'computer_offline'}}));
    await assert.rejects(()=>transport.open(KEY),error=>error.code==='computer_offline'&&error.message.startsWith('This computer is offline.'));
  });
  routes.set('POST /api/hosted/connections',()=>json(201,{ticket:'t'.repeat(40)}));
  await transport.open(KEY);socket=sockets.at(-1);
  await test('CLIENT14','One oversized reply fails only its own request; the link and other requests continue',async()=>{
    const big=transport.request('/response-content?ticket=big'),small=transport.request('/session');
    const [a,b]=requests(socket).slice(-2);
    socket.receive({type:'response',id:a.id,status:200,headers:{'content-length':String(9*1024*1024)}});
    await assert.rejects(big,error=>error.code==='response_too_large');
    assert.ok(socket.sent.some(frame=>frame.type==='cancel'&&frame.id===a.id),'The computer is told to stop that reply');
    socket.receive({type:'response',id:b.id,status:200,headers:{'content-type':'application/json','content-length':'11'}});
    socket.receive({type:'chunk',id:b.id,data:Buffer.from('{"ok":true}').toString('base64')});
    socket.receive({type:'end',id:b.id});
    assert.deepEqual(await (await small).json(),{ok:true});
    assert.equal(lost,0,'The link was not dropped');assert.equal(socket.closed,false);
  });
  await test('CLIENT15','A fifth request waits for a free slot instead of failing',async()=>{
    const before=requests(socket).length;
    const calls=[1,2,3,4,5].map(n=>transport.request('/operations/'+String(n).repeat(32)));
    await turn();let sent=requests(socket).slice(before);
    assert.equal(sent.length,4,'Only four requests travel at once');
    finish(socket,sent[0]);await turn();sent=requests(socket).slice(before);
    assert.equal(sent.length,5,'The waiting request leaves when a slot frees');
    for(const frame of sent.slice(1))finish(socket,frame);
    assert.ok((await Promise.all(calls)).every(response=>response.status===204));
  });
  await test('CLIENT16','A waiting request can be cancelled and is never sent',async()=>{
    const before=requests(socket).length;
    const held=[6,7,8,9].map(n=>transport.request('/operations/'+String(n).repeat(32)));
    const controller=new AbortController(),queued=transport.request('/session',{signal:controller.signal});
    controller.abort();await assert.rejects(queued,error=>error.name==='AbortError');
    const sent=requests(socket).slice(before);assert.equal(sent.length,4);
    for(const frame of sent)finish(socket,frame);
    await Promise.all(held);await turn();
    assert.equal(requests(socket).slice(before).length,4,'The cancelled request never left the phone');
  });
  await test('CLIENT17','A busy computer says so in plain words',async()=>{
    const call=transport.request('/session'),frame=requests(socket).at(-1);
    socket.receive({type:'error',id:frame.id,code:'request_limit',status:429});
    await assert.rejects(call,error=>error.code==='request_limit'&&error.message==='Too many requests are in progress. Try again.');
    assert.equal(lost,0);
  });
  await test('CLIENT19','A request starts its reply clock only when it is actually sent',async()=>{
    const before=requests(socket).length,started=[];let queuedStarted=false;
    const held=[1,2,3,4].map(n=>transport.request('/operations/'+String(n).repeat(31)+'e',{onSent:()=>started.push(n)}));
    const queued=transport.request('/session',{onSent:()=>{queuedStarted=true;}});
    assert.deepEqual(started,[1,2,3,4],'Requests that leave at once start their clock at once');
    assert.equal(queuedStarted,false,'A request still waiting for a slot has not started its clock');
    finish(socket,requests(socket).slice(before)[0]);await turn();
    assert.equal(queuedStarted,true,'The clock starts when the waiting request leaves');
    for(const frame of requests(socket).slice(before+1))finish(socket,frame);
    assert.ok((await Promise.all([...held,queued])).every(response=>response.status===204));
  });
  await test('CLIENT18','Changing computers also ends requests that are waiting',async()=>{
    const held=[1,2,3,4].map(n=>transport.request('/operations/'+String(n).repeat(31)+'f'));
    const queued=transport.request('/session');
    transport.close();
    await assert.rejects(queued,error=>error.code==='selection_changed');
    for(const result of await Promise.allSettled(held))assert.equal(result.status,'rejected');
  });
}catch(error){checks.push({id:'HARNESS',name:'Client check setup',status:'FAIL',error:error.stack});console.error(error.stack);}
finally{try{transport.close();}catch{}}

const failed=checks.filter(c=>c.status==='FAIL').length,passed=checks.filter(c=>c.status==='PASS').length;
await fs.writeFile(path.join(out,'results.json'),JSON.stringify({scope:'Phone client source and transport checks; no browser, server or network',passed,failed,checks},null,2));
await fs.writeFile(path.join(out,'cleanup.json'),JSON.stringify({no_processes_started:true},null,2));
console.log('VERDICT: '+(failed?'FAIL':'PASS')+' - '+passed+' passed, '+failed+' failed');
process.exitCode=failed?1:0;
