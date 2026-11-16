// Tlachihuani — Created for Angela López Mendoza · XAIHT.
// Run only from the verified visible development queue. Uses real Keycloak OIDC
// and a real isolated cloud host; no direct-grant/password-token login shortcut.
// References: https://www.keycloak.org/server/containers
// https://www.keycloak.org/server/importExport
// https://www.keycloak.org/server/hostname
import fs from 'node:fs/promises';
import path from 'node:path';
import https from 'node:https';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {randomUUID,randomBytes,createHash,X509Certificate} from 'node:crypto';
import {root,poll,sleep,launchBrowser,attachBrowser} from './rc_installed_driver.mjs';

const guid=()=>randomUUID().replaceAll('-','');
const origin='https://127.0.0.1:12490',idpOrigin='https://127.0.0.2:12492';
const out=path.resolve(process.env.TL_RC_OIDC_TEST_OUTPUT||path.join(root,'artifacts/rc-hosted-oidc',String(Date.now())));
const cloudImage=process.env.TL_RC_CLOUD_TEST_IMAGE||path.join(root,'src/Tlachihuani.RC.Cloud/bin/Release/net10.0/Tlachihuani.RC.Cloud.exe');
const certPath=process.env.TL_RC_HOSTED_CERT,keyPath=process.env.TL_RC_HOSTED_KEY,pfxPath=process.env.TL_RC_HOSTED_PFX,pfxPassword=process.env.TL_RC_HOSTED_PFX_PASSWORD;
if(!certPath||!keyPath||!pfxPath||!pfxPassword)throw Error('Set TL_RC_HOSTED_CERT, TL_RC_HOSTED_KEY, TL_RC_HOSTED_PFX and TL_RC_HOSTED_PFX_PASSWORD for the isolated certificate.');
for(const location of [out,certPath,keyPath,pfxPath]){
  assert.ok(path.isAbsolute(location),'Absolute application-owned test paths');
  assert.ok(!/^[a-z]:[\\/]Users[\\/]/i.test(location),'Test scratch and credentials must be outside Windows user profiles');
}
const certificate=new X509Certificate(await fs.readFile(certPath)),ca=certificate.toString();
assert.ok(certificate.checkIP('127.0.0.1')&&certificate.checkIP('127.0.0.2'),'Certificate SAN covers both isolated loopback authorities');
const certificateSha256=createHash('sha256').update(certificate.raw).digest('hex');
const certificateSpkiSha256=createHash('sha256').update(certificate.publicKey.export({format:'der',type:'spki'})).digest('base64');
const runId=guid(),realmName='tlachihuani-rc-'+runId.slice(0,12),containerName='tlachihuani-rc-oidc-'+runId.slice(0,12);
const authority=idpOrigin+'/realms/'+realmName,clientId='tlachihuani-rc-development';
const clientSecret=randomBytes(32).toString('hex');
const subjects={en:randomUUID(),es:randomUUID(),unlinked:randomUUID()};
const users=[
  {id:guid(),username:'oidc-english-owner',login:'qa-en',subject:subjects.en,language:'en',edition:'english',password:'QA-only-'+randomBytes(12).toString('hex')},
  {id:guid(),username:'oidc-spanish-owner',login:'qa-es',subject:subjects.es,language:'es',edition:'spanish',password:'QA-only-'+randomBytes(12).toString('hex')}
];
const unlinked={login:'qa-unlinked',subject:subjects.unlinked,password:'QA-only-'+randomBytes(12).toString('hex')};
const computers=users.map((user,index)=>({installation_id:guid(),name:index===0?'OIDC English test PC':'Computadora de prueba OIDC',connector_token:guid()+guid(),accounts:[{binding_id:guid(),owner_id:user.id,edition:user.edition,display_name:index===0?'English account':'Cuenta en español'}]}));
const configPath=path.join(out,'cloud-oidc.json'),statePath=path.join(out,'cloud-grants.json');
const configuration={schema:1,security_profile:'development-loopback',origin,certificate_path:pfxPath,certificate_password:pfxPassword,authentication_mode:'oidc',oidc:{authority,client_id:clientId,client_secret:clientSecret,certificate_sha256:certificateSha256},state_path:statePath,users:users.map(user=>({id:user.id,username:user.username,oidc_issuer:authority,oidc_subject:user.subject})),computers};
const realm={realm:realmName,enabled:true,sslRequired:'all',registrationAllowed:false,resetPasswordAllowed:false,loginWithEmailAllowed:false,duplicateEmailsAllowed:true,internationalizationEnabled:true,supportedLocales:['en','es'],defaultLocale:'en',accessTokenLifespan:180,ssoSessionIdleTimeout:600,ssoSessionMaxLifespan:1200,
  clients:[{clientId,enabled:true,fullScopeAllowed:false,protocol:'openid-connect',publicClient:false,secret:clientSecret,standardFlowEnabled:true,directAccessGrantsEnabled:false,serviceAccountsEnabled:false,implicitFlowEnabled:false,consentRequired:false,redirectUris:[origin+'/signin-oidc'],webOrigins:[origin],attributes:{'pkce.code.challenge.method':'S256'}}],
  users:[...users,unlinked].map(user=>({id:user.subject,username:user.login,enabled:true,email:'same-email-for-isolation@example.invalid',emailVerified:true,firstName:'Synthetic',lastName:'OIDC test',requiredActions:[],credentials:[{type:'password',value:user.password,temporary:false}]}))
};
await fs.mkdir(path.join(out,'scratch'),{recursive:true});await fs.mkdir(path.join(out,'realm'),{recursive:true});
await fs.writeFile(configPath,JSON.stringify(configuration,null,2));
await fs.writeFile(path.join(out,'realm',realmName+'-realm.json'),JSON.stringify(realm,null,2));
const childEnv={...process.env,TEMP:path.join(out,'scratch'),TMP:path.join(out,'scratch'),TMPDIR:path.join(out,'scratch')};
let containerId=null,containerRemoved=false,containerLogs,cloud,browser,client,cloudRound=0,resolvedImage=null,authorizationNavigation=null;
const checks=[],processes=new Set(),authorizationModes={},cloudShutdowns=[];
const summary=()=>({source:'Real Keycloak 26.8.2 container and real isolated cloud OIDC, actual headed Chrome Authorization Code + PKCE, synthetic identities; no installed state or public remote acceptance',image:resolvedImage,containerName,authority,authorizationModes,cloudShutdowns,passed:checks.filter(x=>x.status==='PASS').length,failed:checks.filter(x=>x.status==='FAIL').length,checks});
async function save(){await fs.writeFile(path.join(out,'results.json'),JSON.stringify(summary(),null,2));}
async function failureEvidence(id){
  if(!client)return;
  const name='failure-'+id.replace(/[^a-z0-9-]/gi,'-');
  try{
    // Preserve visible recovery evidence without cookies, input values, URL
    // query parameters, OAuth codes, state, nonce or credential material.
    const observed=await client.evaluate("(()=>({location:location.origin+location.pathname+location.hash,title:document.title,language:document.documentElement.lang,status:document.querySelector('#hosted-status')?.textContent||'',visibleText:(document.body?.innerText||'').slice(0,12000),invalidInputs:[...document.querySelectorAll('[aria-invalid=true]')].map(e=>({id:e.id,role:e.getAttribute('role'),describedBy:e.getAttribute('aria-describedby')}))}))()");
    await fs.writeFile(path.join(out,name+'.json'),JSON.stringify(observed,null,2));
  }catch(error){console.error('Failure DOM evidence: '+error.message);}
  try{await client.photo(name);}catch(error){console.error('Failure screenshot: '+error.message);}
}
async function test(id,name,action){console.log('RUN '+id+' '+name);try{await action();checks.push({id,name,status:'PASS'});console.log('PASS '+id+' '+name);}catch(error){checks.push({id,name,status:'FAIL',error:error.stack});console.error('FAIL '+id+' '+error.stack);process.exitCode=1;await failureEvidence(id);}await save();}
async function command(label,exe,args,{timeout=180000,allowFailure=false}={}){
  console.log('COMMAND '+label);const logPath=path.join(out,label.replace(/[^a-z0-9-]/gi,'-')+'.log');
  return await new Promise((resolve,reject)=>{
    const child=spawn(exe,args,{cwd:root,stdio:['ignore','pipe','pipe'],windowsHide:false,env:childEnv});processes.add(child);
    let text='',writes=Promise.resolve(),finished=false;
    const append=(data,error)=>{text+=data.toString();if(text.length>8*1024*1024)text=text.slice(-8*1024*1024);(error?process.stderr:process.stdout).write(data);writes=writes.then(()=>fs.appendFile(logPath,data));};
    child.stdout.on('data',data=>append(data,false));child.stderr.on('data',data=>append(data,true));
    const timer=setTimeout(()=>{child.kill();},timeout);
    child.once('error',error=>{clearTimeout(timer);processes.delete(child);if(!finished){finished=true;reject(error);}});
    child.once('exit',async(code,signal)=>{clearTimeout(timer);processes.delete(child);await writes;if(finished)return;finished=true;if((code!==0||signal)&&!allowFailure)reject(Error(label+' failed with '+(signal||code)+'; see '+logPath));else resolve({code,signal,text});});
  });
}
function startLogged(exe,args,name,{parentStdin=false}={}){
  const child=spawn(exe,args,{cwd:root,stdio:[parentStdin?'pipe':'ignore','pipe','pipe'],windowsHide:false,env:childEnv});processes.add(child);
  child.parentStdin=parentStdin;child.runLabel=name;
  if(child.stdin)child.stdin.on('error',error=>{if(!ownedExited(child))console.error(name+' shutdown pipe: '+error.message);});
  let writes=Promise.resolve();const record=data=>{process.stdout.write(data);writes=writes.then(()=>fs.appendFile(path.join(out,name+'.log'),data));};
  child.stdout.on('data',record);child.stderr.on('data',record);child.on('error',error=>{child.failure=error;if(!child.pid){child.spawnFailed=true;processes.delete(child);}console.error(name+': '+error.message);});child.once('exit',()=>processes.delete(child));child.flush=()=>writes;return child;
}
function ownedExited(child){return !child||child.spawnFailed===true||child.exitCode!=null||child.signalCode!=null;}
async function exitOwned(child,label){
  if(!child)return;
  if(child.parentStdin&&!ownedExited(child)){
    try{
      if(child.stdin&&!child.stdin.destroyed)child.stdin.end('stop\n');
      await poll(()=>ownedExited(child),label+' gracefully',15000);
      cloudShutdowns.push({process:child.runLabel,graceful:true});
    }catch(error){
      child.forcedShutdown=true;cloudShutdowns.push({process:child.runLabel,graceful:false});
      console.error(label+': graceful shutdown failed; using the owned-process fallback. '+error.message);
    }
  }
  if(!ownedExited(child)&&!child.killed)child.kill();
  await poll(()=>ownedExited(child),label,15000);await child.flush?.();
  if(child.forcedShutdown)throw Error(label+' required forced termination; graceful key disposal was not verified.');
}
function request(url,{method='GET',body,cookie,headers={},maximum=2*1024*1024}={}){
  assert.ok([origin,idpOrigin].includes(new URL(url).origin),'Only test HTTPS authorities');
  return new Promise((resolve,reject)=>{const data=body===undefined?null:Buffer.from(JSON.stringify(body));const req=https.request(url,{method,ca,rejectUnauthorized:true,timeout:10000,headers:{...(data?{'Content-Type':'application/json','Content-Length':data.length}:{}),...(cookie?{Cookie:cookie}:{}),...headers}},response=>{let count=0;const chunks=[];response.on('data',chunk=>{count+=chunk.length;if(count>maximum)response.destroy(Error('Bounded test HTTP response exceeded'));else chunks.push(chunk);});response.once('error',reject);response.once('end',()=>{const bytes=Buffer.concat(chunks);let json;try{json=JSON.parse(bytes.toString('utf8'));}catch{}resolve({status:response.statusCode,headers:response.headers,bytes,json});});});req.once('error',reject);req.once('timeout',()=>req.destroy(Error('HTTPS test request timed out')));if(data)req.write(data);req.end();});
}
async function waitHttp(url,label,timeout=180000){let lastLog=0;return await poll(async()=>{if(Date.now()-lastLog>5000){console.log('Waiting: '+label);lastLog=Date.now();}if(cloud&&url.startsWith(origin)&&ownedExited(cloud))throw Error('Cloud exited during startup');const result=await request(url);return result.status===200?result:null;},label,timeout);}
async function startCloud(){cloudRound++;cloud=startLogged(cloudImage,['--development',configPath,'--parent-stdin'],'cloud-'+cloudRound,{parentStdin:true});await new Promise((resolve,reject)=>{cloud.once('spawn',resolve);cloud.once('error',reject);});await waitHttp(origin+'/healthz','real OIDC cloud',30000);}
async function evaluateFetch(route,options={}){return await client.evaluate('(async()=>{const response=await fetch('+JSON.stringify(route)+','+JSON.stringify({credentials:'same-origin',cache:'no-store',...options})+');const text=await response.text();let json=null;try{json=JSON.parse(text);}catch{}return{status:response.status,json};})()');}
async function openClient(language,{clearCookies=true}={}){
  if(clearCookies)await client.send('Network.clearBrowserCookies');
  await client.send('Page.navigate',{url:origin+'/remote.html'});
  await client.until("location.origin==="+JSON.stringify(origin)+"&&!!document.querySelector('#hosted-oidc-login')&&!document.querySelector('#hosted-banner').hidden",'hosted OIDC page',30000);
  await client.focus();await client.evaluate("localStorage.setItem('rc-language',"+JSON.stringify(language)+");document.querySelector('#language').value="+JSON.stringify(language)+";document.querySelector('#language').dispatchEvent(new Event('change',{bubbles:true}));");
  await client.until("document.documentElement.lang==="+JSON.stringify(language),'selected interface language',5000);
}
async function beginSignIn(language){
  await openClient(language);authorizationNavigation=null;await client.click('#hosted-oidc-login');
  await client.until("location.origin==="+JSON.stringify(idpOrigin)+"&&!!document.querySelector('#username')&&!!document.querySelector('#password')",'actual Keycloak sign-in form',60000);
  await client.focus();
}
async function fillIdentity(user,password=user.password){await client.field('#username',user.login);await client.field('#password',password);await client.click('#kc-login');}
async function signedIn(user){await client.until("location.origin==="+JSON.stringify(origin)+"&&!!document.querySelector('#hosted-user')&&document.querySelector('#hosted-user').textContent==="+JSON.stringify(user.username),'mapped OIDC cloud identity',60000);return await evaluateFetch('/api/hosted/bootstrap');}
async function signIn(user){await beginSignIn(user.language);await fillIdentity(user);return await signedIn(user);}
async function cookie(){const all=await client.send('Network.getCookies',{urls:[origin]});const current=all.cookies.find(item=>item.name==='__Host-tlachihuani-hosted');assert.ok(current,'Opaque hosted cookie exists');return current;}
async function signedOut(){await client.click('#hosted-signout');await client.until("!document.querySelector('#hosted-oidc').hidden&&document.querySelector('#hosted-directory').hidden",'cloud sign-out',15000);const state=await evaluateFetch('/api/hosted/bootstrap');assert.equal(state.json.user,null);}

try{
  for(const candidate of [origin+'/healthz',idpOrigin+'/']){
    let refused=false;try{await request(candidate);}catch(error){if(error.code==='ECONNREFUSED')refused=true;else throw Error('The intended test port is not safely available: '+new URL(candidate).origin+' ('+error.code+').');}
    assert.ok(refused,'Test port is already occupied: '+new URL(candidate).origin);
  }
  await command('keycloak-pull','docker',['pull','quay.io/keycloak/keycloak:26.8.2'],{timeout:600000});
  const inspected=await command('keycloak-image','docker',['image','inspect','quay.io/keycloak/keycloak:26.8.2','--format','{{index .RepoDigests 0}}']);
  resolvedImage=inspected.text.trim().split(/\r?\n/).find(line=>/^quay\.io\/keycloak\/keycloak@sha256:[a-f0-9]{64}$/.test(line));assert.ok(resolvedImage,'Pinned official image digest');
  const mounts=[['realm','/opt/keycloak/data/import'],['tls','/opt/keycloak/conf/rc-test']];
  await fs.mkdir(path.join(out,'tls'),{recursive:true});await fs.copyFile(certPath,path.join(out,'tls','certificate.pem'));await fs.copyFile(keyPath,path.join(out,'tls','key.pem'));
  const created=await command('keycloak-create','docker',['create','--name',containerName,'--label','xaiht.rc-oidc-test='+runId,'--publish','127.0.0.2:12492:8443','--memory','1536m','--cpus','2','--pids-limit','256','--cap-drop','ALL','--security-opt','no-new-privileges',...mounts.flatMap(([source,target])=>['--mount','type=bind,source='+path.join(out,source)+',target='+target+',readonly']),resolvedImage,'start-dev','--import-realm','--hostname='+idpOrigin,'--hostname-strict=true','--http-enabled=false','--https-certificate-file=/opt/keycloak/conf/rc-test/certificate.pem','--https-certificate-key-file=/opt/keycloak/conf/rc-test/key.pem','--https-port=8443','--cache=local']);
  containerId=created.text.trim().split(/\r?\n/).find(line=>/^[a-f0-9]{64}$/.test(line));assert.ok(containerId,'Owned Keycloak container ID');
  await command('keycloak-start','docker',['start',containerId]);
  containerLogs=startLogged('docker',['logs','--follow',containerId],'keycloak-live');
  const discovery=await waitHttp(authority+'/.well-known/openid-configuration','Keycloak realm import and HTTPS',180000);
  assert.equal(discovery.json.issuer,authority);await fs.writeFile(path.join(out,'provider-evidence.json'),JSON.stringify({image:resolvedImage,version:'26.8.2',issuer:discovery.json.issuer,authorization_endpoint:discovery.json.authorization_endpoint,token_endpoint:discovery.json.token_endpoint,pushed_authorization_request_endpoint:discovery.json.pushed_authorization_request_endpoint,code_challenge_methods_supported:discovery.json.code_challenge_methods_supported},null,2));
  await startCloud();
  browser=await launchBrowser(out,{certificateSpkiSha256});client=await attachBrowser(browser);await client.send('Network.enable');
  client.onEvent('Network.requestWillBeSent',event=>{
    if(authorizationNavigation||event.type!=='Document')return;
    let destination;try{destination=new URL(event.request.url);}catch{return;}
    const expected=new URL(discovery.json.authorization_endpoint);
    if(destination.origin!==expected.origin||destination.pathname!==expected.pathname)return;
    const p=destination.searchParams;
    if(p.get('client_id')!==clientId)return;
    // Observe the actual initial navigation in memory. Keycloak may immediately
    // move to login-actions and remove the authorization parameters from the
    // displayed URL. Never persist OAuth state, nonce, code or request URI.
    authorizationNavigation={request_uri:p.get('request_uri'),response_type:p.get('response_type'),client_id:p.get('client_id'),redirect_uri:p.get('redirect_uri'),method:p.get('code_challenge_method'),challenge_length:p.get('code_challenge')?.length,state:!!p.get('state'),nonce:!!p.get('nonce'),scope:p.get('scope'),ui_locales:p.get('ui_locales')};
  });
  await test('OIDC01','Container publishes only the distinct loopback HTTPS authority',async()=>{const inspected=await command('keycloak-port-proof','docker',['inspect',containerId,'--format','{{json .HostConfig.PortBindings}}']);const bindings=JSON.parse(inspected.text.trim());assert.deepEqual(bindings['8443/tcp'],[{HostIp:'127.0.0.2',HostPort:'12492'}]);assert.deepEqual(Object.keys(bindings),['8443/tcp']);});
  await test('OIDC02','Real provider advertises exact issuer and S256 support',async()=>{assert.equal(discovery.json.issuer,authority);assert.ok(discovery.json.code_challenge_methods_supported.includes('S256'));assert.ok(discovery.json.authorization_endpoint.startsWith(authority+'/'));assert.ok(discovery.json.token_endpoint.startsWith(authority+'/'));});
  for(const [index,user] of users.entries()){
    const tag=user.language.toUpperCase();
    await test(tag+'01','Localized OIDC entry hides fixture-password inputs',async()=>{await openClient(user.language);assert.equal(await client.evaluate("document.querySelector('#hosted-login-form').hidden"),true);assert.equal(await client.evaluate("document.querySelector('#hosted-oidc').hidden"),false);assert.equal(await client.evaluate("document.querySelector('#hosted-oidc-login').textContent"),user.language==='es'?'Continuar para iniciar sesión':'Continue to sign in');await client.photo(tag+'-oidc-entry');});
    await test(tag+'02','Real provider accepts direct code or PAR admission for the S256-required client',async()=>{
      authorizationNavigation=null;await client.click('#hosted-oidc-login');
      await client.until("location.origin==="+JSON.stringify(idpOrigin)+"&&!!document.querySelector('#username')",'Keycloak authorization UI',60000);
      assert.ok(authorizationNavigation,'Observed the real initial provider authorization navigation');
      const fields={...authorizationNavigation,document_language:await client.evaluate('document.documentElement.lang')};
      assert.equal(fields.client_id,clientId);
      assert.equal(realm.clients[0].attributes['pkce.code.challenge.method'],'S256','Imported real provider client requires S256');
      assert.deepEqual(realm.clients[0].redirectUris,[origin+'/signin-oidc'],'Provider accepts only the exact callback');
      assert.equal(realm.clients[0].directAccessGrantsEnabled,false);assert.equal(realm.clients[0].implicitFlowEnabled,false);
      if(fields.request_uri){
        // .NET uses PAR when supported. Parameters and their nonce/state are sent
        // on the authenticated backchannel, not exposed in this browser URL.
        assert.ok(/^urn:ietf:params:oauth:request_uri:[A-Za-z0-9._~+\/-]+$/.test(fields.request_uri),'Provider issued a PAR request URI');
        assert.ok(discovery.json.pushed_authorization_request_endpoint?.startsWith(authority+'/'),'Real provider advertises its PAR endpoint');
        authorizationModes[user.language]='PAR';
      }else{
        assert.equal(fields.response_type,'code');assert.equal(fields.redirect_uri,origin+'/signin-oidc');
        assert.equal(fields.method,'S256');assert.equal(fields.challenge_length,43);assert.ok(fields.state&&fields.nonce);
        assert.equal(fields.scope,'openid');assert.equal(fields.ui_locales,user.language);authorizationModes[user.language]='direct-code';
      }
      assert.ok(fields.document_language.toLowerCase().startsWith(user.language),'Identity provider form uses the selected language');
      // EN04/ES04 complete the real UI code exchange against this S256-required
      // provider. This check does not claim PAR-hidden parameters were inspected.
    });
    await test(tag+'03','Incorrect password stays at the real identity provider and grants no cloud session',async()=>{await fillIdentity(user,'incorrect-QA-password');const invalidMessage=user.language==='es'?/(?:usuario|nombre de usuario|credenciales|contraseña).*(?:incorrect|inválid|no válid)|(?:incorrect|inválid).*(?:usuario|contraseña)/i:/invalid (username|user name).*password|invalid.*credentials|incorrect.*password/i;await client.until("location.origin==="+JSON.stringify(idpOrigin)+"&&!!document.querySelector('[aria-invalid=true]')&&"+invalidMessage.toString()+".test(document.body.innerText)",'provider displays localized invalid-credential error',20000);const appCookie=await cookie();const state=await request(origin+'/api/hosted/bootstrap',{cookie:appCookie.name+'='+appCookie.value});assert.equal(state.json.user,null);await client.photo(tag+'-wrong-password');});
    await test(tag+'04','Real UI code exchange with the S256-required provider maps the exact immutable subject',async()=>{await fillIdentity(user);const result=await signedIn(user);assert.equal(result.json.authentication_mode,'oidc');assert.equal(result.json.user.id,user.id);assert.equal(result.json.user.username,user.username);assert.equal(result.json.public_remote_enabled,false);assert.equal(result.json.post_quantum,false);await client.photo(tag+'-signed-in-directory');});
    await test(tag+'05','Directory contains only that subject’s permitted computer and account',async()=>{const result=await evaluateFetch('/api/hosted/computers');assert.equal(result.status,200);assert.equal(result.json.computers.length,1);assert.equal(result.json.computers[0].installation_id,computers[index].installation_id);assert.equal(result.json.computers[0].online,false);assert.equal(result.json.computers[0].accounts.length,1);assert.equal(result.json.computers[0].accounts[0].binding_id,computers[index].accounts[0].binding_id);assert.equal(await client.evaluate("[...document.querySelectorAll('[data-hosted-open]')].every(button=>button.disabled)"),true);});
    await test(tag+'06','Browser receives only an opaque protected cloud cookie and no OAuth tokens',async()=>{const current=await cookie();assert.ok(current.httpOnly&&current.secure);assert.equal(current.domain,'127.0.0.1');assert.equal(current.path,'/');assert.equal(current.sameSite,'Strict');const all=await client.send('Network.getCookies',{urls:[origin]});assert.ok(!all.cookies.some(c=>c.name==='__Host-tlachihuani-oidc-unused'));const keys=await client.evaluate("Object.keys(localStorage).concat(Object.keys(sessionStorage))");assert.ok(!keys.some(key=>/access.?token|refresh.?token|id.?token|oidc/i.test(key)));assert.ok(!(await client.evaluate('location.href')).includes('code='));});
    await test(tag+'07','Cloud sign-out invalidates the original session without deleting provider identity',async()=>{const old=await cookie(),providerBefore=await client.send('Network.getCookies',{urls:[authority+'/']});assert.ok(providerBefore.cookies.some(item=>item.httpOnly),'Real provider session cookie');await signedOut();const providerAfter=await client.send('Network.getCookies',{urls:[authority+'/']});for(const previous of providerBefore.cookies.filter(item=>item.httpOnly))assert.ok(providerAfter.cookies.some(current=>current.name===previous.name&&current.value===previous.value),'Cloud logout preserves the separate provider session');const denied=await request(origin+'/api/hosted/computers',{cookie:old.name+'='+old.value});assert.equal(denied.status,401);assert.equal(await client.evaluate("document.querySelector('#hosted-selected').textContent"),'');await client.photo(tag+'-signed-out');});
  }
  await test('OIDC03','Fixture password endpoint is disabled in OIDC mode',async()=>{await openClient('en');const state=await evaluateFetch('/api/hosted/bootstrap');const response=await evaluateFetch('/api/hosted/login',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-Token':state.json.csrf_token},body:JSON.stringify({username:users[0].username,password:'unused-QA-only'})});assert.equal(response.status,409);assert.equal(response.json.error.code,'fixture_login_disabled');});
  await test('OIDC04','Same-email unlinked subject cannot acquire another user’s cloud account',async()=>{await beginSignIn('es');await fillIdentity(unlinked);await client.until("location.origin==="+JSON.stringify(origin)+"&&!!document.querySelector('#hosted-status')&&document.querySelector('#hosted-status').textContent.includes('no está vinculada')",'explicit unlinked-identity recovery',60000);const state=await evaluateFetch('/api/hosted/bootstrap');assert.equal(state.json.user,null);const denied=await evaluateFetch('/api/hosted/computers');assert.equal(denied.status,401);assert.ok(!(await client.evaluate('location.href')).includes('auth_error='));await client.photo('unlinked-subject-es');});
  await test('OIDC05','Tampered callback state cannot create a cloud login',async()=>{await openClient('en');await client.send('Page.navigate',{url:origin+'/signin-oidc?code=not-an-issued-code&state=not-a-protected-state'});await client.until("location.origin==="+JSON.stringify(origin)+"&&!!document.querySelector('#hosted-status')&&document.querySelector('#hosted-status').textContent.includes('Sign-in did not complete')",'tampered state rejected',30000);const state=await evaluateFetch('/api/hosted/bootstrap');assert.equal(state.json.user,null);await client.photo('callback-state-refused');});
  await test('OIDC06','Missing callback state and unsolicited provider error fail closed',async()=>{for(const query of ['code=not-issued','error=access_denied']){await openClient('en');await client.send('Page.navigate',{url:origin+'/signin-oidc?'+query});await client.until("location.origin==="+JSON.stringify(origin)+"&&!!document.querySelector('#hosted-oidc')&&!document.querySelector('#hosted-oidc').hidden",'invalid callback returns sign-in',30000);const state=await evaluateFetch('/api/hosted/bootstrap');assert.equal(state.json.user,null);}});
  await test('OIDC07','Authenticated wrong-owner link revocation cannot remove another subject’s binding',async()=>{await signIn(users[0]);const state=await evaluateFetch('/api/hosted/bootstrap');const other=computers[1];const denied=await evaluateFetch('/api/hosted/computers/'+other.installation_id+'/accounts/'+other.accounts[0].binding_id,{method:'DELETE',headers:{'X-CSRF-Token':state.json.csrf_token}});assert.ok([403,404].includes(denied.status));});
  await test('OIDC08','Local cloud link revocation persists before restart',async()=>{const state=await evaluateFetch('/api/hosted/bootstrap'),owned=computers[0];const revoked=await evaluateFetch('/api/hosted/computers/'+owned.installation_id+'/accounts/'+owned.accounts[0].binding_id,{method:'DELETE',headers:{'X-CSRF-Token':state.json.csrf_token}});assert.equal(revoked.status,200);assert.equal(revoked.json.revoked,true);const directory=await evaluateFetch('/api/hosted/computers');assert.ok(!directory.json.computers.some(pc=>pc.accounts.some(account=>account.binding_id===owned.accounts[0].binding_id)));assert.ok((await fs.stat(statePath)).size>0);});
  await test('OIDC09','Owned cloud restart and fresh real sign-in do not resurrect a revoked link',async()=>{await exitOwned(cloud,'cloud stopped for durable-revocation test');await startCloud();const state=await signIn(users[0]);assert.equal(state.json.user.id,users[0].id);const directory=await evaluateFetch('/api/hosted/computers');assert.ok(!directory.json.computers.some(pc=>pc.accounts.some(account=>account.binding_id===computers[0].accounts[0].binding_id)));await client.photo('revocation-after-restart');});
  await test('OIDC10','Independent Spanish owner remains available after the other owner’s link revocation',async()=>{await signIn(users[1]);const directory=await evaluateFetch('/api/hosted/computers');assert.equal(directory.json.computers.length,1);assert.equal(directory.json.computers[0].installation_id,computers[1].installation_id);await signedOut();});
  await test('OIDC11','Client runs without uncaught JavaScript exceptions',async()=>{assert.deepEqual(client.errors,[]);});
}catch(error){checks.push({id:'HARNESS',name:'Actual Keycloak/HTTPS/browser setup',status:'FAIL',error:error.stack});console.error(error.stack);process.exitCode=1;await failureEvidence('HARNESS');}
finally{
  let browserClosed=!browser,cloudExited=!cloud;
  if(client){try{await client.send('Browser.close');browserClosed=true;}catch(error){console.error('Browser close: '+error.message);}client.close();}
  if(cloud)try{await exitOwned(cloud,'owned OIDC cloud cleanup');cloudExited=ownedExited(cloud);}catch(error){console.error(error.message);process.exitCode=1;}
  if(containerId){
    try{await command('keycloak-final-logs','docker',['logs',containerId],{allowFailure:true});await command('keycloak-stop','docker',['stop','--timeout','15',containerId],{timeout:30000,allowFailure:true});await command('keycloak-remove','docker',['rm','--force',containerId]);containerRemoved=true;}catch(error){console.error(error.message);process.exitCode=1;}
  }
  if(containerLogs)await exitOwned(containerLogs,'owned Docker log follower').catch(error=>{console.error(error.message);process.exitCode=1;});
  for(const ownedProcess of [...processes])await exitOwned(ownedProcess,'owned test process cleanup').catch(error=>{console.error(error.message);process.exitCode=1;});
  const cleanup={browserClosed,cloudExited,gracefulCloudShutdown:cloudShutdowns.every(item=>item.graceful),keycloakRemoved:!containerId||containerRemoved,ownedChildProcessesStopped:processes.size===0};if(Object.values(cleanup).some(value=>!value))process.exitCode=1;
  await fs.writeFile(path.join(out,'cleanup.json'),JSON.stringify(cleanup,null,2));await save();console.log('OIDC evidence: '+out);console.log(summary().passed+' passed; '+summary().failed+' failed.');
}
