// Read-only reconciliation of the existing smoke receipt after a harness-only
// text-selector failure. Never submits a prompt, enrolls, or starts an app.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import {attachBrowser} from './rc_installed_driver.mjs';
const output=path.join(process.env.TLAMATINI_ACCEPTANCE_ROOT,'english-installed');
const old=JSON.parse(await fs.readFile(path.join(output,'onboarding-results.json'),'utf8'));
assert.equal(old.edition,'english');assert.equal(old.passed,8);assert.equal(old.failed,1);
assert.ok(old.results.at(-1).error.includes('Real model returns the expected arithmetic answer'));
const page=await attachBrowser(JSON.parse(await fs.readFile(path.join(output,'browser.json'),'utf8')));
try{
 await page.focus();
 const receipt=await page.evaluate(`(()=>{const users=[...document.querySelectorAll('.message.user')],answers=[...document.querySelectorAll('.message:not(.user)')],r=answers.at(-1);return{origin:location.origin,prompt:users.at(-1)?.querySelector('span').textContent,userCount:users.length,answerCount:answers.length,state:r?.querySelector('small').textContent,answer:r?.querySelector('span').textContent,options:JSON.parse(r?.querySelector('.execution-modes')?.dataset.options||'null')}})()`);
 assert.equal(receipt.origin,'http://127.0.0.1:12481');assert.equal(receipt.userCount,1);assert.equal(receipt.answerCount,1);
 assert.equal(receipt.prompt,'Local connection acceptance: reply with only the result of 17 + 25. Do not use tools.');
 assert.equal(receipt.state,'Chat request · succeeded');assert.equal(receipt.answer.trim(),'42');assert.equal(page.errors.length,0);
 const previous='onboarding-results-before-reconciliation-'+Date.now()+'.json';
 await fs.copyFile(path.join(output,'onboarding-results.json'),path.join(output,previous));
 const results=old.results.slice(0,-1).concat([{name:'Real model returns the expected arithmetic answer',status:'PASS'},{name:'Client has no uncaught JavaScript exception during receipt reconciliation',status:'PASS'}]);
 await fs.writeFile(path.join(output,'smoke-response.txt'),receipt.answer);
 await fs.writeFile(path.join(output,'onboarding-reconciliation.json'),JSON.stringify({previous,reason:'Whole-card text concatenated labels with the answer; verified the actual answer span without resubmitting.',receipt},null,2));
 await page.photo('first-real-answer');
 await fs.writeFile(path.join(output,'onboarding-results.json'),JSON.stringify({...old,reconciled_from:previous,passed:results.length,failed:0,results},null,2));
 console.log('PASS: existing installed English smoke receipt verified as 42; no prompt resent.');
}finally{page.close();}
