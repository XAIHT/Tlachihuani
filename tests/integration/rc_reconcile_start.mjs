// Reconcile a completed remote launch; never sends another start request.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {root,python,attachBrowser} from './rc_installed_driver.mjs';
const output=path.join(process.env.TLAMATINI_ACCEPTANCE_ROOT,'english-installed');
const json=async name=>JSON.parse((await fs.readFile(path.join(output,name),'utf8')).replace(/^\uFEFF/,''));
const app=await json('application.json'),launch=await json('running-parent.json'),launcher=await json('launcher-parent.json'),cleanup=await json('detached-test-cleanup.json');
const page=await attachBrowser(await json('browser.json'));
try{
 await page.focus();
 const receipt=await page.evaluate(`(()=>{const e=[...document.querySelectorAll('.message:not(.user)')].filter(n=>n.querySelector('small')?.textContent.startsWith('Start Tlamatini')).at(-1);return{state:e?.querySelector('small').textContent,answer:e?.querySelector('span').textContent}})()`);
 assert.equal(receipt.state,'Start Tlamatini · succeeded');
 const observed=await new Promise((resolve,reject)=>{const p=spawn(python,[path.join(root,'tests/integration/rc_installed_processes.py'),'english'],{stdio:['ignore','pipe','inherit'],windowsHide:false});let text='';p.stdout.on('data',b=>text+=b);p.once('error',reject);p.once('exit',code=>{try{assert.equal(code,0);resolve(JSON.parse(text));}catch(e){reject(e);}});});
 assert.equal(observed.processes.length,1);const current=observed.processes[0];
 assert.equal(current.pid,launch.ProcessId);assert.notEqual(current.pid,app.pid);assert.ok(current.created_at>cleanup.at);
 assert.equal(launch.ParentProcessId,launcher.ProcessId);assert.equal(launcher.ExecutablePath,'C:\\Program Files\\XAIHT\\Tlachihuani\\Tray\\Tlachihuani.Tray.exe');
 assert.ok((await fetch(app.base+'/')).ok);await page.until('!document.querySelector("[data-mode=multi_turn_enabled]").disabled','fresh installed adapter capabilities',60000);
 const result={edition:'english',action:'start',reconciled:true,reason:'Verified the existing successful remote start receipt and its XT-created process without replay.',results:[{operation:'start',receipt,passed:true},{check:'new installed process launched by installed XT',process:current,launcher:launcher.ExecutablePath,passed:true},{check:'HTTP and live adapter capability available',passed:true}]};
 await fs.copyFile(path.join(output,'lifecycle-start.json'),path.join(output,'lifecycle-start-before-reconciliation-'+Date.now()+'.json'));
 await fs.writeFile(path.join(output,'lifecycle-start.json'),JSON.stringify(result,null,2));await page.photo('lifecycle-start');console.log('PASS: existing English remote start reconciled; no launch replayed.');
}finally{page.close();}
