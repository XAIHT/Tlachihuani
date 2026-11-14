// Tlachihuani · Angela López Mendoza · XAIHT
// Isolated gateway security checks, NOT model-prompt acceptance. Run visibly.
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs/promises';
import http from 'node:http';
import net from 'node:net';
import path from 'node:path';
import {root,poll,sleep,launchBrowser,attachBrowser} from './rc_installed_driver.mjs';
const image=path.resolve(process.argv[2]);
assert.ok(image.startsWith(path.join(root,'artifacts','release-builds')+path.sep));
assert.ok(image.endsWith('Tlachihuani.RC.Server.exe'));
const stamp=crypto.randomUUID().replaceAll('-','');
const output=path.join(root,'artifacts','rc-mobile-gateway-checks',stamp);
await fs.mkdir(output,{recursive:true});
const reserve=net.createServer();await new Promise(r=>reserve.listen(0,'127.0.0.1',r));const port=reserve.address().port;await new Promise(r=>reserve.close(r));
const origin='http://127.0.0.1:'+port;
const account=crypto.randomUUID().replaceAll('-',''),adapter=crypto.randomUUID().replaceAll('-','');
let binding,identityMode='normal',identityEdition='english',protectedCalls=0,server,keepFresh=false,holdIdentity=null,renewalPage,renewalDevice;
const protectedCookies=[],upstreamSockets=new Set(),clientSockets=[];
const upstream=http.createServer(async(request,response)=>{
 const url=new URL(request.url,'http://127.0.0.1');
 if(url.pathname==='/agent/remote-controller/session/'){
   response.setHeader('Content-Type','application/json');
   if(!request.headers.cookie?.includes('fixture_session=')){response.writeHead(401);response.end(JSON.stringify({authenticated:false}));return;}
   if(holdIdentity){const held=holdIdentity;holdIdentity=null;held.observed();await held.wait;}
   if(identityMode==='oversize'){response.end(' '.repeat(5000));return;}
   if(identityMode==='malformed'){response.end('{broken');return;}
   if(identityMode==='incomplete'){response.end('{"authenticated":true}');return;}
   response.end(JSON.stringify({authenticated:true,active:identityMode!=='inactive',enabled:identityMode!=='disabled',
     account_id:identityMode==='account'||request.headers.cookie.includes('fixture_session=other')?'f'.repeat(32):account,binding_id:identityMode==='binding'?'e'.repeat(32):binding.id,
     edition:identityMode==='edition'?(identityEdition==='english'?'spanish':'english'):identityEdition}));return;
 }
 if(url.pathname==='/'&&request.method==='POST'){
   for await(const chunk of request)void chunk;
   response.setHeader('Set-Cookie',['fixture_session='+(url.searchParams.get('switch')==='other'?'other':'normal')+'; Path=/; HttpOnly; SameSite=Strict','csrftoken=fixture_rotated_csrf; Path=/; SameSite=Strict','tlm_csrf_es_mx=fixture_spanish_rotated; Path=/; SameSite=Strict']);response.writeHead(302,{Location:'/agent/agent/'});response.end();return;
 }
 if(url.pathname==='/'){response.setHeader('Set-Cookie',['csrftoken=fixture_csrf; Path=/; SameSite=Strict','tlm_csrf_es_mx=fixture_spanish_csrf; Path=/; SameSite=Strict']);response.setHeader('Content-Type','text/html');response.end('<html lang="en"><head><title>Isolated login fixture</title><link href="/static/login.css" rel="stylesheet"></head><body><form action="/" method="post"><button id="fixture-login">Sign in to isolated fixture</button></form></body></html>');return;}
 if(url.pathname.startsWith('/static/')){response.setHeader('Content-Type','text/css');response.end('body{background:url(/static/image.png)}');return;}
 if(url.pathname==='/agent/paste_image/'){
   const chunks=[];for await(const chunk of request)chunks.push(chunk);const body=Buffer.concat(chunks);
   response.setHeader('Content-Type','application/json');response.end(JSON.stringify({length:request.headers['content-length'],type:request.headers['content-type'],body:body.toString('base64'),csrf:request.headers['x-csrftoken']}));return;
 }
 if(url.pathname==='/agent/canvas/read/'){
   response.setHeader('Content-Type','text/html');response.setHeader('Content-Disposition','attachment; filename="document.html"');response.end('<html><head></head><body><a href="/unchanged">saved document</a></body></html>');return;
 }
 protectedCalls++;protectedCookies.push(request.headers.cookie||'');response.setHeader('Content-Type','text/html');response.end('<html><head><title>Isolated application fixture</title></head><body><header>Application heading</header><a href="/agent/torrenter/workspace/">Downloads</a><header>Dialog heading</header></body></html>');
});
upstream.on('upgrade',(request,socket)=>{
 upstreamSockets.add(socket);socket.once('close',()=>upstreamSockets.delete(socket));
 const accept=crypto.createHash('sha1').update(request.headers['sec-websocket-key']+'258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
 socket.write('HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: '+accept+'\r\n\r\n');
 socket.on('error',()=>{});socket.on('data',()=>{});
});
await new Promise(r=>upstream.listen(0,'127.0.0.1',r));
const upstreamPort=upstream.address().port;
const results=[];
async function native(body){return new Promise((resolve,reject)=>{
 const child=spawn(image,['--dev-control',stamp,String(server.pid)],{stdio:['pipe','pipe','inherit'],windowsHide:false});
 let out='';child.stdout.on('data',x=>out+=x);child.once('error',reject);child.once('exit',code=>{try{const value=JSON.parse(out);if(code||value.error)throw new Error(value.error?.message||'Native fixture control failed');resolve(value);}catch(error){reject(error);}});child.stdin.end(JSON.stringify(body)+'\n');
});}
async function heartbeat(webPort=upstreamPort){await native({op:'adapter-heartbeat',adapter_id:adapter,ready:true,capabilities:{execution_options:true,self_modify_available:true,self_modify_active:false,self_modify_locked:false,model:'isolated fixture',web_port:webPort}});}
async function api(route,{method='GET',body,token,headers={}}={}){
 const response=await fetch(origin+'/api/rc/v1'+route,{method,redirect:'manual',headers:{...(body?{'Content-Type':'application/json'}:{}),...(token?{Authorization:'Bearer '+token}:{}),...headers},body:body?JSON.stringify(body):undefined});
 const data=response.status===204?null:await response.json();return{response,data};
}
async function device(scopes){
 const keys=crypto.generateKeyPairSync('ec',{namedCurve:'prime256v1'});
 const invitation=await native({op:'invite',binding_id:binding.id,scopes});
 const enrollment=await api('/enrollments',{method:'POST',body:{invitation:invitation.invitation,name:'Isolated gateway test',public_key:keys.publicKey.export({type:'spki',format:'der'}).toString('base64')}});
 assert.equal(enrollment.response.status,202);
 const approved=await native({op:'approve',enrollment_id:enrollment.data.id,fingerprint:enrollment.data.fingerprint});
 async function login(){const challenge=(await api('/sessions/challenges',{method:'POST',body:{device_id:approved.id}})).data;
 const signed=crypto.sign('sha256',Buffer.from(challenge.challenge,'base64'),{key:keys.privateKey,dsaEncoding:'ieee-p1363'}).toString('base64');
 const session=await api('/sessions',{method:'POST',body:{device_id:approved.id,challenge_id:challenge.id,signature:signed}});assert.equal(session.response.status,200);return session.data;}
 return{...approved,login,session:await login()};
}
async function check(name,fn){try{if(keepFresh)await heartbeat();await fn();results.push({name,passed:true});console.log('PASS GATEWAY '+name);}catch(error){results.push({name,passed:false,error:error.stack});console.error('FAIL GATEWAY '+name+' '+error.message);throw error;}finally{await fs.writeFile(path.join(output,'results.json'),JSON.stringify({kind:'isolated security boundary; not real prompts',total:results.length,passed:results.filter(x=>x.passed).length,results},null,2));}}
async function connectSocket(prefix,cookie){return await new Promise((resolve,reject)=>{
 const request=http.request(origin+prefix+'/ws/agent/',{headers:{Cookie:cookie,Origin:origin,Connection:'Upgrade',Upgrade:'websocket','Sec-WebSocket-Key':crypto.randomBytes(16).toString('base64'),'Sec-WebSocket-Version':'13'}});
 const timer=setTimeout(()=>{request.destroy();reject(new Error('Live channel handshake timeout'));},5000);
 request.on('error',error=>{clearTimeout(timer);reject(error);});request.on('response',response=>{clearTimeout(timer);response.resume();reject(new Error('Live channel HTTP '+response.statusCode));});
 request.on('upgrade',(response,socket)=>{clearTimeout(timer);const channel={socket,closed:false};socket.on('data',()=>{});socket.on('error',()=>{});socket.on('close',()=>channel.closed=true);clientSockets.push(channel);resolve(channel);});request.end();
});}
try{
 await fs.mkdir(path.join(output,'state/settings'),{recursive:true});
 await fs.writeFile(path.join(output,'state/settings/configuration.json'),JSON.stringify({port,public_origin:origin,session_minutes:1}));
 server=spawn(image,['--console',path.join(output,'state'),stamp,String(port)],{stdio:'inherit',windowsHide:false,env:{...process.env,TEMP:output,TMP:output,TMPDIR:output}});
 await new Promise((r,j)=>{server.once('spawn',r);server.once('error',j);});
 await poll(async()=>(await api('/health')).data.ready,'isolated gateway startup',30000);
 binding=await native({op:'bind-account',edition:'english',account_id:account,display_name:'Gateway fixture',adapter_id:adapter});
 const limited=await device(['monitor','chat']);const owner=await device(['monitor','workspace']);
 assert.ok(Date.parse(owner.session.expires_at)-Date.now()<=61000&&Date.parse(owner.session.expires_at)-Date.now()>45000,'Regression host uses real one-minute sessions');
 await check('workspace requires authentication',async()=>assert.equal((await api('/workspace',{method:'POST'})).response.status,401));
 await check('chat grant does not become full workspace',async()=>assert.equal((await api('/workspace',{method:'POST',token:limited.session.access_token})).response.status,403));
 await check('application heartbeat is mandatory',async()=>assert.equal((await api('/workspace',{method:'POST',token:owner.session.access_token})).response.status,409));
 await heartbeat(port);
 await check('gateway cannot target itself',async()=>assert.equal((await api('/workspace',{method:'POST',token:owner.session.access_token})).response.status,409));
 await heartbeat(12064);
 await check('gateway cannot target the Jobs service',async()=>assert.equal((await api('/workspace',{method:'POST',token:owner.session.access_token})).response.status,409));
 await heartbeat();
 const opened=await api('/workspace',{method:'POST',token:owner.session.access_token});assert.equal(opened.response.status,200);
 keepFresh=true;
 const prefix=opened.data.url.replace('/agent/agent/','');const cookie=opened.response.headers.get('set-cookie').split(';')[0];
 const request=(route,init={})=>fetch(origin+prefix+route,{redirect:'manual',...init,headers:{Cookie:cookie,...init.headers}});
 await check('workspace URL contains no bearer credentials',async()=>{assert.match(prefix,/^\/workspace\/[a-f0-9]{32}$/);assert.ok(!opened.data.url.includes(owner.session.access_token));assert.match(opened.response.headers.get('set-cookie'),/httponly/i);assert.match(opened.response.headers.get('set-cookie'),/samesite=strict/i);});
 await check('routing ID alone does not authenticate',async()=>assert.equal((await fetch(origin+prefix+'/agent/agent/')).status,401));
 await check('normal application login returns directly to chat',async()=>{const response=await request('/agent/agent/');assert.equal(response.status,302);assert.equal(response.headers.get('location'),prefix+'/?next=%2Fagent%2Fagent%2F');});
 await check('login resources stay inside the account workspace',async()=>{const text=await(await request('/')).text();assert.ok(text.includes(prefix+'/static/login.css'));assert.ok(text.includes('mobile-transport.js'));assert.ok(text.includes('data-edition="english"'));});
 await check('normal same-origin login forms retain a verifiable Origin',async()=>assert.equal((await request('/')).headers.get('referrer-policy'),'same-origin'));
 identityMode='account';
 await check('login to another account is refused',async()=>{const before=protectedCalls;assert.equal((await request('/',{method:'POST'})).status,403);assert.equal(protectedCalls,before);});
 identityMode='normal';
 await check('normal verified login redirects within workspace',async()=>{const response=await request('/',{method:'POST'});assert.equal(response.status,302);assert.equal(response.headers.get('location'),prefix+'/agent/agent/');});
 await check('rotated CSRF token is scoped to the workspace while the app session remains private',async()=>{const response=await request('/',{method:'POST'});const cookies=response.headers.getSetCookie();assert.equal(cookies.length,1);assert.ok(cookies[0].startsWith('csrftoken=fixture_rotated_csrf;'));assert.ok(cookies[0].includes('path='+prefix+'/'));assert.match(cookies[0],/samesite=strict/i);assert.doesNotMatch(cookies[0],/httponly|fixture_session/i);});
 await check('approved account reaches its real backend route',async()=>{const response=await request('/agent/agent/');assert.equal(response.status,200);assert.ok((await response.text()).includes(prefix+'/agent/torrenter/workspace/'));});
 await check('HTML headers do not inject duplicate mobile transports',async()=>{const text=await(await request('/agent/agent/')).text();assert.equal((text.match(/src="\/mobile-transport\.js"/g)||[]).length,1);assert.ok(text.includes('<header>Application heading</header>'));assert.ok(text.includes('<header>Dialog heading</header>'));});
 const multipart=Buffer.from('--mobile-test\r\nContent-Disposition: form-data; name="image"; filename="image.png"\r\nContent-Type: image/png\r\n\r\nimage-bytes\r\n--mobile-test--\r\n');
 for(const chunked of [false,true])await check((chunked?'chunked':'known-length')+' multipart uploads keep their body and Django content length',async()=>{
   const observed=await new Promise((resolve,reject)=>{const raw=http.request(origin+prefix+'/agent/paste_image/',{method:'POST',headers:{Cookie:cookie,Origin:origin,'Content-Type':'multipart/form-data; boundary=mobile-test',...(chunked?{}:{'Content-Length':String(multipart.length)})}},response=>{let body='';response.on('data',x=>body+=x);response.on('end',()=>{try{assert.equal(response.statusCode,200);resolve(JSON.parse(body));}catch(error){reject(error);}});});raw.on('error',reject);if(chunked)raw.write(multipart);raw.end(chunked?undefined:multipart);});
   assert.equal(Number(observed.length),multipart.length);assert.equal(observed.body,multipart.toString('base64'));assert.equal(observed.type,'multipart/form-data; boundary=mobile-test');
 });
 await check('downloaded HTML is byte-preserved instead of receiving the mobile shell',async()=>{
   const response=await request('/agent/canvas/read/');assert.equal(response.status,200);assert.match(response.headers.get('content-disposition'),/attachment/);assert.equal(await response.text(),'<html><head></head><body><a href="/unchanged">saved document</a></body></html>');
 });
 await check('concurrent login cannot change a verified request account',async()=>{
   let release,observed;const wait=new Promise(r=>release=r),seen=new Promise(r=>observed=r);holdIdentity={wait,observed};
   const protectedRequest=request('/agent/agent/');await seen;
   const switched=request('/?switch=other',{method:'POST'});await sleep(150);release();
   assert.equal((await protectedRequest).status,200);assert.ok(protectedCookies.at(-1).includes('fixture_session=normal'));
   assert.equal((await switched).status,403);assert.equal((await request('/',{method:'POST'})).status,302);
 });
 for(const [mode,label]of[['account','account identity'],['binding','binding identity'],['edition','edition identity'],['disabled','disabled profile'],['inactive','inactive user']]){
  identityMode=mode;await check(label+' is checked for every application request',async()=>{const before=protectedCalls;assert.equal((await request('/agent/agent/')).status,403);assert.equal(protectedCalls,before);});
 }
 identityMode='oversize';await check('identity response is bounded',async()=>assert.equal((await request('/agent/agent/')).status,502));identityMode='normal';
 for(const mode of ['malformed','incomplete']){identityMode=mode;await check(mode+' upstream identity fails closed',async()=>assert.equal((await request('/agent/agent/')).status,502));}identityMode='normal';
 await check('live channel closes when the normal account is disabled',async()=>{const channel=await connectSocket(prefix,cookie);assert.equal(channel.closed,false);identityMode='disabled';await poll(()=>channel.closed,'disabled live channel closes',8000);identityMode='normal';});
 await check('excluded admin panel cannot be proxied',async()=>assert.equal((await request('/admin/')).status,404));
 await check('excluded Agentic Control Panel cannot be proxied',async()=>assert.equal((await request('/agent/agentic_control_panel/')).status,404));
 await check('excluded Prompt Flow Panel cannot be proxied',async()=>assert.equal((await request('/agent/prompt_flow_panel/')).status,404));
 for(const picker of ['pick_context_directory','pick_backup_directory','pick_db_sqlite_file'])await check('phone picker cannot open '+picker+' on the host',async()=>assert.equal((await request('/agent/'+picker+'/',{method:'POST'})).status,404));
 await check('cross-origin mutations are refused',async()=>assert.equal((await request('/agent/agent/',{method:'POST',headers:{Origin:'https://untrusted.invalid'}})).status,403));
 await check('unrecognized Host is refused',async()=>{
  // Fetch implementations may replace Host with the URL's authority. Use a
  // raw HTTP request so this check actually sends the hostile header.
  const status=await new Promise((resolve,reject)=>{const raw=http.request(origin+prefix+'/agent/agent/',{headers:{Host:'untrusted.invalid',Cookie:cookie}},response=>{response.resume();resolve(response.statusCode);});raw.on('error',reject);raw.end();});assert.equal(status,400);
 });
 await heartbeat();
 await check('workspace can renew only as its exact device',async()=>assert.equal((await request('/renew-session',{method:'POST',headers:{Authorization:'Bearer '+limited.session.access_token}})).status,403));
 const renewed=await owner.login();
 const retained=await connectSocket(prefix,cookie);
 await check('signed renewal keeps the normal login and live channel',async()=>{assert.equal((await request('/renew-session',{method:'POST',headers:{Authorization:'Bearer '+renewed.access_token}})).status,200);assert.equal((await api('/session',{token:owner.session.access_token})).response.status,401);assert.equal((await request('/agent/agent/')).status,200);await sleep(1500);assert.equal(retained.closed,false);});
 await check('device revocation closes workspace and live channel',async()=>{await native({op:'revoke',device_id:owner.id});assert.equal((await request('/agent/agent/')).status,401);await poll(()=>retained.closed,'revoked live channel closes',5000);});
 keepFresh=false;
 await check('Spanish uses its own rotated CSRF cookie for browser forms and upstream headers',async()=>{
   const spanishAdapter=crypto.randomUUID().replaceAll('-','');identityEdition='spanish';
   binding=await native({op:'bind-account',edition:'spanish',account_id:account,display_name:'Spanish gateway fixture',adapter_id:spanishAdapter});
   await native({op:'adapter-heartbeat',adapter_id:spanishAdapter,ready:true,capabilities:{web_port:upstreamPort,execution_options:true,model:'isolated fixture'}});
   const spanish=await device(['monitor','workspace']);const opened=await api('/workspace',{method:'POST',token:spanish.session.access_token});assert.equal(opened.response.status,200);
   const spanishPrefix=opened.data.url.replace('/agent/agent/',''),proof=opened.response.headers.getSetCookie()[0].split(';')[0];
   const response=await fetch(origin+spanishPrefix+'/',{method:'POST',redirect:'manual',headers:{Cookie:proof,Origin:origin}});assert.equal(response.status,302);
   const cookies=response.headers.getSetCookie();assert.equal(cookies.length,1);assert.ok(cookies[0].startsWith('tlm_csrf_es_mx=fixture_spanish_rotated;'));assert.ok(cookies[0].includes('path='+spanishPrefix+'/'));assert.doesNotMatch(cookies[0],/fixture_session/i);
   const uploaded=await fetch(origin+spanishPrefix+'/agent/paste_image/',{method:'POST',headers:{Cookie:proof,Origin:origin},body:'spanish upload'});assert.equal(uploaded.status,200);assert.equal((await uploaded.json()).csrf,'fixture_spanish_rotated');
 });
 identityEdition='english';identityMode='normal';
 binding=await native({op:'bind-account',edition:'english',account_id:account,display_name:'Gateway fixture',adapter_id:adapter});
 keepFresh=true;
 await check('headed workspace renews near expiry and across real one-minute session lifetimes',async()=>{
   renewalPage=await attachBrowser(await launchBrowser(path.join(output,'renewal-browser')));
   await renewalPage.send('Network.enable');
   await renewalPage.navigate(origin+'/remote.html#security');
   await renewalPage.until('!!document.querySelector("#enroll")','isolated RC enrollment');
   const invite=await native({op:'invite',binding_id:binding.id,scopes:['monitor','workspace']});
   await renewalPage.field('#device-name','Visible renewal fixture');await renewalPage.field('#invitation',invite.invitation);await renewalPage.click('#enroll');
   await renewalPage.until('document.querySelector("#fingerprint").textContent.includes("Compare in XT:")','real device challenge fingerprint');
   const pending=(await native({op:'pending-enrollments'})).enrollments.filter(e=>e.binding_id===binding.id&&e.name==='Visible renewal fixture');
   assert.equal(pending.length,1);assert.ok(await renewalPage.evaluate(`document.querySelector('#fingerprint').textContent.includes(${JSON.stringify(pending[0].fingerprint)})`));
   renewalDevice=await native({op:'approve',enrollment_id:pending[0].id,fingerprint:pending[0].fingerprint});
   await renewalPage.until('document.querySelector("#connection").textContent==="Connected"','real signed device session');
   const alive=async(seconds,step)=>{
     const until=Date.now()+seconds*1000;
     while(Date.now()<until){await heartbeat();await renewalPage.focus();await fs.writeFile(path.join(output,'renewal-progress.json'),JSON.stringify({step,remaining_seconds:Math.ceil((until-Date.now())/1000)}));await sleep(Math.min(5000,Math.max(0,until-Date.now())));}
   };
   await alive(45,'aging the actual one-minute RC session');
   await renewalPage.evaluate("location.hash='chat'");await renewalPage.click('#open-workspace');
   await renewalPage.until('!!document.querySelector("#fixture-login")','normal isolated upstream login');
   await renewalPage.click('#fixture-login');
   await renewalPage.until('!!window.TlamatiniMobile&&!document.querySelector("#fixture-login")','authenticated workspace');
   await alive(75,'remaining usable beyond the original and first renewed session expirations');
   assert.equal(await renewalPage.evaluate('fetch("/agent/agent/").then(r=>r.status)'),200);
   assert.equal(await renewalPage.evaluate('!!document.querySelector("#mobile-renewal-error")'),false);
   assert.ok(renewalPage.responses.filter(r=>r.path==='/api/rc/v1/sessions'&&r.status===200).length>=3,'Initial login and repeated actual signed renewals');
   assert.deepEqual(renewalPage.errors,[]);await renewalPage.photo('renewal-after-expiry');
 });
 await check('automatic renewal cannot restore a revoked browser grant',async()=>{
   await native({op:'revoke',device_id:renewalDevice.id});
   assert.equal(await renewalPage.evaluate('window.TlamatiniMobile.renew().then(()=>false,()=>true)'),true);
   assert.ok(await renewalPage.evaluate('document.querySelector("#mobile-renewal-error")?.textContent.length>0'));
 });
 console.log('ISOLATED GATEWAY CHECKS PASSED:',results.length);
}catch(error){console.error(error);process.exitCode=1;}
finally{
 if(renewalPage)renewalPage.close();
 for(const channel of clientSockets)channel.socket.destroy();for(const socket of upstreamSockets)socket.destroy();
 if(server){try{await native({op:'development-stop'});}catch{}await Promise.race([new Promise(r=>server.once('exit',r)),sleep(10000)]);if(server.exitCode===null)server.kill();}
 upstream.closeAllConnections();await new Promise(r=>upstream.close(r));
 console.log('Gateway evidence: '+output);
}
