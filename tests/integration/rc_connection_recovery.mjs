// Tlachihuani — Angela López Mendoza · XAIHT
// Focused headed-browser checks against a real isolated RC host. The account
// adapter is an explicit fixture; this is not installed/main-model acceptance.
import {spawn} from 'node:child_process';
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {root,launchBrowser,attachBrowser,poll} from './rc_installed_driver.mjs';

const out=path.join(root,'artifacts/rc-connection-recovery',String(Date.now()));
await fs.mkdir(out,{recursive:true});
const image=process.env.TL_RC_TEST_IMAGE||path.join(root,'src/Tlachihuani.RC.Server/bin/Release/net10.0-windows/Tlachihuani.RC.Server.exe');
const nonce=randomUUID().replaceAll('-',''),adapter=randomUUID().replaceAll('-','');
const base='http://127.0.0.1:12485';
const host=spawn(image,['--console',path.join(out,'host'),nonce,'12485'],{cwd:root,stdio:'inherit',windowsHide:false});
await new Promise((resolve,reject)=>{host.once('spawn',resolve);host.once('error',reject);});
const checks=[];let page,binding,heartbeat;
const check=(name,value)=>{assert.ok(value,name);checks.push({name,status:'PASS'});console.log('PASS recovery: '+name);};
async function native(body){return new Promise((resolve,reject)=>{
  const child=spawn(image,['--dev-control',nonce,String(host.pid)],{stdio:['pipe','pipe','inherit'],windowsHide:false});
  const timer=setTimeout(()=>{child.kill();reject(Error('Native control timed out: '+body.op));},15000);
  let output='';child.stdout.on('data',chunk=>output+=chunk);child.once('error',error=>{clearTimeout(timer);reject(error);});
  child.once('exit',code=>{clearTimeout(timer);try{const reply=JSON.parse(output);if(code||reply.error)throw Error(reply.error?.message||'Native control failed');resolve(reply);}catch(error){reject(error);}});
  child.stdin.on('error',()=>{});
  child.stdin.end(JSON.stringify(body)+'\n');
});}
const storedDevice=()=>page.evaluate(`new Promise((resolve,reject)=>{const r=indexedDB.open('tlachihuani-rc-device-v1',1);r.onerror=reject;r.onsuccess=()=>{const db=r.result,q=db.transaction('identity').objectStore('identity').get(location.origin);q.onsuccess=()=>{resolve(q.result?.deviceId||null);db.close()};q.onerror=reject}})`);
async function reloadClient(){
  // Wait for a new document, not a matching notice left in the old page.
  await page.evaluate('window.__rcRecoveryReload=true');
  await page.send('Page.reload');
  await page.until('!window.__rcRecoveryReload&&document.readyState==="complete"&&!!document.querySelector("#connect")','client document reloaded');
}
async function enroll(scopes,name){
  const invite=await native({op:'invite',binding_id:binding.id,scopes});
  await page.click('nav a[href="#security"]');
  await page.until('!document.querySelector("#enroll-form").hidden','enrollment form');
  await page.field('#device-name',name);await page.field('#invitation',invite.invitation);await page.click('#enroll');
  await page.until('document.querySelector("#fingerprint").textContent.startsWith("Compare in XT:")','real fingerprint');
  const pending=(await native({op:'pending-enrollments'})).enrollments.find(e=>e.name===name);
  assert.ok(pending);assert.ok(await page.evaluate(`document.querySelector('#fingerprint').textContent.includes(${JSON.stringify(pending.fingerprint)})`));
  await native({op:'approve',enrollment_id:pending.id,fingerprint:pending.fingerprint});
  await page.until('document.querySelector("#connection").textContent==="Connected"','approved connection');
  return await storedDevice();
}
async function resetLocal(){
  await page.click('nav a[href="#security"]');
  if(await page.evaluate('document.querySelector("#connection").textContent==="Connected"')){
    await page.click('#disconnect');
    await page.until('document.querySelector("#connection").textContent==="Disconnected"','disconnect before local reset');
  }
  await page.until('!document.querySelector("#reset-device").disabled','local reset available');
  page.approveDialog('Forget this browser');await page.click('#reset-device');
  await page.until('!document.querySelector("#enroll-form").hidden&&document.querySelector("#reset-device").hidden','local reset complete');
}
// A separate real proof tests route authorization. Only statuses and scoped
// feature booleans leave this browser expression; its bearer token is closed.
async function permissionProbe(){return await page.evaluate(`(async()=>{
  const device=await new Promise((resolve,reject)=>{const r=indexedDB.open('tlachihuani-rc-device-v1',1);r.onerror=reject;r.onsuccess=()=>{const db=r.result,q=db.transaction('identity').objectStore('identity').get(location.origin);q.onsuccess=()=>{resolve(q.result);db.close()};q.onerror=reject}});
  const post=async(p,b)=>fetch('/api/rc/v1'+p,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(b)});
  const challenge=await(await post('/sessions/challenges',{device_id:device.deviceId})).json();
  const signature=await crypto.subtle.sign({name:'ECDSA',hash:'SHA-256'},device.key,Uint8Array.from(atob(challenge.challenge),c=>c.charCodeAt(0)));
  const session=await(await post('/sessions',{device_id:device.deviceId,challenge_id:challenge.id,signature:btoa(String.fromCharCode(...new Uint8Array(signature)))})).json();
  if(!session.access_token)throw Error('Proof did not establish the authorization probe');
  const headers={Authorization:'Bearer '+session.access_token};
  try{const snapshot=await fetch('/api/rc/v1/snapshot',{headers}),capabilities=await fetch('/api/rc/v1/capabilities',{headers});return{snapshot:snapshot.status,status:capabilities.status,capabilities:await capabilities.json()};}
  finally{await fetch('/api/rc/v1/session',{method:'DELETE',headers});}
})()`);}
try{
  await poll(async()=>(await fetch(base+'/api/rc/v1/health')).ok,'isolated RC listener');
  check('Account capabilities require an authenticated session',(await fetch(base+'/api/rc/v1/capabilities')).status===401);
  binding=await native({op:'bind-account',edition:'english',account_id:randomUUID().replaceAll('-',''),display_name:'Connection recovery test account',adapter_id:adapter});
  const oldBinding=await native({op:'bind-account',edition:'english',account_id:randomUUID().replaceAll('-',''),display_name:'Previous offline test account',adapter_id:randomUUID().replaceAll('-','')});
  await native({op:'adapter-heartbeat',adapter_id:adapter,ready:false});
  let accounts=(await native({op:'accounts'})).accounts;
  check('Native account picker distinguishes loading from offline accounts',accounts.find(a=>a.id===binding.id)?.application_state==='loading'&&accounts.find(a=>a.id===oldBinding.id)?.application_state==='offline');
  const pulse=()=>native({op:'adapter-heartbeat',adapter_id:adapter,ready:true,capabilities:{execution_options:true,self_modify_available:true,self_modify_active:false,self_modify_locked:false,model:'explicit test adapter',web_port:8000}});
  await pulse();heartbeat=setInterval(()=>pulse().catch(()=>{}),3000);
  accounts=(await native({op:'accounts'})).accounts;
  const readyAccount=accounts.find(a=>a.id===binding.id);
  check('Native readiness preserves all existing account identity fields',readyAccount.application_state==='ready'&&Object.entries(binding).every(([key,value])=>JSON.stringify(readyAccount[key])===JSON.stringify(value)));
  page=await attachBrowser(await launchBrowser(out));
  await page.navigate(base+'/remote.html');
  await page.until('document.querySelector("#security-profile").textContent.includes("Local-only")','initial profile');
  check('Fresh no-hash launch opens Connection & trust',await page.evaluate('!document.querySelector("[data-panel=security]").hidden'));
  await page.navigate(base+'/remote.html#chat');
  check('Explicit Conversation link provides a visible setup action',await page.evaluate('!document.querySelector("#chat-connection").hidden&&document.querySelector("#chat-connect-link").textContent==="Set up this browser"'));
  await page.click('#chat-connect-link');
  check('Setup action reaches the real enrollment form',await page.evaluate('location.hash==="#security"&&!document.querySelector("#enroll-form").hidden'));
  await page.field('#device-name','Invalid invitation test');await page.field('#invitation','0'.repeat(64));await page.click('#enroll');
  await page.until('document.querySelector("#notice").textContent.includes("create a new invitation")','invalid invitation guidance');
  check('Invalid invitation provides a next action and allows retry',await page.evaluate('!document.querySelector("#enroll").disabled'));
  const original=await enroll(['monitor','chat'],'Recovery original');
  await page.click('nav a[href="#monitor"]');
  check('Conversation/status-only browser explains disabled Start and Stop',await page.evaluate('document.querySelector("#start-app").disabled&&document.querySelector("#stop-app").disabled&&document.querySelector("#lifecycle-help").textContent.includes("Change permissions")'));
  const oldGrant=(await native({op:'devices'})).devices.find(d=>d.id===original);
  check('Native editor identifies the correct account and revision',oldGrant.account_name===binding.display_name&&oldGrant.edition===binding.edition&&Number.isInteger(oldGrant.revision));
  await native({op:'set-device-scopes',device_id:original,revision:oldGrant.revision,scopes:['monitor','chat','launch']});
  await page.until('document.querySelector("#connection").textContent==="Disconnected"','permission change ends old session');
  await page.click('nav a[href="#security"]');await page.click('#connect');
  await page.until('document.querySelector("#connection").textContent==="Connected"','same device reconnects with new permissions');
  check('Adding Start requires no new invitation or browser key',await storedDevice()===original);
  await page.click('nav a[href="#monitor"]');
  check('Start becomes available independently of Stop',await page.evaluate('!document.querySelector("#start-app").disabled&&document.querySelector("#stop-app").disabled'));
  await page.click('#start-app');
  await page.until('document.querySelector("#activity-list").textContent.includes("accepted")','Start request receipt');
  const startOperation=(await native({op:'broker-next'})).operation;
  check('New Start permission admits the actual typed operation',startOperation?.kind==='application.start'&&startOperation.device_id===original);
  await native({op:'broker-complete',operation_id:startOperation.id,result:{status:'starting',ready:false}});
  const newGrant=(await native({op:'devices'})).devices.find(d=>d.id===original);
  await native({op:'set-device-scopes',device_id:original,revision:newGrant.revision,scopes:['monitor','chat']});
  await page.until('document.querySelector("#connection").textContent==="Disconnected"','removed Start observed');
  await page.click('nav a[href="#security"]');await page.click('#connect');
  await page.until('document.querySelector("#connection").textContent==="Connected"','same device with Start removed');
  check('Removing Start disables it again and preserves approval',await storedDevice()===original&&await page.evaluate('document.querySelector("#start-app").disabled'));
  check('Approved browser hides the disconnected chat help',await page.evaluate('document.querySelector("#chat-connection").hidden'));
  check('Connected session cannot accidentally erase its active identity',await page.evaluate('document.querySelector("#reset-device").disabled'));
  await page.evaluate('window.dispatchEvent(new Event("offline"))');
  check('Browser offline notification alone preserves live loopback authority',await page.evaluate('document.querySelector("#connection").textContent==="Connected"'));
  await reloadClient();
  await page.until('!document.querySelector("#connect").disabled','saved identity reload');
  check('Normal page reload retains the same approved device',await storedDevice()===original);
  await page.click('#connect');await page.until('document.querySelector("#connection").textContent==="Connected"','normal reconnect');
  await page.click('#disconnect');await page.until('document.querySelector("#connection").textContent==="Disconnected"','normal disconnect');
  check('Ordinary disconnect retains approval for the next visit',await storedDevice()===original);
  await page.click('#connect');await page.until('document.querySelector("#connection").textContent==="Connected"','reconnected before revocation');
  await native({op:'revoke',device_id:original});await page.until('document.querySelector("#connection").textContent==="Disconnected"','revocation observed');
  await page.click('#connect');await page.until('document.querySelector("#identity-title").textContent.includes("new approval")','revocation recovery message');
  check('Revoked device stays disconnected with a visible recovery action',await page.evaluate('document.querySelector("#connection").textContent==="Disconnected"&&!document.querySelector("#reset-device").hidden&&!document.querySelector("#reset-device").disabled'));
  await page.click('#reset-device'); // The driver declines unless explicitly armed.
  check('Cancelling local reset preserves the old identity',await storedDevice()===original);
  await resetLocal();
  check('Confirmed local reset exposes enrollment without granting authority',await storedDevice()===null&&await page.evaluate('!document.querySelector("#enroll-form").hidden&&document.querySelector("#connect").disabled&&document.querySelector("#connection").textContent==="Disconnected"'));
  check('Local reset does not reverse server revocation',(await native({op:'devices'})).devices.find(d=>d.id===original)?.revoked===true);
  const declinedInvite=await native({op:'invite',binding_id:binding.id,scopes:['chat']});
  await page.field('#device-name','Declined recovery');await page.field('#invitation',declinedInvite.invitation);await page.click('#enroll');
  await page.until('document.querySelector("#fingerprint").textContent.startsWith("Compare in XT:")','declined request pending');
  const declined=(await native({op:'pending-enrollments'})).enrollments.find(e=>e.name==='Declined recovery');
  await native({op:'decline',enrollment_id:declined.id});
  await page.until('document.querySelector("#notice").textContent.includes("declined")','decline observed');
  check('Declined enrollment leaves the browser able to try a new invitation',await page.evaluate('!document.querySelector("#enroll").disabled&&!document.querySelector("#reset-device").disabled'));
  await resetLocal();
  const chatDevice=await enroll(['chat'],'Conversation only');
  await page.until('!document.querySelector("[data-mode=multi_turn_enabled]").disabled','chat capabilities without monitoring');
  let probe=await permissionProbe();
  check('Conversation-only approval can read its own feature availability',probe.status===200&&probe.capabilities.execution_options===true&&probe.capabilities.self_modify_available===true);
  check('Conversation-only approval does not gain monitoring',probe.snapshot===403);
  check('Feature response discloses only five scoped booleans',Object.keys(probe.capabilities).sort().join(',')==='execution_options,self_modify_active,self_modify_available,self_modify_locked,workspace_available'&&Object.values(probe.capabilities).every(v=>typeof v==='boolean')&&probe.capabilities.workspace_available===false);
  await page.click('nav a[href="#chat"]');
  await page.field('#prompt','Actual typed request from a Conversation-only browser');await page.click('#send');
  await page.until('document.querySelectorAll(".message:not(.user)").length===1','chat-only receipt');
  const operation=(await native({op:'adapter-next',adapter_id:adapter})).operation;
  check('Conversation works without View status permission',operation?.kind==='conversation.submit'&&operation.payload.text==='Actual typed request from a Conversation-only browser'&&typeof operation.payload.options.multi_turn_enabled==='boolean');
  await native({op:'adapter-complete',adapter_id:adapter,operation_id:operation.id,result:{text:'Explicit adapter fixture received the scoped request.'}});
  await page.until('document.querySelector("#messages").textContent.includes("Explicit adapter fixture received")','chat-only completion');
  check('Conversation-only browser receives the real operation receipt',await page.evaluate('document.querySelector("#messages").textContent.includes("succeeded")'));
  await page.photo('conversation-only-completed');
  await resetLocal();
  check('Local reset does not revoke a different server grant',(await native({op:'devices'})).devices.find(d=>d.id===chatDevice)?.revoked===false);
  await enroll(['workspace'],'Workspace only');
  await page.until('!document.querySelector("#open-workspace").disabled','workspace-only ready');
  probe=await permissionProbe();
  check('Workspace-only approval enables its workspace without monitoring',probe.status===200&&probe.snapshot===403&&probe.capabilities.workspace_available===true&&probe.capabilities.execution_options===false);
  check('Workspace-only approval does not enable chat controls',await page.evaluate('document.querySelector("#send").disabled&&document.querySelector("[data-mode=multi_turn_enabled]").disabled'));
  await resetLocal();
  await enroll(['monitor'],'Monitoring only');probe=await permissionProbe();
  check('Monitoring remains independent of conversation capabilities',probe.snapshot===200&&probe.status===403);
  check('Monitoring-only approval cannot send or open a workspace',await page.evaluate('document.querySelector("#send").disabled&&document.querySelector("#open-workspace").disabled'));
  await resetLocal();
  const finalDevice=await enroll(['chat'],'Offline recovery');
  await page.until('!!navigator.serviceWorker.controller','cached shell is controlled');
  clearInterval(heartbeat);
  await native({op:'development-stop'});
  await poll(async()=>{try{await fetch(base+'/api/rc/v1/health');return false;}catch{return true;}},'isolated service stopped');
  await page.until('document.querySelector("#connection").textContent==="Disconnected"&&document.querySelector("#notice").textContent.includes("Cannot reach Remote-Controller")','actual service failure recovery');
  check('Actual loopback failure ends the session and explains how to restart RC',await storedDevice()===finalDevice);
  await reloadClient();
  await page.until('document.querySelector("#notice")?.textContent.includes("Cannot reach Remote-Controller")','offline cached startup guidance');
  check('Cached startup with a stopped service retains approval and explains recovery',await storedDevice()===finalDevice&&await page.evaluate('!document.querySelector("#connect").disabled&&document.querySelector("#connection").textContent==="Disconnected"'));
  check('Focused recovery flow has no uncaught JavaScript errors',page.errors.length===0);
  await page.photo('stopped-server-recovery');
}catch(error){checks.push({name:'focused connection recovery',status:'FAIL',error:error.stack});console.error(error);process.exitCode=1;}
finally{
  clearInterval(heartbeat);
  if(page)page.close();
  if(host.exitCode===null)try{await native({op:'development-stop'});}catch{}
  await fs.writeFile(path.join(out,'results.json'),JSON.stringify({source:'real isolated RC service; explicit account adapter fixture; headed Chrome',passed:checks.filter(c=>c.status==='PASS').length,failed:checks.filter(c=>c.status==='FAIL').length,checks},null,2));
  console.log('Connection recovery evidence: '+out);
}
