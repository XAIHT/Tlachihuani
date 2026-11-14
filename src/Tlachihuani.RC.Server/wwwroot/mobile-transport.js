// Tlamatini-RC · Angela López Mendoza · XAIHT
// Rebase application traffic into the fixed, approved account workspace.
(() => {
  'use strict';
  if (window.TlamatiniMobile) return;
  const boot = document.currentScript;
  const prefix = boot.dataset.workspace;
  const upstream = new URL(boot.dataset.upstream);
  const local = new URL(location.href);
  function route(value) {
    if (typeof value !== 'string' || !value || /^(#|data:|blob:|mailto:|tel:|javascript:)/i.test(value)) return value;
    let url; try { url = new URL(value, document.baseURI); } catch { return value; }
    if (![local.host, upstream.host].includes(url.host) || !['http:', 'https:', 'ws:', 'wss:'].includes(url.protocol)) return value;
    if (url.pathname.startsWith(prefix + '/') || /^\/mobile-(transport\.js|workspace\.(js|css))$/.test(url.pathname)) return value;
    const ws = url.protocol.startsWith('ws');
    return (ws ? local.protocol === 'https:' ? 'wss:' : 'ws:' : local.protocol) + '//' + local.host + prefix + url.pathname + url.search + url.hash;
  }
  const originalFetch = window.fetch;
  let renewalTask=null, renewalDue=0;
  function renew() {
    if(!renewalTask)renewalTask=renewNow().finally(()=>{renewalTask=null;});
    return renewalTask;
  }
  function ensureSession() {
    return renewalTask || (Date.now()>=renewalDue ? renew() : Promise.resolve());
  }
  async function renewNow() {
    let fresh;
    const api=async(path,body,token)=>{
      const response=await originalFetch('/api/rc/v1'+path,{method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},body:JSON.stringify(body),signal:AbortSignal.timeout(10000)});
      const value=await response.json();if(!response.ok)throw new Error(value.error?.message||'Connection renewal failed.');return value;
    };
    try{
      const identity=await new Promise((resolve,reject)=>{const open=indexedDB.open('tlachihuani-rc-device-v1',1);open.onerror=()=>reject(new Error('Device storage unavailable.'));open.onsuccess=()=>{const db=open.result;const tx=db.transaction('identity','readonly');const get=tx.objectStore('identity').get(location.origin);get.onsuccess=()=>resolve(get.result);get.onerror=()=>reject(get.error);tx.oncomplete=()=>db.close();};});
      if(!identity?.deviceId||!identity.key)throw new Error('Reconnect your approved device in RC.');
      const challenge=await api('/sessions/challenges',{device_id:identity.deviceId});
      const bytes=Uint8Array.from(atob(challenge.challenge),value=>value.charCodeAt(0));
      const parts=new TextDecoder('utf-8',{fatal:true}).decode(bytes).split('\n');
      if(parts.length!==7||parts[0]!=='Tlachihuani-RC login v1'||parts[2]!==location.origin||parts[3]!==identity.deviceId||parts[4]!==challenge.id)throw new Error('Connection challenge could not be verified.');
      const signature=await crypto.subtle.sign({name:'ECDSA',hash:'SHA-256'},identity.key,bytes);
      fresh=await api('/sessions',{device_id:identity.deviceId,challenge_id:challenge.id,signature:btoa(String.fromCharCode(...new Uint8Array(signature)))});
      const response=await originalFetch(prefix+'/renew-session',{method:'POST',credentials:'same-origin',cache:'no-store',headers:{Authorization:'Bearer '+fresh.access_token},signal:AbortSignal.timeout(10000)});
      if(!response.ok)throw new Error('Return to RC and reopen this workspace.');
      const expires=Date.parse(fresh.expires_at);
      renewalDue=Number.isFinite(expires)?expires-30000:Date.now()+30000;
      document.querySelector('#mobile-renewal-error')?.remove();
    }catch(error){
      if(fresh)await originalFetch('/api/rc/v1/session',{method:'DELETE',headers:{Authorization:'Bearer '+fresh.access_token}}).catch(()=>{});
      if(document.body&&!document.querySelector('#mobile-renewal-error')){const banner=document.createElement('p');banner.id='mobile-renewal-error';banner.setAttribute('role','status');banner.textContent=error.message;document.body.prepend(banner);}
      throw error;
    }
  }
  window.TlamatiniMobile = Object.freeze({prefix, route, renew, edition:boot.dataset.edition});
  // Sign a fresh challenge with the already approved, non-exportable device key.
  // Keep an active workspace usable without silently extending a revoked grant.
  // An RC connection may already be near expiry when this page opens. Renew
  // immediately, then use the actual expiry instead of a page-relative timer.
  // Navigation, returning from the background and concurrent requests share
  // one signed renewal. A mutation is never automatically replayed.
  void ensureSession().catch(()=>{});
  setInterval(()=>{if(document.visibilityState==='visible')void ensureSession().catch(()=>{});},15000);
  document.addEventListener('visibilitychange',()=>{if(document.visibilityState==='visible')void ensureSession().catch(()=>{});});
  window.addEventListener('pageshow',()=>{void ensureSession().catch(()=>{});});

  window.fetch = async function(input, init) {
    await ensureSession();
    const raw = input instanceof Request ? input.url : String(input);
    const path = new URL(route(raw), document.baseURI).pathname.slice(prefix.length);
    if (window.TlamatiniMobilePicker && ['/agent/pick_context_directory/','/agent/pick_backup_directory/','/agent/pick_db_sqlite_file/'].includes(path)) {
      const selected = await window.TlamatiniMobilePicker({file:path.includes('sqlite')});
      return new Response(JSON.stringify(selected ? {path:selected} : {canceled:true}), {status:200,headers:{'Content-Type':'application/json'}});
    }
    if (path === '/agent/canvas/save/' && init?.body instanceof FormData && !init.body.get('token') && window.TlamatiniMobilePicker) {
      const selected = await window.TlamatiniMobilePicker({saveName:init.body.get('file')?.name || 'untitled.txt'});
      if(!selected)return new Response(JSON.stringify({saved:false,canceled:true}),{status:200,headers:{'Content-Type':'application/json'}});
      const body=new FormData();for(const [key,value]of init.body)body.append(key,value);body.set('token',selected.token);body.set('revision',selected.revision);
      return originalFetch.call(this,route('/agent/mobile/canvas/save/'),{...init,body});
    }
    if (input instanceof Request) return originalFetch.call(this, new Request(route(input.url), input), init);
    return originalFetch.call(this, route(String(input)), init);
  };
  const open = XMLHttpRequest.prototype.open;
  XMLHttpRequest.prototype.open = function(method, url, ...rest) { return open.call(this, method, route(String(url)), ...rest); };
  const NativeSocket = window.WebSocket;
  window.WebSocket = class extends NativeSocket {
    constructor(url, protocols) { super(route(String(url)), ...(protocols === undefined ? [] : [protocols])); }
  };
  if(window.Worker){const NativeWorker=window.Worker;window.Worker=class extends NativeWorker{constructor(url,options){super(route(String(url)),options);}};}
  if(window.EventSource){const NativeEvents=window.EventSource;window.EventSource=class extends NativeEvents{constructor(url,options){super(route(String(url)),options);}};}
  const beacon=navigator.sendBeacon?.bind(navigator);if(beacon)navigator.sendBeacon=(url,data)=>beacon(route(String(url)),data);
  const nativeOpen = window.open;
  window.open = function(url, target, features) {
    const mapped = route(String(url || ''));
    if (mapped.includes(local.host + prefix + '/')) { location.assign(mapped); return window; }
    return nativeOpen.call(window, url, target, features);
  };
  const attrs = ['src', 'href', 'action', 'param', 'data-viewer-url'];
  // Rebase URL setters before the browser starts loading dynamically created
  // scripts, frames and images. A MutationObserver alone runs too late for
  // those requests; it remains a fallback for parsed fragments below.
  const setAttribute=Element.prototype.setAttribute;
  Element.prototype.setAttribute=function(name,value){return setAttribute.call(this,name,attrs.includes(String(name).toLowerCase())?route(String(value)):value);};
  for(const [type,key]of [[HTMLScriptElement,'src'],[HTMLImageElement,'src'],[HTMLIFrameElement,'src'],[HTMLMediaElement,'src'],[HTMLSourceElement,'src'],[HTMLLinkElement,'href'],[HTMLAnchorElement,'href'],[HTMLFormElement,'action']]){
    const descriptor=Object.getOwnPropertyDescriptor(type.prototype,key);
    if(descriptor?.set&&descriptor.configurable)Object.defineProperty(type.prototype,key,{...descriptor,set(value){descriptor.set.call(this,route(String(value)));}});
  }
  function rebase(element) {
    if (!(element instanceof Element)) return;
    for (const attr of attrs) {
      const value = element.getAttribute(attr);
      if (!value || value.startsWith('#')) continue;
      const mapped = route(value);
      if (mapped !== value) element.setAttribute(attr, mapped);
    }
  }
  const observer = new MutationObserver(records => {
    for (const record of records) {
      if (record.type === 'attributes') rebase(record.target);
      for (const node of record.addedNodes) if (node instanceof Element) {
        rebase(node); node.querySelectorAll('[src],[href],[action],[param],[data-viewer-url]').forEach(rebase);
      }
    }
  });
  observer.observe(document.documentElement, {subtree:true, childList:true, attributes:true, attributeFilter:attrs});
  document.addEventListener('click', event => {
    const link = event.target.closest?.('a[href]');
    if (!link || link.getAttribute('href').startsWith('#')) return;
    rebase(link);
    if (link.href.includes(local.host + prefix + '/')) link.removeAttribute('target');
  }, true);
  document.addEventListener('submit', event => rebase(event.target), true);
})();
