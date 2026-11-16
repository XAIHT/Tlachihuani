// Tlamatini-RC · Angela López Mendoza · XAIHT
// Two explicit transports. Hosted mode is an isolated loopback development lab.
const API='/api/rc/v1';
const MAX_FRAME=128*1024, MAX_BODY=65536, MAX_FILE=64*1024*1024;
const encoder=new TextEncoder();
const allowedHeaders=new Set(['authorization','content-type','idempotency-key','if-match']);
const responseHeaders=new Set(['content-type','content-length','content-disposition','x-content-sha256','cache-control','etag']);
const isId=value=>typeof value==='string'&&/^[a-f0-9]{32}$/.test(value);
const fail=(code,message,status=409)=>Object.assign(new Error(message),{code,status});
const MAX_PENDING=4;
// The connection service answers with codes only; each gets a plain sentence.
const cloudMessages={"invalid_credentials": "Sign-in failed. Check the test username and password.", "login_busy": "Another sign-in is being checked. Wait a moment and try again.", "rate_limited": "Too many attempts. Wait a minute and try again.", "authentication_required": "Your lab sign-in ended. Sign in again.", "csrf_rejected": "This page is out of date. Reload it and try again.", "invalid_origin": "This page is out of date. Reload it and try again.", "session_limit": "Too many lab sign-ins are open. Wait a few minutes and try again.", "computer_offline": "This computer is offline. Connect Remote-Controller on this computer before trying again.", "computer_unavailable": "This computer or account is no longer available to this lab account. Refresh computers.", "connection_limit": "Too many connections are open for this lab account. Wait a minute and try again.", "cloud_stopping": "The connection service is restarting. Try again shortly.", "fixture_login_disabled": "Password sign-in is disabled for this lab. Choose “Continue to sign in”.", "invalid_device_key": "This browser key was not accepted. Choose “Set up this browser again”.", "deny_store_full": "The connection service could not save this change. Try again.", "deny_persistence_unavailable": "The connection service could not save this change. Try again.", "security_profile_unavailable": "This hosted security profile is not available.", "development_loopback_required": "This hosted security profile is not available.", "invalid_host": "This hosted security profile is not available."};
const relayMessages={"request_limit": "Too many requests are in progress. Try again.", "response_too_large": "The response exceeds the allowed size.", "service_stopping": "Remote-Controller is stopping on this computer. Try again shortly."};
const waiting=[];
function pump(){while(waiting.length&&pending.size<MAX_PENDING)waiting.shift().start();}
function dropWaiting(error){for(const waiter of waiting.splice(0))waiter.fail(error);}
const loopback=hostname=>['127.0.0.1','localhost','[::1]'].includes(hostname);
let bootstrapValue=null, selectedValue=null, relay=null, readyPromise=null, opening=null, stopReady=null, serial=0;
const pending=new Map();
const lostListeners=new Set();
function remove(id){const item=pending.get(id);if(!item)return;pending.delete(id);clearTimeout(item.timer);item.signal?.removeEventListener('abort',item.abort);queueMicrotask(pump);return item;}
function rejectRequest(id,error){const item=remove(id);if(!item)return;if(item.controller){try{item.controller.error(error);}catch{}}else item.reject(error);}
function lose(error){const old=relay;relay=null;readyPromise=null;opening=null;stopReady?.(error);stopReady=null;serial++;dropWaiting(error);if(old&&old.readyState<2)old.close(1000);for(const id of [...pending.keys()])rejectRequest(id,error);for(const listener of lostListeners)listener(error);}
function decode(data){if(typeof data!=='string'||data.length>MAX_FRAME||data.length%4!==0||!/^[A-Za-z0-9+/]*={0,2}$/.test(data))throw fail('invalid_relay_frame','Invalid relay data.');const raw=atob(data);return Uint8Array.from(raw,c=>c.charCodeAt(0));}
function handle(frame){
  if(!frame||!isId(frame.id)||!['response','chunk','end','error'].includes(frame.type))throw fail('invalid_relay_frame','Invalid relay response.');
  const item=pending.get(frame.id);if(!item)return;
  // A problem with one reply fails only that request; the link stays up.
  try{deliver(frame,item);}catch(error){const active=pending.has(frame.id);rejectRequest(frame.id,error);if(active&&relay?.readyState===WebSocket.OPEN)relay.send(JSON.stringify({type:'cancel',id:frame.id}));}
}
function deliver(frame,item){
  if(frame.type==='error'){const code=typeof frame.code==='string'?frame.code:'relay_error';rejectRequest(frame.id,fail(code,Object.hasOwn(relayMessages,code)?relayMessages[code]:'The computer could not complete this request.',Number.isInteger(frame.status)?frame.status:502));return;}
  if(frame.type==='response'){
    if(item.controller||!Number.isInteger(frame.status)||frame.status<200||frame.status>599||!frame.headers||Array.isArray(frame.headers)||typeof frame.headers!=='object')throw fail('invalid_relay_frame','Invalid relay response headers.');
    const headers=new Headers({'Cache-Control':'no-store'});for(const [key,value] of Object.entries(frame.headers)){if(responseHeaders.has(key.toLowerCase())){if(typeof value!=='string'||value.length>4096||/[\r\n]/.test(value))throw fail('invalid_relay_frame','Invalid relay response headers.');headers.set(key,value);}}
    const length=headers.get('content-length');if(length!==null&&(!/^\d+$/.test(length)||Number(length)>item.maximum))throw fail('response_too_large','The response exceeds the allowed size.');
    const body=new ReadableStream({start(controller){item.controller=controller;},cancel(){const removed=remove(frame.id);if(removed&&relay?.readyState===WebSocket.OPEN)relay.send(JSON.stringify({type:'cancel',id:frame.id}));}});
    item.status=frame.status;item.expected=length===null?null:Number(length);
    if([204,205,304].includes(frame.status)){item.controller.close();item.empty=true;item.resolve(new Response(null,{status:frame.status,headers}));}
    else item.resolve(new Response(body,{status:frame.status,headers}));
    return;
  }
  if(!item.controller)throw fail('invalid_relay_frame','Relay body arrived before its headers.');
  if(frame.type==='chunk'){
    if(item.empty)throw fail('invalid_relay_frame','Unexpected response body.');
    const bytes=decode(frame.data);item.bytes+=bytes.length;
    if(item.bytes>item.maximum||(item.expected!==null&&item.bytes>item.expected))throw fail('response_too_large','The response exceeds the allowed size.');
    item.controller.enqueue(bytes);return;
  }
  if(item.expected!==null&&item.bytes!==item.expected){rejectRequest(frame.id,fail('incomplete_response','The response was interrupted.'));return;}
  remove(frame.id);if(!item.empty)item.controller.close();
}
async function cloud(path,{method='GET',body,signal}={}){
  const response=await fetch('/api/hosted'+path,{method,body:body===undefined?undefined:JSON.stringify(body),headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(method==='GET'?{}:{'X-CSRF-Token':bootstrapValue?.csrf_token||''})},credentials:'same-origin',cache:'no-store',signal});
  const value=response.status===204?null:await response.json().catch(()=>undefined);
  if(!response.ok){const code=typeof value?.error?.code==='string'?value.error.code:response.status===429?'rate_limited':'cloud_request_failed';throw fail(code,Object.hasOwn(cloudMessages,code)?cloudMessages[code]:'The connection service is unavailable.',response.status);}
  if(value===undefined)throw fail('invalid_cloud_response','The connection service returned an invalid response.');
  return value;
}
function validateBootstrap(value){
  if(!value||value.mode!=='hosted'||value.profile!=='development-loopback'||value.post_quantum!==false||value.public_remote_enabled!==false||location.protocol!=='https:'||!loopback(location.hostname))throw fail('security_profile_unavailable','This hosted security profile is not available.');
  if(value.authentication_mode==='oidc'&&value.login_url!=='/auth/login')throw fail('invalid_cloud_response','Invalid sign-in destination.');
  if(value.authentication_mode!==undefined&&!['oidc','fixture','development-fixture'].includes(value.authentication_mode))throw fail('security_profile_unavailable','Unsupported sign-in configuration.');
  if(typeof value.csrf_token!=='string'||value.csrf_token.length<16||value.csrf_token.length>512)throw fail('invalid_cloud_response','The connection service returned an invalid session.');
  if(value.user!==null&&(!value.user||typeof value.user.id!=='string'||typeof value.user.username!=='string'))throw fail('invalid_cloud_response','The connection service returned an invalid account.');
  return value;
}
export const transport={
  get hosted(){return bootstrapValue?.mode==='hosted';},
  get bootstrap(){return bootstrapValue;},
  get selected(){return selectedValue;},
  onLost(listener){lostListeners.add(listener);return()=>lostListeners.delete(listener);},
  async initialize(){
    const response=await fetch('/api/hosted/bootstrap',{credentials:'same-origin',cache:'no-store'});
    if(response.status===404){bootstrapValue={mode:'local'};return bootstrapValue;}
    if(!response.ok)throw fail('cloud_unavailable','The connection service is unavailable.');
    bootstrapValue=validateBootstrap(await response.json());return bootstrapValue;
  },
  async refreshIdentity(){bootstrapValue=validateBootstrap(await cloud('/bootstrap'));return bootstrapValue;},
  async login(username,password){await cloud('/login',{method:'POST',body:{username,password}});return this.refreshIdentity();},
  async logout(){try{await cloud('/logout',{method:'POST'});}finally{this.close();selectedValue=null;bootstrapValue={...bootstrapValue,user:null};}return this.refreshIdentity();},
  async computers(){const value=await cloud('/computers');if(!Array.isArray(value?.computers)||value.computers.length>100)throw fail('invalid_cloud_response','Invalid computer directory.');for(const computer of value.computers){if(!isId(computer.installation_id)||typeof computer.name!=='string'||typeof computer.online!=='boolean'||!Array.isArray(computer.accounts)||computer.accounts.length>100)throw fail('invalid_cloud_response','Invalid computer directory.');for(const account of computer.accounts)if(!isId(account.binding_id)||!['english','spanish'].includes(account.edition)||typeof account.display_name!=='string')throw fail('invalid_cloud_response','Invalid computer directory.');}return value.computers;},
  select(computer,account){this.close();if(!this.hosted||!isId(computer?.installation_id)||!isId(account?.binding_id)||!computer.accounts.some(a=>a.binding_id===account.binding_id))throw fail('invalid_selection','Choose a linked account.');selectedValue={installation_id:computer.installation_id,binding_id:account.binding_id,name:computer.name,edition:account.edition,display_name:account.display_name};},
  clearSelection(){this.close();selectedValue=null;},
  credentialKey(){return this.hosted?(selectedValue?location.origin+'|'+selectedValue.installation_id+'|'+selectedValue.binding_id:null):location.origin;},
  open(publicKey){
    if(!this.hosted)return Promise.resolve();
    if(opening)return opening;
    if(readyPromise)return readyPromise;
    if(!selectedValue||!bootstrapValue.user)return Promise.reject(fail('selection_required','Sign in and choose a computer first.'));
    if(typeof publicKey!=='string'||publicKey.length>1024)return Promise.reject(fail('invalid_key','A browser key is required.'));
    const expected={...selectedValue},generation=serial;
    const task=(async()=>{
      const admitted=await cloud('/connections',{method:'POST',body:{installation_id:expected.installation_id,binding_id:expected.binding_id,device_public_key:publicKey,profile:'development-loopback'}});
      if(generation!==serial||selectedValue?.binding_id!==expected.binding_id)throw fail('selection_changed','The selected computer changed.');
      if(typeof admitted?.ticket!=='string'||admitted.ticket.length<16||admitted.ticket.length>1024)throw fail('invalid_cloud_response','Invalid connection admission.');
      const ws=new WebSocket(location.origin.replace(/^http/,'ws')+'/relay/client','tlachihuani.relay.v1');relay=ws;
      readyPromise=new Promise((resolve,reject)=>{
        let settled=false;
        const finishError=error=>{if(settled)return;settled=true;clearTimeout(timer);reject(error);};
        const timer=setTimeout(()=>{const error=fail('connection_timeout','The computer did not respond.');finishError(error);if(relay===ws)lose(error);},10000);
        stopReady=finishError;
        ws.onopen=()=>{if(relay===ws)ws.send(JSON.stringify({ticket:admitted.ticket}));};
        ws.onmessage=event=>{if(relay!==ws)return;try{
          if(typeof event.data!=='string'||encoder.encode(event.data).length>MAX_FRAME)throw fail('invalid_relay_frame','Invalid relay frame.');
          const frame=JSON.parse(event.data);
          if(!settled){
            if(frame.type!=='ready'||frame.binding_id!==expected.binding_id||frame.profile!=='development-loopback')throw fail('invalid_relay_peer','The connected computer did not match the selected account.');
            settled=true;clearTimeout(timer);stopReady=null;resolve();return;
          }
          handle(frame);
        }catch(error){finishError(error);lose(error);}};
        ws.onerror=()=>{};
        ws.onclose=()=>{const error=fail('connection_lost','The computer connection was interrupted.');finishError(error);if(relay===ws)lose(error);};
      });
      return readyPromise;
    })();
    opening=task;task.then(()=>{if(opening===task)opening=null;},()=>{if(opening===task)opening=null;});return task;
  },
  request(path,init={}){
    if(!this.hosted){init.onSent?.();return fetch(API+path,init);}
    if(!relay||relay.readyState!==WebSocket.OPEN||!readyPromise)return Promise.reject(fail('connection_lost','Choose a computer and reconnect.'));
    if(typeof path!=='string'||!path.startsWith('/')||path.includes('#')||/[\r\n\\]/.test(path)||path.length>4096)return Promise.reject(fail('invalid_path','Invalid request destination.'));
    const body=init.body??null;if(body!==null&&(typeof body!=='string'||encoder.encode(body).length>MAX_BODY))return Promise.reject(fail('request_too_large','The request is too large.',413));
    const method=(init.method||'GET').toUpperCase();if(!['GET','POST','PUT','PATCH','DELETE','HEAD'].includes(method))return Promise.reject(fail('invalid_method','Unsupported request method.'));
    const headers={};for(const [key,value] of new Headers(init.headers||{})){if(!allowedHeaders.has(key.toLowerCase()))return Promise.reject(fail('invalid_header','Unsupported request header.'));headers[key]=value;}
    const id=crypto.randomUUID().replaceAll('-','');
    return new Promise((resolve,reject)=>{
      if(init.signal?.aborted){reject(new DOMException('Request cancelled.','AbortError'));return;}
      const start=()=>{
      if(!relay||relay.readyState!==WebSocket.OPEN||!readyPromise){reject(fail('connection_lost','Choose a computer and reconnect.'));return;}
      const item={resolve,reject,signal:init.signal,bytes:0,maximum:path.startsWith('/response-file?')?MAX_FILE:8*1024*1024};
      item.abort=()=>{if(!pending.has(id))return;if(relay?.readyState===WebSocket.OPEN)relay.send(JSON.stringify({type:'cancel',id}));rejectRequest(id,new DOMException('Request cancelled.','AbortError'));};
      item.timer=setTimeout(item.abort,path.startsWith('/response-file?')?120000:30000);
      pending.set(id,item);init.signal?.addEventListener('abort',item.abort,{once:true});
      if(init.signal?.aborted){item.abort();return;}
      try{relay.send(JSON.stringify({type:'request',id,method,path:API+path,headers,body}));}catch(error){rejectRequest(id,error);return;}
      // Only now is the request on its way: the caller's reply clock starts here.
      try{init.onSent?.();}catch{}
      };
      if(pending.size<MAX_PENDING&&!waiting.length){start();return;}
      // Wait for a free slot instead of refusing: replies loading in the
      // background must not make the person's own request fail.
      let timer;
      const finish=()=>{clearTimeout(timer);init.signal?.removeEventListener('abort',cancelWait);const index=waiting.indexOf(waiter);if(index>=0)waiting.splice(index,1);};
      const waiter={start:()=>{finish();start();},fail:error=>{finish();reject(error);}};
      const cancelWait=()=>waiter.fail(new DOMException('Request cancelled.','AbortError'));
      timer=setTimeout(()=>waiter.fail(fail('connection_busy','Too many requests are in progress, so this one was not sent. Try again.',429)),30000);
      init.signal?.addEventListener('abort',cancelWait,{once:true});waiting.push(waiter);
    });
  },
  validateInfo(info){
    if(info?.protocol_major!==1||info.mode!=='live'||info.security_profile!=='local-only')throw fail('security_profile_unavailable','This client does not support this service security profile.');
    if(this.hosted){
      let origin;try{origin=new URL(info.authentication_origin);}catch{throw fail('invalid_relay_peer','Invalid computer authentication origin.');}
      if(info.installation_id!==selectedValue?.installation_id||!loopback(origin.hostname)||!['http:','https:'].includes(origin.protocol)||origin.origin!==info.authentication_origin)throw fail('invalid_relay_peer','The selected computer identity did not match.');
    }
    return info;
  },
  challengeOrigin(info){return this.hosted?info.authentication_origin:location.origin;},
  close(){const old=relay;relay=null;readyPromise=null;opening=null;stopReady?.(fail('selection_changed','The selected computer changed.'));stopReady=null;serial++;dropWaiting(fail('selection_changed','The selected computer changed.'));if(old){old.onclose=null;old.close(1000);}for(const id of [...pending.keys()])rejectRequest(id,fail('selection_changed','The selected computer changed.'));}
};
