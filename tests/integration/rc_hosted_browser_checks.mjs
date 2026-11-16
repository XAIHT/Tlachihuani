// Tlachihuani — Angela López Mendoza · XAIHT.
// Real headed browser -> HTTPS cloud -> outbound RC connector. Main replies are
// explicit fixtures, never represented as model or installed-app acceptance.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import {createHash,randomBytes} from 'node:crypto';
import {launchBrowser,attachBrowser,poll,sleep} from './rc_installed_driver.mjs';

const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
const last="document.querySelector('#messages').lastElementChild.querySelector('.response-body')";
export async function runBrowser(ctx){
  const wire=[],checks=[],downloads=path.join(ctx.out,'downloads');
  await fs.mkdir(downloads,{recursive:true});
  let browser,page,closed=false;
  const save=()=>fs.writeFile(path.join(ctx.out,'results.json'),JSON.stringify({checks,failed:checks.filter(x=>x.status==='FAIL').length},null,2));
  async function test(id,name,action){
    console.log('RUN BROWSER '+id+' '+name);
    try{await action();checks.push({id,name,status:'PASS'});console.log('PASS BROWSER '+id);}
    catch(error){checks.push({id,name,status:'FAIL',error:error.stack});console.error('FAIL BROWSER '+id+' '+error.stack);}
    await save();
  }
  async function language(code){
    await page.click('#language');
    await page.send('Input.dispatchKeyEvent',{type:'keyDown',key:code==='es'?'End':'Home'});
    await page.send('Input.dispatchKeyEvent',{type:'keyUp',key:code==='es'?'End':'Home'});
    await page.send('Input.dispatchKeyEvent',{type:'keyDown',key:'Enter'});
    await page.send('Input.dispatchKeyEvent',{type:'keyUp',key:'Enter'});
    await page.until('document.documentElement.lang==='+JSON.stringify(code),'selected UI language');
  }
  const identityStorageKey=account=>ctx.origin+'|'+ctx.installationId+'|'+account.binding.id;
  async function identityRecord(account,remove=false){
    return await page.evaluate('new Promise((resolve,reject)=>{const open=indexedDB.open("tlachihuani-rc-device-v1",1);open.onerror=()=>reject(open.error);open.onsuccess=()=>{const db=open.result,tx=db.transaction("identity",'+JSON.stringify(remove?'readwrite':'readonly')+'),store=tx.objectStore("identity"),request=store.'+(remove?'delete':'get')+'('+JSON.stringify(identityStorageKey(account))+');let value=null;request.onsuccess=()=>{const record=request.result;if(record)value={deviceId:record.deviceId||null,publicKey:record.publicKey,pendingId:record.pending?.id||null};};tx.oncomplete=()=>{db.close();resolve(value);};tx.onerror=tx.onabort=()=>{db.close();reject(tx.error||new Error("Identity test transaction failed"));};};})');
  }
  async function logoutIdentity(){
    if(!await page.evaluate('document.querySelector("#hosted-signout").hidden')){
      await page.click('nav a[href="#computers"]');await page.click('#hosted-signout');
    }
    await page.until('!document.querySelector("#hosted-login-form").hidden&&!document.querySelector("#hosted-login").disabled','fixture signed out');
  }
  async function loginIdentity(account){
    await logoutIdentity();await page.field('#hosted-username',account.user.username);await page.field('#hosted-password',account.user.password);await page.click('#hosted-login');
    const selector='[data-hosted-open][data-binding="'+account.binding.id+'"]';
    await page.until('!!document.querySelector('+JSON.stringify(selector)+')&&!document.querySelector('+JSON.stringify(selector)+').disabled','owner directory ready');
  }
  async function openIdentity(account){
    await page.click('[data-hosted-open][data-binding="'+account.binding.id+'"]');
    await page.until('document.querySelector("#connection").classList.contains("connected")','approved account reconnected');
  }
  async function armIdentityOpenDelay(){
    await page.evaluate(`(()=>{
      const target=indexedDB,descriptor=Object.getOwnPropertyDescriptor(target,'open'),original=target.open;
      const held={armed:true,ready:false,released:false};window.__rcIdentityDelay=held;
      held.restore=()=>{if(descriptor)Object.defineProperty(target,'open',descriptor);else delete target.open;};
      Object.defineProperty(target,'open',{configurable:true,value:function(...args){
        const request=original.apply(this,args);if(!held.armed)return request;held.armed=false;
        Object.defineProperty(request,'onsuccess',{configurable:true,set(listener){
          request.addEventListener('success',event=>{held.ready=true;held.release=()=>{if(held.released)return;held.released=true;listener.call(request,event);};},{once:true});
        }});return request;
      }});return true;
    })()`);
  }
  async function armKeyGenerationDelay(){
    await page.evaluate(`(()=>{
      const target=crypto.subtle,descriptor=Object.getOwnPropertyDescriptor(target,'generateKey'),original=target.generateKey;
      const storageDescriptor=Object.getOwnPropertyDescriptor(indexedDB,'open'),storageOpen=indexedDB.open;
      const held={armed:true,ready:false,released:false,storageOpensAfterRelease:0};window.__rcKeyDelay=held;
      held.restore=()=>{if(descriptor)Object.defineProperty(target,'generateKey',descriptor);else delete target.generateKey;if(storageDescriptor)Object.defineProperty(indexedDB,'open',storageDescriptor);else delete indexedDB.open;};
      Object.defineProperty(indexedDB,'open',{configurable:true,value:function(...args){if(held.released)held.storageOpensAfterRelease++;return storageOpen.apply(this,args);}});
      Object.defineProperty(target,'generateKey',{configurable:true,value:function(...args){
        const actual=original.apply(this,args);if(!held.armed||args[0]?.name!=='ECDSA')return actual;held.armed=false;
        return actual.then(pair=>new Promise(resolve=>{held.ready=true;held.release=()=>{if(held.released)return;held.released=true;resolve(pair);};}));
      }});return true;
    })()`);
  }
  async function restoreRaceHooks(){await page.evaluate('(()=>{for(const name of ["__rcIdentityDelay","__rcKeyDelay"]){const held=window[name];held?.restore?.();held?.release?.();}return true;})()');}
  async function reply(account,html,text){
    await page.click('nav a[href="#chat"]');
    await page.field('#prompt',text);await page.click('#send');
    const operation=await ctx.nextOperation(account,'conversation.submit');
    assert.equal(operation.payload.text,text,'User text reaches only selected account unchanged');
    await ctx.native({op:'adapter-complete',adapter_id:account.adapter,operation_id:operation.id,result:{text,html,format:'html',truncated:false,options:operation.payload.options}});
    await page.until(last+'?.classList.contains("rc-rich-response")&&'+last+'.textContent.includes('+JSON.stringify(text)+')','rendered hosted response');
    return operation;
  }
  try{
    browser=await launchBrowser(ctx.out,{certificateSpkiSha256:ctx.certificateSpkiSha256});
    page=await attachBrowser(browser);
    await page.send('Network.enable');
    for(const method of ['Network.webSocketFrameSent','Network.webSocketFrameReceived','Network.webSocketFrameError','Network.webSocketClosed']) page.onEvent(method,event=>{try{const f=JSON.parse(event.response?.payloadData||'{}'); wire.push({event:method,type:f.type||null,id:f.id||null,path:f.path?.split('?')[0],status:f.status,code:f.code,error:event.errorMessage});if(wire.length>256)wire.shift();}catch{wire.push({event:method,unparsed:true});}});
    await page.send('Browser.setDownloadBehavior',{behavior:'allow',downloadPath:downloads,eventsEnabled:true});
    for(const account of ctx.accounts){
      const es=account.edition==='spanish',code=es?'es':'en',id=code.toUpperCase();
      const connected=es?'Conectado':'Connected';
      const user=account.user,deviceName='Hosted browser '+code;
      await page.navigate(ctx.origin+'/remote.html#computers');
      await page.until('!document.querySelector("#hosted-login-form").hidden','hosted sign-in');
      await language(code);
      await test(id+'01','Localized sign-in rejects a bad password without exposing computers',async()=>{
        assert.equal(await page.evaluate('document.querySelector("#hosted-login").textContent'),es?'Iniciar sesión':'Sign in');
        await page.field('#hosted-username',user.username);await page.field('#hosted-password','incorrect isolated fixture password');await page.click('#hosted-login');
        await page.until('!!document.querySelector("#hosted-status").textContent&&!document.querySelector("#hosted-login").disabled','invalid login outcome');
        assert.equal(await page.evaluate('document.querySelector("#hosted-directory").hidden'),true);
        assert.equal(await page.evaluate('document.querySelector("#hosted-status").textContent'),es?'No se pudo iniciar sesión. Revisa el usuario y la contraseña de prueba.':'Sign-in failed. Check the test username and password.','A wrong password is named as such, not as an unavailable service');
      });
      await page.field('#hosted-password',user.password);await page.click('#hosted-login');
      await page.until('!!document.querySelector("[data-hosted-open]")','owned computer directory');
      await test(id+'02','Directory exposes only this owner and explicitly labels the lab',async()=>{
        const rows=await page.evaluate('[...document.querySelectorAll("[data-hosted-open]")].map(e=>e.dataset.binding)');
        assert.deepEqual(rows,[account.binding.id]);
        assert.ok(await page.evaluate('!document.querySelector("#hosted-banner").hidden'));
        assert.ok(await page.evaluate('document.querySelector("#open-workspace").hidden'));
      });
      await page.click('[data-hosted-open][data-binding="'+account.binding.id+'"]');
      await page.until('location.hash==="#security"&&!document.querySelector("[data-panel=security]").hidden&&!document.querySelector("#enroll-form").hidden&&!document.querySelector("#enroll").disabled','selected fresh account');
      await test(id+'03','Fresh browser enrollment matches the native fingerprint before approval',async()=>{
        const invitation=await ctx.native({op:'invite',binding_id:account.binding.id,scopes:['chat','monitor','launch','stop']});
        await page.field('#device-name',deviceName);await page.field('#invitation',invitation.invitation);await page.click('#enroll');
        const pending=await poll(async()=> (await ctx.native({op:'pending-enrollments'})).enrollments.find(x=>x.name===deviceName),'native enrollment request');
        await page.until('document.querySelector("#fingerprint").textContent.includes('+JSON.stringify(pending.fingerprint)+')','matching visible fingerprint');
        assert.ok(await page.evaluate('document.querySelector("#send").disabled'),'Cloud login alone is insufficient');
        await ctx.native({op:'approve',enrollment_id:pending.id,fingerprint:pending.fingerprint});
        await page.until('document.querySelector("#connection").textContent==='+JSON.stringify(connected)+'&&!document.querySelector("#send").disabled','approved real browser session');
      });
      assert.equal(await page.evaluate('document.querySelector("#connection").classList.contains("connected")'),true,'Dependent browser tests require enrollment');
      await test(id+'04','Ready Tlamatini disables Start and enables only the separately authorized Stop',async()=>{
        await page.click('nav a[href="#monitor"]');await page.click('#refresh');
        await page.until('document.querySelector("#start-app").disabled&&!document.querySelector("#stop-app").disabled','observed running controls');
        assert.ok((await page.evaluate('document.querySelector("#lifecycle-help").textContent')).length>0);
      });
      const title=es?'Respuesta española con acentos ñ':'English response with café';
      await test(id+'05','Canonical response HTML retains headings, styles and code without execution',async()=>{
        await reply(account,'<h2>'+title+'</h2><div style="padding:16px;border-radius:12px;background:linear-gradient(135deg,#0a0a23,#3a0ca3);color:#fff"><strong>42</strong></div><pre><code>&lt;script&gt;window.__unsafeHosted=1&lt;/script&gt;</code></pre><script>window.__unsafeHosted=1</script>',title);
        const rendered=await page.evaluate('(()=>{const e='+last+';return {heading:e.querySelector("h2")?.textContent,background:getComputedStyle(e.querySelector("[style*=linear-gradient]")).backgroundImage,code:e.querySelector("code")?.textContent,unsafe:window.__unsafeHosted||0};})()');
        assert.equal(rendered.heading,title);assert.ok(rendered.background.includes('linear-gradient'));assert.ok(rendered.code.includes('<script>'));assert.equal(rendered.unsafe,0);
        assert.ok((await page.evaluate('document.querySelector("#messages").lastElementChild.querySelector(".execution-modes")?.textContent||""')).startsWith(es?'Ejecutado con: ':'Executed with: '),'Execution receipt label follows the chosen interface language');
      });
      await test(id+'06','History uses the same rich response renderer through the relay',async()=>{
        await page.click('#history');const operation=await ctx.nextOperation(account,'conversation.history');
        await ctx.native({op:'adapter-complete',adapter_id:account.adapter,operation_id:operation.id,result:{messages:[{role:'assistant',text:title,html:'<h3>'+title+'</h3><table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>',format:'html',truncated:false}],truncated:false}});
        await page.until(last+'?.querySelector(".history-message h3")?.textContent==='+JSON.stringify(title),'rendered history');
        assert.equal(await page.evaluate(last+'.querySelectorAll("td").length'),2);
      });
      await test(id+'16','Step by step turns on Multiturn, and an untouched Self-Modify box is not sent',async()=>{
        const open=async()=>{await page.click('nav a[href="#chat"]');if(!await page.evaluate('document.querySelector(".execution-settings").open'))await page.click('.execution-settings summary');};
        const modes=()=>page.evaluate('Object.fromEntries([...document.querySelectorAll("[data-mode]")].map(e=>[e.dataset.mode,e.checked]))');
        await open();if((await modes()).multi_turn_enabled)await page.click('[data-mode="multi_turn_enabled"]');
        assert.equal((await modes()).step_by_step_enabled,false);
        await page.click('[data-mode="step_by_step_enabled"]');
        const chosen=await modes();assert.equal(chosen.step_by_step_enabled,true);assert.equal(chosen.multi_turn_enabled,true,'Step by step needs Multiturn');
        const text=title+' · modes',operation=await reply(account,'<p>'+text+'</p>',text);
        assert.equal(operation.payload.options.multi_turn_enabled,true);assert.equal(operation.payload.options.step_by_step_enabled,true);
        assert.ok(!Object.hasOwn(operation.payload.options,'self_modify_enabled'),'Self-Modify follows the computer until it is changed on the phone');
        await open();await page.click('[data-mode="multi_turn_enabled"]');
        assert.equal((await modes()).step_by_step_enabled,false,'Turning off Multiturn clears Step by step');
        await page.click('[data-mode="multi_turn_enabled"]');await page.click('.execution-settings summary');
      });
      const prefix=es?'archivo español ñ':'English résumé';
      const samples=[
        ['txt','text/plain; charset=utf-8',Buffer.from('\ufeff'+title+'\r\n')],
        ['html','text/html',Buffer.from('<!doctype html><h1>'+title+'</h1><script>window.__downloadExecuted=1</script>')],
        ['svg','image/svg+xml',Buffer.from('<svg xmlns="http://www.w3.org/2000/svg"><text>'+title+'</text><script>window.__downloadExecuted=1</script></svg>')],
        ['csv','text/csv',Buffer.from('\ufeffName,Value\r\n"'+prefix+'",42\r\n')],
        ['png','image/png',Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aVFkAAAAASUVORK5CYII=','base64')],
        ['bin','application/octet-stream',randomBytes(192*1024+19)]
      ];
      for(let index=0;index<samples.length;index++){
        const [ext,type,bytes]=samples[index],name=prefix+'.'+ext;
        await test(id+String(7+index).padStart(2,'0'),'Original '+ext.toUpperCase()+' download preserves bytes and Unicode name',async()=>{
          const ticket=ctx.addFile(account,name,type,bytes);
          await reply(account,'<p>'+name+'</p><a href="#" data-rc-file="'+ticket+'" data-rc-filename="'+name+'">---Load in canvas: '+name+'---</a>',name);
          const selector='#messages > .message:last-child .rc-file-download';
          assert.ok((await page.evaluate('document.querySelector('+JSON.stringify(selector)+').textContent')).includes(es?'Guardar archivo':'Save file'));
          assert.ok(!await page.evaluate(last+'.textContent.includes("Load in canvas")'));
          await page.click(selector);const target=path.join(downloads,name);
          await poll(async()=>{try{return sha(await fs.readFile(target))===sha(bytes);}catch{return false;}},'downloaded original '+ext,30000);
          assert.equal((await fs.stat(target)).size,bytes.length);
          assert.equal(await page.evaluate('window.__downloadExecuted||0'),0);
        });
      }
      await test(id+'13','Mobile width keeps controls and messages inside the viewport',async()=>{
        await page.send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});await page.setTouch(true);
        await page.click('nav a[href="#chat"]');
        assert.ok(await page.evaluate('document.documentElement.scrollWidth<=innerWidth+1'),'No horizontal page overflow');
        await page.photo(code+'-mobile-chat');
        await page.setTouch(false);await page.send('Emulation.clearDeviceMetricsOverride');
      });
      await test(id+'14','Reload and account selection reuse approved browser identity without another invitation',async()=>{
        const count=(await ctx.native({op:'devices'})).devices.filter(x=>x.name===deviceName).length;
        await page.navigate(ctx.origin+'/remote.html#computers');await page.until('!!document.querySelector("[data-hosted-open]")','directory after reload');
        await page.click('[data-hosted-open][data-binding="'+account.binding.id+'"]');
        await page.until('document.querySelector("#connection").classList.contains("connected")','reconnected approved browser');
        assert.equal((await ctx.native({op:'devices'})).devices.filter(x=>x.name===deviceName).length,count);
        assert.equal(await page.evaluate('document.querySelector("#enroll-form").hidden'),true);
      });
      await test(id+'15','Logout removes private conversation and closes browser control authority',async()=>{
        await page.click('nav a[href="#computers"]');await page.click('#hosted-signout');
        await page.until('!document.querySelector("#hosted-login-form").hidden','signed out');
        assert.equal(await page.evaluate('document.querySelector("#connection").classList.contains("connected")'),false);
        assert.ok(!await page.evaluate('document.querySelector("#messages").textContent.includes('+JSON.stringify(title)+')'));
        assert.equal(await page.evaluate('document.querySelector("#send").disabled'),true);
      });
      await page.photo(code+'-signed-out');
    }
    await test('COMMON02','Delayed enrollment persistence cannot overwrite a different account after switching owners',async()=>{
      const [first,second]=ctx.accounts,secondBefore=await identityRecord(second);assert.ok(secondBefore?.deviceId,'Previously approved second browser identity');
      try{
        await loginIdentity(first);await identityRecord(first,true);
        await page.click('[data-hosted-open][data-binding="'+first.binding.id+'"]');
        await page.until('location.hash==="#security"&&!document.querySelector("[data-panel=security]").hidden&&!document.querySelector("#enroll-form").hidden&&!document.querySelector("#enroll").disabled','fresh first identity for storage race');
        const name='Delayed approval persistence '+first.edition,invitation=await ctx.native({op:'invite',binding_id:first.binding.id,scopes:['chat','monitor','launch','stop']});
        await page.field('#device-name',name);await page.field('#invitation',invitation.invitation);await page.click('#enroll');
        const pending=await poll(async()=>(await ctx.native({op:'pending-enrollments'})).enrollments.find(item=>item.name===name),'pending race enrollment');
        await page.until('document.querySelector("#fingerprint").textContent.includes('+JSON.stringify(pending.fingerprint)+')','persisted pending approval');
        const firstBefore=await identityRecord(first);assert.equal(firstBefore.pendingId,pending.id);
        await armIdentityOpenDelay();await ctx.native({op:'approve',enrollment_id:pending.id,fingerprint:pending.fingerprint});
        await page.until('window.__rcIdentityDelay?.ready===true','actual approved-identity IndexedDB open held');
        await logoutIdentity();await loginIdentity(second);await openIdentity(second);
        await page.evaluate('window.__rcIdentityDelay.restore();window.__rcIdentityDelay.release();true');
        // This fresh real IndexedDB read queues behind any transaction started
        // by the released callback, so no timing sleep hides a stale write.
        assert.deepEqual(await identityRecord(second),secondBefore,'A delayed write cannot replace the second account approval');
        assert.deepEqual(await identityRecord(first),firstBefore,'An obsolete write is rejected without changing the original pending record');
        assert.ok(await page.evaluate('document.querySelector("#connection").classList.contains("connected")'));
        assert.ok((await page.evaluate('document.querySelector("#account-line").textContent')).includes(second.binding.display_name));
        await logoutIdentity();await loginIdentity(first);await openIdentity(first);
        assert.ok((await identityRecord(first)).deviceId,'Original approved enrollment can still recover from its own saved pending record');
        await logoutIdentity();await page.photo('credential-storage-race-isolated');
      }finally{await restoreRaceHooks();}
    });
    await test('COMMON03','A delayed real P-256 key cannot persist or reconnect after its selection is abandoned',async()=>{
      const [first,second]=ctx.accounts,secondBefore=await identityRecord(second);
      try{
        await loginIdentity(first);await identityRecord(first,true);await armKeyGenerationDelay();
        await page.click('[data-hosted-open][data-binding="'+first.binding.id+'"]');
        await page.until('window.__rcKeyDelay?.ready===true','actual P-256 key generation held before completion');
        await logoutIdentity();await page.evaluate('window.__rcKeyDelay.release();true');
        await page.until('!document.querySelector("#hosted-refresh").disabled','abandoned key-generation selection settled');
        assert.equal(await page.evaluate('window.__rcKeyDelay.storageOpensAfterRelease'),0,'Stale key completion cannot even begin an IndexedDB persistence operation');
        await page.evaluate('window.__rcKeyDelay.restore();true');
        assert.equal(await identityRecord(first),null,'Stale generated key is never saved after logout');
        assert.equal(await page.evaluate('document.querySelector("#connection").classList.contains("connected")'),false);
        assert.equal(await page.evaluate('document.querySelector("#hosted-selected").textContent'),'');
        assert.equal(await page.evaluate('document.querySelector("#connect").disabled'),true);
        await loginIdentity(second);await openIdentity(second);
        assert.deepEqual(await identityRecord(second),secondBefore,'The other account retains its previously approved key');
        assert.ok((await page.evaluate('document.querySelector("#account-line").textContent')).includes(second.binding.display_name));
        await logoutIdentity();await page.photo('credential-generation-race-isolated');
      }finally{await restoreRaceHooks();}
    });
    await test('COMMON04','Refreshing a real changed cloud identity clears the previous account view',async()=>{
      const [next,previous]=ctx.accounts,privateText='Previous cloud owner private response '+previous.binding.id;
      await loginIdentity(previous);await openIdentity(previous);await reply(previous,'<p>'+privateText+'</p>',privateText);
      await page.click('nav a[href="#computers"]');
      // The same real browser cookie is rotated through the real cloud routes,
      // as another tab would do. No client identity or directory is mocked.
      const switched=await page.evaluate('(async()=>{const bootstrap=async()=>{const r=await fetch("/api/hosted/bootstrap",{credentials:"same-origin",cache:"no-store"});if(!r.ok)throw new Error("Bootstrap failed");return r.json();};const before=await bootstrap();const logout=await fetch("/api/hosted/logout",{method:"POST",credentials:"same-origin",headers:{"X-CSRF-Token":before.csrf_token}});if(!logout.ok)throw new Error("Real cloud logout failed");const anonymous=await bootstrap();const login=await fetch("/api/hosted/login",{method:"POST",credentials:"same-origin",headers:{"Content-Type":"application/json","X-CSRF-Token":anonymous.csrf_token},body:JSON.stringify('+JSON.stringify({username:next.user.username,password:next.user.password})+')});if(!login.ok)throw new Error("Real cloud login failed");const after=await bootstrap();return {userId:after.user?.id};})()');
      assert.equal(switched.userId,next.user.id);
      assert.equal(await page.evaluate('document.querySelector("#hosted-user").textContent'),previous.user.username,'Current UI has not yet refreshed its cloud identity');
      assert.ok(await page.evaluate('document.querySelector("#messages").textContent.includes('+JSON.stringify(privateText)+')'),'Regression starts with the prior account response still visible');
      await page.click('#hosted-refresh');
      const nextSelector='[data-hosted-open][data-binding="'+next.binding.id+'"]';
      await page.until('document.querySelector("#hosted-user").textContent==='+JSON.stringify(next.user.username)+'&&!!document.querySelector('+JSON.stringify(nextSelector)+')','new immutable cloud identity directory');
      assert.equal(await page.evaluate('document.querySelector("#hosted-selected").textContent'),'');
      assert.equal(await page.evaluate('document.querySelector("#messages").textContent.includes('+JSON.stringify(privateText)+')'),false);
      assert.equal(await page.evaluate('document.querySelector("#connection").classList.contains("connected")'),false);
      assert.equal(await page.evaluate('document.querySelector("#send").disabled'),true);
      assert.deepEqual(await page.evaluate('[...document.querySelectorAll("[data-hosted-open]")].map(button=>button.dataset.binding)'),[next.binding.id]);
      await page.photo('cloud-identity-refresh-clears-private-view');await logoutIdentity();
    });
    await test('COMMON01','No uncaught client runtime exceptions during bilingual hosted flow',async()=>assert.deepEqual(page.errors,[]));
  }catch(error){checks.push({id:'HARNESS',name:'Headed hosted browser infrastructure',status:'FAIL',error:error.stack});console.error(error.stack);}
  finally{
    if(page){try{await page.photo('final');await page.send('Browser.close');closed=true;}catch(error){console.error('Owned browser close: '+error.message);}page.close();}
    await fs.writeFile(path.join(ctx.out,'cleanup.json'),JSON.stringify({ownedBrowserClosed:closed||!browser},null,2));
    if(browser&&!closed)checks.push({id:'CLEANUP',name:'Owned browser stopped',status:'FAIL'});
    await fs.writeFile(path.join(ctx.out,'relay-diagnostics.json'),JSON.stringify({wire,errors:page?.errors},null,2));
    await save();
  }
  return {checks,failed:checks.filter(x=>x.status==='FAIL').length};
}
