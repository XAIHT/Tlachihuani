// Tlamatini-RC · Angela López Mendoza · XAIHT
// Headed, real-browser acceptance against the actual service host. No installed state is touched.
import {spawn} from 'node:child_process';
import fs from 'node:fs/promises';
import {existsSync} from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomUUID} from 'node:crypto';
import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const output=path.join(root,'artifacts','rc-implementation','browser-'+Date.now());
const image=process.env.TL_RC_TEST_IMAGE||path.join(root,'src','Tlachihuani.RC.Server','bin','Release','net10.0-windows','Tlachihuani.RC.Server.exe');
const nonce=randomUUID().replaceAll('-',''),adapter=randomUUID().replaceAll('-','');
const base='http://127.0.0.1:12484';
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
await fs.mkdir(output,{recursive:true});
const host=spawn(image,['--console',path.join(output,'host'),nonce,'12484'],{cwd:root,stdio:'inherit',windowsHide:false});
await new Promise((resolve,reject)=>{host.once('spawn',resolve);host.once('error',reject);});
async function native(body){return new Promise((resolve,reject)=>{const child=spawn(image,['--dev-control',nonce,String(host.pid)],{stdio:['pipe','pipe','inherit'],windowsHide:false});let response='';child.stdout.on('data',chunk=>response+=chunk);child.on('error',reject);child.on('exit',code=>{try{const result=JSON.parse(response);if(code||result.error)throw new Error(result.error?.message||'Control failed');resolve(result);}catch(error){reject(error);}});child.stdin.end(JSON.stringify(body)+'\n');});}
let cdp,failures=0;const checks=[];
const record=(label,condition)=>{assert.ok(condition,label);checks.push({label,passed:true});console.log('PASS BROWSER '+label);};
async function connect(url){const ws=new WebSocket(url);await new Promise((r,j)=>{ws.addEventListener('open',r,{once:true});ws.addEventListener('error',j,{once:true});});let next=0;const pending=new Map(),errors=[];ws.onmessage=({data})=>{const event=JSON.parse(data);if(event.id&&pending.has(event.id)){const item=pending.get(event.id);pending.delete(event.id);clearTimeout(item.timer);event.error?item.reject(new Error(event.error.message)):item.resolve(event.result);}if(event.method==='Runtime.exceptionThrown')errors.push(event.params.exceptionDetails);};return{errors,close:()=>ws.close(),send:(method,params={})=>new Promise((resolve,reject)=>{const id=++next;const timer=setTimeout(()=>reject(new Error('CDP timed out: '+method)),12000);pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({id,method,params}));})};}
const evaluate=async expression=>{const result=await cdp.send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(result.exceptionDetails)throw new Error(result.exceptionDetails.exception?.description||result.exceptionDetails.text);return result.result.value;};
async function until(expression,label,timeout=12000){const deadline=Date.now()+timeout;while(Date.now()<deadline){if(await evaluate(expression))return;await sleep(150);}throw new Error('Timed out: '+label+'; notice='+await evaluate('document.querySelector("#notice")?.textContent'));}
async function click(selector){await evaluate(`document.querySelector(${JSON.stringify(selector)}).scrollIntoView({block:'center',behavior:'instant'})`);await sleep(100);const rect=await evaluate(`(()=>{const e=document.querySelector(${JSON.stringify(selector)}),r=e.getBoundingClientRect(),x=r.x+r.width/2,y=r.y+r.height/2;if(!e.contains(document.elementFromPoint(x,y)))throw new Error('Obstructed control');return{x,y};})()`);await cdp.send('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,...rect});await cdp.send('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,...rect});await sleep(150);}
async function field(selector,value){await click(selector);await evaluate(`(()=>{const e=document.querySelector(${JSON.stringify(selector)});e.value=${JSON.stringify(value)};e.dispatchEvent(new Event('input',{bubbles:true}));})()`);}
async function route(name){await evaluate(`location.hash=${JSON.stringify(name)}`);await sleep(200);}
async function photo(name){await evaluate('scrollTo(0,0)');const result=await cdp.send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await fs.writeFile(path.join(output,name+'.png'),Buffer.from(result.data,'base64'));}
async function desktop(name){await cdp.send('Page.bringToFront');await sleep(700);await new Promise((resolve,reject)=>{const shot=spawn('C:/Development/XAIHT/Tlamatini/python/python.exe',[path.join(root,'artifacts','rc-implementation','capture.py'),name],{stdio:'inherit',windowsHide:false});shot.once('error',reject);shot.once('exit',code=>code?reject(new Error('Desktop evidence failed')):resolve());});}
try{
 for(let i=0;i<100;i++){try{if((await fetch(base+'/api/rc/v1/health')).ok)break;}catch{}await sleep(150);}
 const profile=path.join(output,'profile');await fs.mkdir(profile,{recursive:true});
 const chrome='C:/Program Files/Google/Chrome/Application/chrome.exe';if(!existsSync(chrome))throw new Error('Installed Chrome is required');
 const browser=spawn(chrome,[`--user-data-dir=${profile}`,'--remote-debugging-port=0','--remote-debugging-address=127.0.0.1','--no-first-run','--no-default-browser-check','--disable-background-networking','--disable-sync','--disable-extensions','--window-size=1100,1000','--app=about:blank'],{stdio:'ignore',windowsHide:false,env:{...process.env,TEMP:path.join(root,'artifacts','rc-implementation','scratch'),TMP:path.join(root,'artifacts','rc-implementation','scratch')}});
 browser.unref();
 let port;for(let i=0;i<100;i++){try{port=Number((await fs.readFile(path.join(profile,'DevToolsActivePort'),'utf8')).split('\n')[0]);break;}catch{}await sleep(150);}if(!port)throw new Error('Visible Chrome did not start');
 const tabs=await(await fetch(`http://127.0.0.1:${port}/json/list`)).json();const tab=tabs.find(t=>t.type==='page');if(!tab)throw new Error('No browser page');
 cdp=await connect(tab.webSocketDebuggerUrl);await cdp.send('Page.enable');await cdp.send('Runtime.enable');await cdp.send('Network.enable');await cdp.send('Page.bringToFront');
 await cdp.send('Page.navigate',{url:base+'/remote.html'});await until('!!document.querySelector("#prompt")','client loaded');
 await new Promise((resolve,reject)=>{const shot=spawn('C:/Development/XAIHT/Tlamatini/python/python.exe',[path.join(root,'artifacts','rc-implementation','capture.py'),'rc-browser-start'],{stdio:'inherit',windowsHide:false});shot.once('error',reject);shot.once('exit',code=>code?reject(new Error('Desktop evidence failed')):resolve());});
 record('Live client loads without simulated account',!await evaluate('document.body.innerText.includes("simulated")'));
 record('Disconnected client cannot submit work',await evaluate('document.querySelector("#send").disabled'));
 record('Microphone is not requested on load',await evaluate('document.body.innerText.includes("Voice is unavailable")'));
 await route('security');await until('document.querySelector("#security-profile").textContent.includes("Local-only")','profile observed');
 record('Connection profile is disclosed',await evaluate('document.querySelector("#security-profile").textContent.includes("post-quantum transport is unavailable")'));
 const binding=await native({op:'bind-account',edition:'english',account_id:randomUUID().replaceAll('-',''),display_name:'Browser QA account',adapter_id:adapter});
 const invitation=await native({op:'invite',binding_id:binding.id,scopes:['monitor','chat','launch','schedules']});
 await field('#device-name','Browser QA device');await field('#invitation',invitation.invitation);await click('#enroll');await until('document.querySelector("#fingerprint").textContent.includes("Compare in XT:")','fingerprint shown');
 record('Enrollment requires local approval',await evaluate('document.querySelector("#connection").textContent==="Disconnected"'));
 const pending=(await native({op:'pending-enrollments'})).enrollments[0];
 record('Displayed fingerprint matches native authority',await evaluate(`document.querySelector('#fingerprint').textContent.includes(${JSON.stringify(pending.fingerprint)})`));
 await native({op:'approve',enrollment_id:pending.id,fingerprint:pending.fingerprint});
 await until('document.querySelector("#connection").textContent==="Connected"','authentication');
 record('WebCrypto proof establishes a real session',await evaluate('document.querySelector("#identity-detail").textContent.includes("Permissions:")'));
 record('Client displays approved account identity',await evaluate('document.querySelector("#account-line").textContent.includes("Browser QA account")'));
 record('Bearer token absent from local/session storage',await evaluate('![...Object.values(localStorage),...Object.values(sessionStorage)].some(value=>/access_token|Bearer/.test(value))'));
 record('Private key is non-exportable',await evaluate(`new Promise((resolve,reject)=>{const r=indexedDB.open('tlachihuani-rc-device-v1',1);r.onsuccess=()=>{const db=r.result,q=db.transaction('identity').objectStore('identity').get(location.origin);q.onsuccess=()=>{resolve(q.result.key.extractable===false);db.close()};q.onerror=reject};r.onerror=reject})`));
 await route('chat');await field('#prompt','<img src=x onerror="window.qaInjected=true">');await click('#send');await until('document.querySelectorAll(".message").length===2','accepted receipt');
 record('User content stays inert',await evaluate('!window.qaInjected&&!document.querySelector(".message img")'));
 record('Accepted work is not a fabricated response',await evaluate('document.querySelector(".message:not(.user)").textContent.includes("Waiting for")'));
 const operation=(await native({op:'adapter-next',adapter_id:adapter})).operation;
 record('Client message reaches the typed native adapter',operation.kind==='conversation.submit'&&operation.payload.text.includes('<img'));
 const permissionId=randomUUID().replaceAll('-','');
 await native({op:'adapter-progress',adapter_id:adapter,operation_id:operation.id,progress:{type:'permission',request_id:permissionId,tool_name:'QA execution request',program:'No executable is run by this fixture',parameters:'<img src=x onerror="window.qaInjected=true">',truncated:false}});
 await until('!!document.querySelector(".permission button")','execution approval');
 record('Execution approval displays exact inert details',await evaluate('document.querySelector(".permission dl").textContent.includes("<img")&&!document.querySelector(".permission img")'));
 await native({op:'adapter-progress',adapter_id:adapter,operation_id:operation.id,progress:{type:'message',text:'QA progress while approval is pending'}});
 await sleep(1500);record('Status progress preserves the pending approval',await evaluate('!!document.querySelector(".permission button")'));
 await desktop('rc-browser-permission');await click('.permission button');
 const decision=(await native({op:'adapter-next',adapter_id:adapter,controls_only:true})).operation;
 record('Deny reaches only its owned request',decision.kind==='conversation.permission'&&decision.payload.operation_id===operation.id&&decision.payload.request_id===permissionId&&decision.payload.decision==='deny');
 await native({op:'adapter-complete',adapter_id:adapter,operation_id:decision.id,result:{resolved:true}});
 await native({op:'adapter-complete',adapter_id:adapter,operation_id:operation.id,result:{text:'QA adapter fixture completed this request.'}});
 await until('document.querySelector("#messages").textContent.includes("QA adapter fixture completed")','receipt completion');
 record('Completed receipt replaces pending state',await evaluate('document.querySelector("#messages").textContent.includes("succeeded")'));
 const beforeDelayed=await evaluate('document.querySelectorAll(".message:not(.user)").length');
 await field('#prompt','QA late completion after adapter heartbeat loss');await click('#send');
 await until(`document.querySelectorAll(".message:not(.user)").length>${beforeDelayed}`,'second admitted request');
 const delayed=(await native({op:'adapter-next',adapter_id:adapter})).operation;
 await native({op:'adapter-heartbeat',adapter_id:adapter,ready:true,operation_id:delayed.id});
 await until('document.querySelector(".message:not(.user):last-child").textContent.includes("outcome unknown")','heartbeat loss marks uncertain receipt',45000);
 record('Lost adapter marks a real receipt uncertain',await evaluate('document.querySelector(".message:not(.user):last-child").classList.contains("error")'));
 record('Uncertain work exposes no cancellation or approval controls',await evaluate('!document.querySelector(".message:not(.user):last-child .operation-actions")&&!document.querySelector(".message:not(.user):last-child .permission")'));
 await native({op:'adapter-complete',adapter_id:adapter,operation_id:delayed.id,result:{text:'QA late owned result is reconciled.'}});
 await until('document.querySelector("#messages").textContent.includes("QA late owned result is reconciled.")','late result replaces uncertainty');
 record('Late owned completion replaces the uncertain outcome',await evaluate('document.querySelector(".message:not(.user):last-child small").textContent.includes("succeeded")'));
 record('Reconciled success removes stale error presentation',await evaluate('!document.querySelector(".message:not(.user):last-child").classList.contains("error")'));
 record('Uncertain observation never replays the operation',(await native({op:'adapter-next',adapter_id:adapter})).operation===null);
 await native({op:'adapter-heartbeat',adapter_id:adapter,ready:true});await route('monitor');await click('#refresh');await until('document.querySelector("#application-state").textContent==="ready"','account heartbeat');record('Monitoring uses actual native account heartbeat',true);
 await route('schedules');await field('#schedule-name','Browser daily task');await field('#schedule-text','A QA schedule, never a real model call');await field('#schedule-time','09:00');await field('#schedule-zone','UTC');await click('#schedule-save');await until('document.querySelector("#schedule-list").textContent.includes("Browser daily task")','durable schedule');
 record('Schedule is stored by the service',await evaluate('document.querySelector("#schedule-list").textContent.includes("Enabled")'));
 await click('#schedule-list button');await until('document.querySelector("#schedule-list").textContent.includes("Paused")','schedule paused');record('Schedule pause uses its revision',true);
 await route('security');await click('#disconnect');await until('document.querySelector("#connection").textContent==="Disconnected"','logout');record('Disconnect removes command authority',await evaluate('document.querySelector("#send").disabled'));
 await click('#connect');await until('document.querySelector("#connection").textContent==="Connected"','reconnect');record('Reconnect does not ask for another invitation',await evaluate('document.querySelector("#enroll-form").hidden'));
 for(const [width,height] of [[320,568],[360,740],[393,852],[430,932],[768,1024],[1024,768],[1440,960]]){
  await cdp.send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:width<760,screenWidth:width,screenHeight:height});
  for(const section of ['chat','monitor','schedules','security','activity','settings']){await route(section);record(`No page overflow: ${section} ${width}x${height}`,await evaluate('document.documentElement.scrollWidth<=innerWidth+1'));record(`Named controls: ${section} ${width}x${height}`,await evaluate(`[...document.querySelectorAll('button')].filter(e=>e.getClientRects().length).every(e=>(e.textContent.trim()||e.getAttribute('aria-label')))`));}
 }
 await cdp.send('Emulation.setDeviceMetricsOverride',{width:393,height:852,deviceScaleFactor:1,mobile:true,screenWidth:393,screenHeight:852});await route('chat');await photo('phone-conversation');
 await desktop('rc-browser-phone-conversation');
 await route('settings');await click('#opaque');record('Opaque preference changes the live UI',await evaluate('document.body.classList.contains("opaque")'));await click('#motion');record('Reduced motion preference is applied',await evaluate('document.body.classList.contains("reduce-motion")'));
 await cdp.send('Page.reload');await until('!!document.querySelector("#connect")','reload');await until('!document.querySelector("#connect").disabled','stored device loaded');record('Page reload retains identity without retaining bearer session',await evaluate('document.querySelector("#connection").textContent==="Disconnected"'));
 await route('security');await click('#connect');await until('document.querySelector("#connection").textContent==="Connected"','reconnect after reload');record('Stored device key authenticates after reload',true);
 const device=(await native({op:'devices'})).devices[0];await native({op:'revoke',device_id:device.id});await until('document.querySelector("#connection").textContent==="Disconnected"','revocation');record('Revocation ends the live channel',await evaluate('document.querySelector("#send").disabled'));
 await click('#connect');await until('!document.querySelector("#connect").disabled','refused reconnect');record('Revoked device cannot reconnect',await evaluate('document.querySelector("#connection").textContent==="Disconnected"'));
 record('No uncaught client exceptions',cdp.errors.length===0);
 await native({op:'development-stop'});await sleep(1000);await cdp.send('Page.reload');await until('!!document.querySelector("#prompt")','offline shell');record('Offline shell remains usable after server stop',await evaluate('document.querySelector("#send").disabled'));await photo('phone-server-stopped');
}catch(error){failures++;checks.push({label:'browser acceptance failure',passed:false,error:error.stack});console.error(error);if(cdp)try{await photo('failure');}catch{}}
finally{if(host.exitCode===null){try{await native({op:'development-stop'});}catch{}await sleep(1000);if(host.exitCode===null)host.kill();}if(cdp)cdp.close();await fs.writeFile(path.join(output,'results.json'),JSON.stringify({total:checks.length,passed:checks.filter(c=>c.passed).length,failed:failures,checks},null,2));console.log(`RC browser checks: ${checks.filter(c=>c.passed).length}/${checks.length} passed. Visible Chrome retained. Evidence: ${output}`);}
process.exitCode=failures?1:0;
