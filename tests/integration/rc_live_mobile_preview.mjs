// Real installed edition UI behind an isolated development gateway.
// Normal device approval and Django login remain mandatory. Only the service
// registration/heartbeat are fixture setup. This is not installed SCM acceptance.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import {spawn} from 'node:child_process';
import assert from 'node:assert/strict';
import {root,python,launchBrowser,attachBrowser,poll,sleep} from './rc_installed_driver.mjs';
const image=path.resolve(process.argv[2]);assert.ok(image.startsWith(path.join(root,'artifacts')+path.sep));assert.equal(path.basename(image),'Tlachihuani.RC.Server.exe');
const edition=process.argv[3]||'english';assert.ok(['english','spanish'].includes(edition));
const stamp=crypto.randomUUID().replaceAll('-',''),output=path.join(root,'artifacts','rc-live-mobile-preview',stamp),state=path.join(output,'state');
await fs.mkdir(path.join(state,'state'),{recursive:true});
const origin='http://127.0.0.1:12489',port=12489;
let supervisor,server,page,beat,beatError;const children=[];
function start(executable,args,extra={}){const child=spawn(executable,args,{cwd:root,stdio:'inherit',windowsHide:false,...extra});child.done=new Promise((r,j)=>{child.once('exit',code=>r(code));child.once('error',j);});children.push(child);return child;}
async function bounded(done,ms){let timer;try{return await Promise.race([done,new Promise(r=>timer=setTimeout(()=>r(null),ms))]);}finally{clearTimeout(timer);}}
async function native(body){return new Promise((resolve,reject)=>{const child=spawn(image,['--dev-control',stamp,String(server.pid)],{stdio:['pipe','pipe','inherit'],windowsHide:false});let text='';child.stdout.on('data',x=>text+=x);child.on('error',reject);child.on('exit',code=>{try{const value=JSON.parse(text);assert.equal(code,0,value.error?.message);assert.ok(!value.error,value.error?.message);resolve(value);}catch(e){reject(e);}});child.stdin.end(JSON.stringify(body)+'\n');});}
try{
 const matchingHelper=path.join(path.dirname(path.dirname(image)),'Tray/Tlachihuani.Tray.exe');
 const helper=await fs.access(matchingHelper).then(()=>matchingHelper).catch(()=>path.join(root,'artifacts/release-builds/20261114-004000-69ee48de/publish/Tray/Tlachihuani.Tray.exe'));
 supervisor=start(python,[path.join(root,'tests/integration/rc_frozen_boot.py'),edition,'--stop-helper',helper,'--serve-ready',path.join(output,'app')]);
 const app=await poll(async()=>JSON.parse(await fs.readFile(path.join(output,'app/ready.json'),'utf8')),'real installed app readiness',180000);
 const browserOutput=path.join(output,edition+'-installed');await fs.mkdir(browserOutput,{recursive:true});
 page=await attachBrowser(await launchBrowser(browserOutput));
 // Installed onboarding first signs in directly. Retain that real host cookie
 // here so the mobile run exercises isolation from a prior direct app session.
 await page.navigate(app.base+'/');await page.field('#id_username',process.env.TLAMATINI_USER||'user');await page.field('#id_password',process.env.TLAMATINI_PASSWORD||'changeme');await page.click('form button[type=submit]');
 await page.until('!document.querySelector("#id_password")','normal edition login');
 if(!app.identity.enabled){
   // Enable the user's requested edition through its normal password-confirmed
   // account form. Never set a database field or forge an enabled identity.
   await page.navigate(app.base+'/agent/remote-controller/');
   await page.until('document.querySelector("#service-state")?.classList.contains("active")','installed account-binding service',30000);
   await page.field('#current-password',process.env.TLAMATINI_PASSWORD||'changeme');await page.click('form:has(input[value="enable"]) button');
   await page.until('document.querySelector("#account-state")?.classList.contains("active")','password-confirmed edition activation');
   app.identity=await page.evaluate('fetch("/agent/remote-controller/session/").then(r=>r.json())');
   await fs.writeFile(path.join(output,'account-activation.json'),JSON.stringify({edition,normal_password_form:true,identity:app.identity},null,2));
 }
 assert.equal(app.identity.enabled,true);assert.equal(app.identity.edition,edition);
 const sid=JSON.parse((await fs.readFile('C:/ProgramData/XAIHT/Tlachihuani.RemoteController/installation.json','utf8')).replace(/^\uFEFF/,'')).operator_sid;
 const adapter=crypto.randomUUID().replaceAll('-','');
 // Fresh test state references only the normal account identity just observed.
 // No production service state or application database is copied or directly edited.
 const binding={id:app.identity.binding_id,edition,account_id:app.identity.account_id,display_name:process.env.TLAMATINI_USER||'user',windows_sid:sid,adapter_id:adapter,enabled:true,revision:1};
 await fs.writeFile(path.join(state,'state/state.json'),JSON.stringify({schema:1,installation_id:stamp,bindings:{[binding.id]:binding}}));
 server=start(image,['--console',state,stamp,String(port)]);
 await poll(async()=>(await(await fetch(origin+'/api/rc/v1/health')).json()).ready,'development gateway');
 const heartbeat=()=>native({op:'adapter-heartbeat',adapter_id:adapter,ready:true,capabilities:{execution_options:true,self_modify_available:true,self_modify_active:false,self_modify_locked:false,model:'real installed UI; fixture service heartbeat',web_port:Number(new URL(app.base).port)}});
 await heartbeat();beat=setInterval(()=>void heartbeat().catch(error=>beatError=error),5000);
 await page.navigate(origin+'/remote.html#security');
 const invitation=await native({op:'invite',binding_id:binding.id,scopes:['monitor','workspace','chat']});
 await page.field('#device-name','Isolated mobile UI verification');await page.field('#invitation',invitation.invitation);await page.click('#enroll');
 await page.until('document.querySelector("#fingerprint").textContent.includes("Compare in XT:")','native fingerprint comparison');
 const pending=(await native({op:'pending-enrollments'})).enrollments.find(e=>e.binding_id===binding.id);assert.ok(pending);
 assert.ok(await page.evaluate('document.querySelector("#fingerprint").textContent.includes('+JSON.stringify(pending.fingerprint)+')'));
 await native({op:'approve',enrollment_id:pending.id,fingerprint:pending.fingerprint});await page.until('document.querySelector("#connection").textContent==="Connected"','approved development gateway');page.close();page=null;
 const checks=start(process.execPath,[path.join(root,'tests/integration/rc_installed_mobile.mjs'),edition],{env:{...process.env,TLAMATINI_ACCEPTANCE_ROOT:output,TLAMATINI_RC_ORIGIN:origin,TLAMATINI_MOBILE_CHECK_KIND:'real installed '+edition+' UI + isolated development gateway; NOT installed SCM or real-prompt acceptance'}});
 assert.equal(await checks.done,0,'Headed mobile checks');assert.ok(!beatError,String(beatError));
}catch(error){console.error(error);await fs.writeFile(path.join(output,'failure.json'),JSON.stringify({error:error.stack},null,2));process.exitCode=1;}
finally{
 page?.close();clearInterval(beat);
 if(server){try{await native({op:'development-stop'});}catch{}await bounded(server.done,10000);if(server.exitCode===null)server.kill();}
 if(supervisor){await fs.mkdir(path.join(output,'app'),{recursive:true});await fs.writeFile(path.join(output,'app/stop.ready'),'stop');await bounded(supervisor.done,45000);if(supervisor.exitCode!==0){console.error('Owned main cleanup requires inspection.');process.exitCode=1;}}
 console.log('Real UI / isolated gateway evidence: '+output);
}
