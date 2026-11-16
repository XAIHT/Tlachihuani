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
import {root,poll,sleep,launchBrowser,attachBrowser} from '../integration/rc_installed_driver.mjs';

const guid=()=>randomUUID().replaceAll('-','');
const origin='https://127.0.0.1:12490',idpOrigin='https://127.0.0.2:12492';
const out=path.resolve(process.env.TL_RC_VIDEO_OUTPUT||path.join(root,'artifacts/rc-hosted-video',String(Date.now())));
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

export {fs,path,https,assert,spawn,randomUUID,randomBytes,createHash,root,poll,sleep,launchBrowser,attachBrowser,guid,origin,idpOrigin,out,cloudImage,certPath,keyPath,pfxPath,pfxPassword,certificate,ca,certificateSha256,certificateSpkiSha256,runId,realmName,containerName,authority,clientId,users,configuration,configPath,realm,childEnv,command,startLogged,ownedExited,exitOwned,request,waitHttp};
export async function bootCloud(){await startCloud();return cloud;}
