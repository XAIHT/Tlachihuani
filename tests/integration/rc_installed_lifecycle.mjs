// Real installed lifecycle through the RC browser, with HTTP/process evidence.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {root,python,attachBrowser,poll,sleep} from './rc_installed_driver.mjs';
const edition=process.argv[2],action=process.argv[3];
assert.ok(['english','spanish'].includes(edition));assert.ok(['start','restart','stop'].includes(action));
const output=path.join(process.env.TLAMATINI_ACCEPTANCE_ROOT||path.join(root,'artifacts/rc-local-diagnosis'),edition+'-installed');
const metadata=JSON.parse(await fs.readFile(path.join(output,'browser.json'),'utf8'));
const page=await attachBrowser(metadata);const results=[];
const app=JSON.parse(await fs.readFile(path.join(output,'application.json'),'utf8'));
async function processes(){
 return await new Promise((resolve,reject)=>{const child=spawn(python,[path.join(root,'tests/integration/rc_installed_processes.py'),edition],{stdio:['ignore','pipe','inherit'],windowsHide:false});let out='';child.stdout.on('data',x=>out+=x);child.once('error',reject);child.once('exit',code=>{try{assert.equal(code,0);resolve(JSON.parse(out));}catch(e){reject(e);}});});
}
async function ready(){
 if(!await page.evaluate('!!document.querySelector("#connection")'))await page.navigate('http://127.0.0.1:12481/remote.html#security');
 if(!await page.evaluate('document.querySelector("#connection").textContent==="Connected"')){await page.evaluate("location.hash='security'");await page.click('#connect');await page.until('document.querySelector("#connection").textContent==="Connected"','device connection');}
}
async function operation(kind){
 await page.evaluate("location.hash='monitor'");await sleep(150);
 const before=await page.evaluate('document.querySelectorAll(".message:not(.user)").length');
 if(kind==='stop')page.approveDialog('Stop this Tlamatini edition?');
 await page.click(kind==='stop'?'#stop-app':'#start-app');
 await page.until('document.querySelectorAll(".message:not(.user)").length>'+before,'new lifecycle receipt');
 await page.until("(()=>{const e=document.querySelector('.message:not(.user):last-child');return / · (succeeded|failed|cancelled)$/.test(e.querySelector('small').textContent)})()",'lifecycle completion',90000);
 const receipt=await page.evaluate("document.querySelector('.message:not(.user):last-child').textContent");
 assert.match(receipt,/ · succeeded/);results.push({operation:kind,receipt,at:new Date().toISOString()});
}
try{
 await ready();
 const initial=await processes();
 if(action==='start')assert.equal(initial.processes.length,0,'Remote start requires a stopped edition');
 else{
 assert.ok(initial.processes.length>0,'Installed application is initially running');
 await operation('stop');
 await poll(async()=>!(await processes()).processes.length,'verified installed processes exited',15000);
 await poll(async()=>{try{await fetch(app.base+'/',{signal:AbortSignal.timeout(1000)});return false;}catch{return true;}},'application HTTP listener stopped',15000);
 results.push({check:'actual processes and HTTP listener stopped',passed:true});
 }
 assert.ok((await fetch('http://127.0.0.1:12481/api/rc/v1/health')).ok,'RC stays running');
 if(action==='restart'||action==='start'){
   await operation('start');await poll(async()=>(await fetch(app.base+'/',{signal:AbortSignal.timeout(2000)})).ok,'installed HTTP returned',120000);
   await page.until('!document.querySelector("[data-mode=multi_turn_enabled]").disabled','fresh ready capability',120000);
   const observed=await processes();assert.equal(observed.processes.length,1,'One installed application process');
   if(action==='restart')assert.ok(initial.processes.every(old=>observed.processes[0].created_at>old.created_at),'Restart creates a new process instead of accepting the previous heartbeat');
   results.push({check:'real installed application started via RC',initial_processes:initial.processes,...observed,passed:true});
 }
 await page.photo('lifecycle-'+action);console.log('PASS installed '+edition+' remote '+action);
}catch(error){results.push({passed:false,error:error.stack});process.exitCode=1;console.error(error);try{await page.photo('lifecycle-'+action+'-failure');}catch{}}
finally{await fs.writeFile(path.join(output,'lifecycle-'+action+'.json'),JSON.stringify({edition,action,results},null,2));page.close();}
