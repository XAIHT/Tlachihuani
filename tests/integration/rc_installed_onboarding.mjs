// Tlachihuani — Angela López Mendoza · XAIHT.
// Normal installed login/enrollment and a real model response. No model fixture.
import {spawn} from 'node:child_process';
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import {root,python,native,poll,launchBrowser,attachBrowser,sleep} from './rc_installed_driver.mjs';
const edition=process.argv[2]||'english';assert.ok(['english','spanish'].includes(edition));
const resumeExisting=process.argv[3]==='--resume-existing';
const install=edition==='english'?'C:/Tlamatini':'C:/Tlamatini-Spanish';
const executable=path.join(install,edition==='english'?'Tlamatini.exe':'Tlamatini-Spanish.exe');
const output=path.join(process.env.TLAMATINI_ACCEPTANCE_ROOT||path.join(root,'artifacts/rc-local-diagnosis'),edition+'-installed');
await fs.mkdir(output,{recursive:true});
if(resumeExisting){
  await fs.copyFile(path.join(output,'onboarding-results.json'),path.join(output,'onboarding-results-before-resume-'+Date.now()+'.json'));
  assert.equal(await fs.access(path.join(output,'prompt-results.json')).then(()=>true).catch(()=>false),false,'Do not resume onboarding after batch dispatch');
}
const results=[];let page;
const check=(name,condition)=>{assert.ok(condition,name);results.push({name,status:'PASS'});console.log('PASS '+edition+' '+name);};
try{
  const config=JSON.parse((await fs.readFile(path.join(install,'config.json'),'utf8')).replace(/^\uFEFF/,''));
  const port=Number(config.django_port||8000);assert.ok(Number.isInteger(port)&&port>=1024&&port<=65535);
  const base='http://127.0.0.1:'+port;
  const service=await native({op:'status'});check('Installed RC is ready',service.health==='ready'&&service.listener);
  let already=false;try{await fetch(base+'/',{signal:AbortSignal.timeout(1500)});already=true;}catch{}
  assert.equal(already,resumeExisting,'Fresh onboarding requires a stopped application; continuation requires the existing application');
  let browser;
  if(resumeExisting){
    const previous=JSON.parse(await fs.readFile(path.join(output,'application.json'),'utf8'));
    assert.equal(previous.edition,edition);assert.equal(previous.executable,executable);assert.equal(previous.base,base);
    browser=JSON.parse(await fs.readFile(path.join(output,'browser.json'),'utf8'));
    check('Existing installed application retained after a pre-prompt focus failure',true);
  }else{
  const launcher=spawn(python,[path.join(root,'tests/integration/rc_launch_installed.py'),edition,output],{stdio:'inherit',windowsHide:false});
  assert.equal(await new Promise((r,j)=>{launcher.once('exit',r);launcher.once('error',j);}),0,'Dedicated console launch');
  console.log('Started installed '+edition+' application; waiting for its real login page.');
  await poll(async()=>(await fetch(base+'/',{signal:AbortSignal.timeout(2000)})).ok,'installed login page',120000);
  check('Installed application serves its login page',true);
  browser=await launchBrowser(output);
  }
  page=await attachBrowser(browser);
  await page.navigate(base+(resumeExisting?'/agent/remote-controller/':'/'));
  const username=process.env.TLAMATINI_USER||'user';const password=process.env.TLAMATINI_PASSWORD||'changeme';
  if(await page.evaluate('!!document.querySelector("#id_username")')){
    await page.field('#id_username',username);await page.field('#id_password',password);await page.click('form button[type="submit"]');
  }
  await page.until('!document.querySelector("#id_password")','normal login acceptance',20000);
  check('Normal account login succeeds',true);
  await page.navigate(base+'/agent/remote-controller/');
  await page.until('!!document.querySelector("#account-state")','installed account connector');
  check('Frozen account connector is available',true);
  check('Account page reaches installed RC',await page.evaluate('document.querySelector("#service-state").classList.contains("active")'));
  if(!await page.evaluate('document.querySelector("#account-state").classList.contains("active")')){
    await page.field('#current-password',password);await page.click('form:has(input[value="enable"]) button');
    await page.until('document.querySelector("#account-state")?.classList.contains("active")','account activation',20000);
  }
  check('Account enabled through password-confirmed form',true);
  const matches=(await native({op:'accounts'})).accounts.filter(a=>a.edition===edition&&a.display_name===username);
  assert.equal(matches.length,1,'Exact edition/account binding');const binding=matches[0];
  await fs.writeFile(path.join(output,'binding.json'),JSON.stringify({id:binding.id,edition,display_name:binding.display_name},null,2));
  const invitation=await native({op:'invite',binding_id:binding.id,scopes:['monitor','chat','launch','stop','workspace','schedules']});
  await page.navigate(service.origin+'/remote.html#security');
  await page.until('!!document.querySelector("#enroll")','live RC client');
  await page.field('#device-name','Installed acceptance '+edition);await page.field('#invitation',invitation.invitation);await page.click('#enroll');
  await page.until('document.querySelector("#fingerprint").textContent.includes("Compare in XT:")','device fingerprint');
  const enrollments=(await native({op:'pending-enrollments'})).enrollments.filter(e=>e.binding_id===binding.id&&e.name==='Installed acceptance '+edition);
  assert.equal(enrollments.length,1,'Exact pending test device');
  const enrollment=enrollments[0];
  check('Browser fingerprint matches native approval authority',await page.evaluate(`document.querySelector('#fingerprint').textContent.includes(${JSON.stringify(enrollment.fingerprint)})`));
  await native({op:'approve',enrollment_id:enrollment.id,fingerprint:enrollment.fingerprint});
  await page.until('document.querySelector("#connection").textContent==="Connected"','authenticated local connection');
  check('Real browser device authenticates to installed service',true);
  await page.evaluate("location.hash='chat'");await sleep(200);
  await page.photo('connected');
  const prompt=edition==='english'?'Local connection acceptance: reply with only the result of 17 + 25. Do not use tools.':'Prueba de conexión local: responde solamente con el resultado de 17 + 25. No uses herramientas.';
  const before=await page.evaluate('document.querySelectorAll(".message:not(.user)").length');
  await page.field('#prompt',prompt);await page.click('#send');
  await page.until('document.querySelectorAll(".message:not(.user)").length>'+before,'new smoke receipt');
  await page.until('document.querySelector(".message:not(.user):last-child small")?.textContent.endsWith(" · succeeded")','actual model response',240000);
  const answer=await page.evaluate('document.querySelector(".message:not(.user):last-child span").textContent');
  check('Real model returns the expected arithmetic answer',answer.trim()==='42');
  await fs.writeFile(path.join(output,'smoke-response.txt'),answer);
  await page.photo('first-real-answer');
  check('Client has no uncaught JavaScript exception',page.errors.length===0);
}catch(error){console.error('FAIL '+edition+' '+error.message);results.push({name:'installed onboarding',status:'FAIL',error:error.stack});if(page)try{await page.photo('failure');}catch{}process.exitCode=1;}
finally{if(page)page.close();await fs.writeFile(path.join(output,'onboarding-results.json'),JSON.stringify({edition,resumed:resumeExisting,source:'installed executables and live model',passed:results.filter(r=>r.status==='PASS').length,failed:results.filter(r=>r.status==='FAIL').length,results},null,2));console.log('Installed onboarding evidence: '+output);}
