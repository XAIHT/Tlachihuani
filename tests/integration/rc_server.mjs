// Real RC host / HTTP / authenticated native-pipe checks. No installed services are changed.
// Run from a verified visible console. Child host output remains on that console.
import {spawn} from 'node:child_process';
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {generateKeyPairSync,sign,randomUUID} from 'node:crypto';
import assert from 'node:assert/strict';
import http from 'node:http';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const output=path.join(root,'artifacts','rc-implementation','http-'+Date.now());
const image=process.env.TL_RC_TEST_IMAGE || path.join(root,'src','Tlachihuani.RC.Server','bin','Release','net10.0-windows','Tlachihuani.RC.Server.exe');
const nonce=randomUUID().replaceAll('-','');
const port=12483,origin=`http://127.0.0.1:${port}`,api=origin+'/api/rc/v1';
await fs.mkdir(output,{recursive:true});
const host=spawn(image,['--console',path.join(output,'host'),nonce,String(port)],{cwd:root,stdio:'inherit',windowsHide:false});
await new Promise((resolve,reject)=>{host.once('spawn',resolve);host.once('error',reject);});
const results=[];
let failures=0,token,deviceId,binding,invitation,enrollment,challenge,receipt,schedule;
const adapter=randomUUID().replaceAll('-','');
const key=generateKeyPairSync('ec',{namedCurve:'prime256v1'});
const publicKey=key.publicKey.export({type:'spki',format:'der'}).toString('base64');
async function native(body){return new Promise((resolve,reject)=>{const proc=spawn(image,['--dev-control',nonce,String(host.pid)],{stdio:['pipe','pipe','inherit'],windowsHide:false});let output='';proc.stdout.on('data',data=>{output+=data;if(output.length>131072){proc.kill();reject(new Error('Oversize control response'));}});proc.once('error',reject);proc.once('exit',code=>{try{const value=JSON.parse(output);if(code!==0||value.error)reject(Object.assign(new Error(value.error?.message||'Native request failed'),{code:value.error?.code}));else resolve(value);}catch(error){reject(error);}});proc.stdin.end(JSON.stringify(body)+'\n');});}
async function call(route,{method='GET',body,auth=token,headers={}}={}){const response=await fetch(api+route,{method,headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(auth?{Authorization:'Bearer '+auth}:{}),...headers},body:body===undefined?undefined:typeof body==='string'?body:JSON.stringify(body),signal:AbortSignal.timeout(8000)});const text=await response.text();let json;try{json=JSON.parse(text);}catch{}return {status:response.status,json,text,headers:response.headers};}
async function test(name,run){const began=Date.now();try{await run();console.log('PASS HTTP '+name);results.push({name,passed:true,milliseconds:Date.now()-began});}catch(error){failures++;console.error('FAIL HTTP '+name+' :: '+error.message);results.push({name,passed:false,error:error.stack});}}
const expect=async(promise,status,code)=>{const result=await promise;assert.equal(result.status,status,result.text);if(code)assert.equal(result.json?.error?.code,code);return result;};
async function waitReady(){for(let attempt=0;attempt<100;attempt++){if(host.exitCode!==null)throw new Error('Host exited before readiness');try{if((await call('/health',{auth:null})).json?.ready)return;}catch{}await new Promise(r=>setTimeout(r,150));}throw new Error('Host did not become ready');}
try{
 await waitReady();
 await test('001 readiness belongs to the running host',async()=>assert.equal((await call('/health')).json.ready,true));
 await test('002 protocol and live mode reported',async()=>{const r=await call('/info');assert.equal(r.json.protocol_major,1);assert.equal(r.json.mode,'live');});
 await test('003 local profile never claims post-quantum protection',async()=>{const r=await call('/info');assert.equal(r.json.security_profile,'local-only');assert.equal(r.json.post_quantum,false);});
 await test('004 client HTML is contained in the host',async()=>{const r=await fetch(origin);assert.equal(r.status,200);assert.match(await r.text(),/Tlamatini-RC/);});
 await test('005 client module served with correct MIME',async()=>{const r=await fetch(origin+'/remote.js');assert.equal(r.status,200);assert.match(r.headers.get('content-type'),/javascript/);});
 await test('006 strict CSP prevents inline script execution',async()=>{const r=await fetch(origin);assert.match(r.headers.get('content-security-policy'),/script-src 'self'/);assert.doesNotMatch(r.headers.get('content-security-policy'),/unsafe-inline/);});
 await test('007 private responses are never cacheable',async()=>assert.equal((await call('/info')).headers.get('cache-control'),'no-store'));
 await test('008 no permissive CORS header',async()=>assert.equal((await call('/info')).headers.get('access-control-allow-origin'),null));
 await test('009 unknown Host refused',async()=>{const status=await new Promise((resolve,reject)=>{const req=http.get(api+'/health',{headers:{Host:'attacker.invalid'}},response=>{response.resume();response.on('end',()=>resolve(response.statusCode));});req.on('error',reject);});assert.equal(status,400);});
 await test('010 foreign Origin refused',()=>expect(call('/health',{headers:{Origin:'https://attacker.invalid'}}),403,'invalid_origin'));
 await test('011 cross-site fetch refused',()=>expect(call('/health',{headers:{'Sec-Fetch-Site':'cross-site'}}),403,'invalid_origin'));
 await test('012 unknown asset not served',async()=>assert.equal((await fetch(origin+'/state/state.json')).status,404));
 await test('013 settings never served',async()=>assert.equal((await fetch(origin+'/settings/configuration.json')).status,404));
 await test('014 service manifest never served',async()=>assert.equal((await fetch(origin+'/installation.json')).status,404));
 await test('015 assets reject mutation',async()=>assert.equal((await fetch(origin+'/remote.js',{method:'POST'})).status,405));
 await test('016 snapshot requires authentication',()=>expect(call('/snapshot',{auth:null}),401,'unauthorized'));
 await test('017 schedules require authentication',()=>expect(call('/schedules',{auth:null}),401,'unauthorized'));
 await test('018 operation submission requires authentication',()=>expect(call('/operations',{method:'POST',auth:null,body:{operation:'application.start',payload:{}}}),401,'unauthorized'));
 await test('019 channel ticket requires authentication',()=>expect(call('/connection-ticket',{method:'POST',auth:null}),401,'unauthorized'));
 await test('020 session query requires authentication',()=>expect(call('/session',{auth:null}),401,'unauthorized'));
 await test('021 only JSON enrollment is accepted',()=>expect(call('/enrollments',{method:'POST',auth:null,body:'x',headers:{'Content-Type':'text/plain'}}),415,'unsupported_media_type'));
 await test('022 unknown JSON fields refused',()=>expect(call('/enrollments',{method:'POST',auth:null,body:{executable:'cmd.exe'}}),400,'invalid_request'));
 await test('023 native operations are closed',async()=>await assert.rejects(native({op:'execute',command:'cmd.exe'}),{code:'unknown_operation'}));
 await test('024 native account binding is durable',async()=>{binding=await native({op:'bind-account',edition:'english',account_id:randomUUID().replaceAll('-',''),display_name:'HTTP QA account',adapter_id:adapter});assert.equal(binding.edition,'english');});
 await test('025 native invitation creates bounded enrollment',async()=>{invitation=await native({op:'invite',binding_id:binding.id,scopes:['monitor','chat','launch','schedules']});assert.equal(invitation.invitation.length,64);});
 await test('026 public device request remains pending',async()=>{enrollment=(await expect(call('/enrollments',{method:'POST',auth:null,body:{invitation:invitation.invitation,name:'HTTP QA device',public_key:publicKey}}),202)).json;assert.equal(enrollment.fingerprint.length,64);});
 await test('027 invitation cannot be reused',()=>expect(call('/enrollments',{method:'POST',auth:null,body:{invitation:invitation.invitation,name:'Replay',public_key:publicKey}}),403,'invalid_invitation'));
 await test('028 polling requires the poll secret',()=>expect(call('/enrollments/poll',{method:'POST',auth:null,body:{id:enrollment.id,poll_secret:'0'.repeat(64)}}),404,'enrollment_unavailable'));
 await test('029 incorrect local fingerprint cannot approve',async()=>await assert.rejects(native({op:'approve',enrollment_id:enrollment.id,fingerprint:'wrong'}),{code:'identity_changed'}));
 await test('030 exact local fingerprint approves the key',async()=>{deviceId=(await native({op:'approve',enrollment_id:enrollment.id,fingerprint:enrollment.fingerprint})).id;assert.equal(deviceId.length,32);});
 await test('031 approved identity available only with poll secret',async()=>assert.equal((await call('/enrollments/poll',{method:'POST',auth:null,body:{id:enrollment.id,poll_secret:enrollment.poll_secret}})).json.device_id,deviceId));
 await test('032 challenge binds origin and device',async()=>{challenge=(await expect(call('/sessions/challenges',{method:'POST',auth:null,body:{device_id:deviceId}}),200)).json;const text=Buffer.from(challenge.challenge,'base64').toString();assert.ok(text.includes(origin));assert.ok(text.includes(deviceId));});
 await test('033 signed challenge establishes a session',async()=>{const signature=sign('sha256',Buffer.from(challenge.challenge,'base64'),{key:key.privateKey,dsaEncoding:'ieee-p1363'}).toString('base64');const r=await expect(call('/sessions',{method:'POST',auth:null,body:{device_id:deviceId,challenge_id:challenge.id,signature}}),200);token=r.json.access_token;assert.equal(token.length,64);});
 await test('034 challenge replay refused',()=>expect(call('/sessions',{method:'POST',auth:null,body:{device_id:deviceId,challenge_id:challenge.id,signature:''}}),401,'authentication_failed'));
 await test('035 session omits local Windows identity',async()=>{const r=await call('/session');assert.equal(r.json.binding.id,binding.id);assert.doesNotMatch(r.text,/windows_sid|adapter_id|S-1-/);});
 await test('036 snapshot separates running service from disconnected application',async()=>{const r=await call('/snapshot');assert.equal(r.json.service.health,'ready');assert.equal(r.json.application.state,'offline');assert.equal(r.json.application.connected,false);});
 await test('037 arbitrary operation rejected',()=>expect(call('/operations',{method:'POST',headers:{'Idempotency-Key':'bad'},body:{operation:'process.kill',payload:{pid:123}}}),400,'unknown_operation'));
 await test('038 browser executable path rejected',()=>expect(call('/operations',{method:'POST',headers:{'Idempotency-Key':'bad'},body:{operation:'application.start',payload:{path:'cmd.exe'}}}),400,'invalid_request'));
 await test('039 admission produces accepted receipt',async()=>{receipt=(await expect(call('/operations',{method:'POST',headers:{'Idempotency-Key':'one'},body:{operation:'application.start',payload:{}}}),202)).json;assert.equal(receipt.state,'accepted');});
 await test('040 same request key returns the same receipt',async()=>assert.equal((await call('/operations',{method:'POST',headers:{'Idempotency-Key':'one'},body:{operation:'application.start',payload:{}}})).json.id,receipt.id));
 await test('041 request key cannot change meaning',()=>expect(call('/operations',{method:'POST',headers:{'Idempotency-Key':'one'},body:{operation:'conversation.submit',payload:{text:'different'}}}),409,'idempotency_conflict'));
 await test('042 only the XT broker consumes an application launch',async()=>{assert.equal((await native({op:'adapter-next',adapter_id:adapter})).operation,null);assert.equal((await native({op:'broker-next'})).operation.id,receipt.id);});
 await test('043 wrong adapter cannot complete work',async()=>await assert.rejects(native({op:'adapter-complete',adapter_id:randomUUID().replaceAll('-',''),operation_id:receipt.id,result:{state:'running'}}),{code:'forbidden'}));
 await test('044 XT broker completion becomes visible',async()=>{await native({op:'broker-complete',operation_id:receipt.id,result:{state:'running',source:'explicit QA broker fixture'}});assert.equal((await call('/operations/'+receipt.id)).json.state,'succeeded');});
 await test('045 invalid schedule timezone refused',()=>expect(call('/schedules',{method:'POST',body:{name:'QA',operation:'application.start',payload:{},time_zone:'Missing/Zone',local_time:'09:00',days:[1]}}),400,'invalid_schedule'));
 await test('046 schedule stores a service-owned revision',async()=>{schedule=(await expect(call('/schedules',{method:'POST',body:{name:'QA daily',operation:'application.start',payload:{},time_zone:'UTC',local_time:'09:00',days:[0,1,2,3,4,5,6]}}),201)).json;assert.equal(schedule.revision,1);});
 await test('047 schedule delete requires a revision',()=>expect(call('/schedules/'+schedule.id,{method:'DELETE'}),428,'precondition_required'));
 await test('048 stale schedule revision refused',()=>expect(call('/schedules/'+schedule.id,{method:'DELETE',headers:{'If-Match':'9'}}),412,'revision_conflict'));
 await test('049 matching schedule revision removes it',()=>expect(call('/schedules/'+schedule.id,{method:'DELETE',headers:{'If-Match':'1'}}),204));
 await test('050 events report actual operation completion',async()=>assert.ok((await call('/events?after=0')).json.events.some(e=>e.kind==='operation.completed'&&e.operation_id===receipt.id)));
 await test('051 logout invalidates the old bearer token',async()=>{await expect(call('/session',{method:'DELETE'}),204);await expect(call('/session'),401,'unauthorized');});
 await test('052 device revocation blocks new challenge',async()=>{await native({op:'revoke',device_id:deviceId});await expect(call('/sessions/challenges',{method:'POST',auth:null,body:{device_id:deviceId}}),401,'device_unavailable');});
 await test('053 graceful stop exits the owned host',async()=>{await native({op:'development-stop'});await new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(new Error('Host shutdown timeout')),12000);if(host.exitCode!==null){clearTimeout(timer);return resolve();}host.once('exit',()=>{clearTimeout(timer);resolve();});});assert.equal(host.exitCode,0);});
 await test('054 stopped host closes the listener',async()=>await assert.rejects(fetch(api+'/health',{signal:AbortSignal.timeout(2000)})));
}catch(error){failures++;console.error(error);results.push({name:'suite infrastructure',passed:false,error:error.stack});}
finally{if(host.exitCode===null){try{await native({op:'development-stop'});}catch{}await new Promise(r=>setTimeout(r,1000));if(host.exitCode===null)host.kill();}await fs.writeFile(path.join(output,'results.json'),JSON.stringify({total:results.length,passed:results.filter(r=>r.passed).length,failed:failures,results},null,2));console.log(`RC HTTP checks: ${results.filter(r=>r.passed).length}/${results.length} passed. Evidence: ${output}`);}
process.exitCode=failures?1:0;
