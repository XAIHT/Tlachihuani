// Run from the verified visible console after real installed onboarding.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import {root,attachBrowser,launchBrowser,sleep} from './rc_installed_driver.mjs';
import {cases} from './rc_prompt_cases.mjs';
import {answerMatches} from './rc_prompt_answer.mjs';
const edition=process.argv[2];assert.ok(['english','spanish'].includes(edition));assert.equal(cases.length,50);
const output=path.join(process.env.TLAMATINI_ACCEPTANCE_ROOT||path.join(root,'artifacts/rc-local-diagnosis'),edition+'-installed');
let metadata=JSON.parse(await fs.readFile(path.join(output,'browser.json'),'utf8'));
try { await fetch(`http://127.0.0.1:${metadata.debugPort}/json/version`); }
catch(error) {
 if(error.cause?.code!=='ECONNREFUSED')throw error;
 console.log('The owned test browser was closed; reopening its retained approved-device profile.');
 metadata=await launchBrowser(output);
}
const page=await attachBrowser(metadata);
if(!await page.evaluate('!!document.querySelector("#connection")'))await page.navigate('http://127.0.0.1:12481/remote.html#chat');
await page.send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
const results=[];const prefix=edition==='english'?'EN':'ES';
const report=async current=>{await fs.writeFile(path.join(output,'prompt-results.json'),JSON.stringify({edition,mode:'installed real model',planned:50,completed:results.length,passed:results.filter(r=>r.passed).length,failed:results.filter(r=>!r.passed).length,current,results},null,2));};
try {
 for(let i=0;i<cases.length;i++) {
  const [name,en,es,common,spanish]=cases[i];const id=prefix+String(i+1).padStart(3,'0');
  const instruction=edition==='english'?en:es;const expected=edition==='spanish'&&spanish!==undefined?spanish:common;
  const prompt=edition==='english'?`Acceptance ${id}. ${instruction} Reply only with the requested result. Do not use tools, commands, files, or external services.`:`Aceptación ${id}. ${instruction} Responde solo con el resultado solicitado. No uses herramientas, comandos, archivos ni servicios externos.`;
  if(await page.evaluate('document.querySelector("#connection").textContent!=="Connected"')){
    await page.evaluate("location.hash='security'");await page.click('#connect');await page.until('document.querySelector("#connection").textContent==="Connected"','approved-device reconnect');
  }
  await page.evaluate("location.hash='chat'");await sleep(100);
  await page.until('!!document.querySelector("[data-mode=multi_turn_enabled]")&&!document.querySelector("[data-mode=multi_turn_enabled]").disabled','live execution-mode support',60000);
  const requested={multi_turn_enabled:i%5!==0,exec_report_enabled:i%5!==0,acpx_enabled:i%5===2,ask_execs_enabled:i%5!==0,step_by_step_enabled:i%5===3,self_modify_enabled:i===4};
  if(!await page.evaluate('document.querySelector(".execution-settings").open'))await page.click('.execution-settings summary');
  const selected={},unavailableModes={};
  for(const [key,value]of Object.entries(requested)){
    const selector='[data-mode="'+key+'"]';const state=await page.evaluate('(()=>{const e=document.querySelector('+JSON.stringify(selector)+');return{disabled:e.disabled,checked:e.checked}})()');
    if(state.disabled){unavailableModes[key]=await page.evaluate('document.querySelector("#mode-state").textContent');continue;}if(state.checked!==value)await page.click(selector);selected[key]=value;
  }
  await page.click('.execution-settings summary');
  const before=await page.evaluate('document.querySelectorAll(".message:not(.user)").length');
  await report({id,name,state:'submitting'});console.log(`REAL PROMPT ${id}/50 ${name}`);
  await page.field('#prompt',prompt);await page.click('#send');
  const began=Date.now();await report({id,name,state:'awaiting real application result'});
  await page.until(`document.querySelectorAll('.message:not(.user)').length>${before}`,'new operation receipt');
  const deadline=Date.now()+300000;let complete=false;
  while(Date.now()<deadline){
    if(await page.evaluate('document.querySelector("#connection").textContent!=="Connected"')){
      // Recover the existing receipt with the approved device key. Never
      // resubmit a prompt when a session expires during model execution.
      await page.evaluate("location.hash='security'");await page.click('#connect');
      await page.until('document.querySelector("#connection").textContent==="Connected"','reconnect to existing receipt');
      await page.evaluate("location.hash='chat'");
    }
    complete=await page.evaluate("(()=>{const e=document.querySelector('.message:not(.user):last-child');return / · (succeeded|failed|cancelled)$/.test(e.querySelector('small').textContent)||!!e.querySelector('.permission')})()");
    if(complete)break;await page.focus();await report({id,name,state:'awaiting real application result',elapsed_seconds:Math.round((Date.now()-began)/1000)});await sleep(10000);
  }
  if(!complete)throw new Error('Timed out awaiting actual model receipt: '+id);
  const observed=await page.evaluate(`(()=>{const e=document.querySelector('.message:not(.user):last-child');return{state:e.querySelector('small').textContent,answer:e.querySelector('span').textContent,permission:!!e.querySelector('.permission'),options:JSON.parse(e.querySelector('.execution-modes')?.dataset.options||'null')}})()`);
  if(observed.permission)throw new Error(id+' requested unexpected execution consent; no automatic approval was given');
  const correct=answerMatches(name,observed.answer,expected);
  const modesMatch=!!observed.options&&Object.entries(selected).every(([key,value])=>observed.options[key]===value);
  const passed=observed.state.endsWith(' · succeeded')&&correct&&modesMatch;
  results.push({id,name,prompt,expected,selected,unavailableModes,modesMatch,...observed,passed,milliseconds:Date.now()-began});await report(null);
  console.log(`${passed?'PASS':'FAIL'} REAL PROMPT ${id} ${name}; ${results.filter(r=>r.passed).length}/${results.length} passed; ${Date.now()-began} ms`);
  if(!passed||i%10===9)await page.photo('prompt-'+id);
  // Never replay a dispatched prompt. A failed answer is recorded, then a new
  // independent case may proceed only after the application completed its work.
 }
}catch(error){console.error(error);await report({state:'blocked',error:error.stack});process.exitCode=1;try{await page.photo('prompt-batch-blocked');}catch{}}
finally{page.close();console.log(`REAL ${edition} PROMPTS: ${results.filter(r=>r.passed).length}/${results.length} passed; ${50-results.length} not run.`);if(results.length!==50||results.some(r=>!r.passed))process.exitCode=1;}
