// Tlamatini-RC · Angela López Mendoza · XAIHT
// Live client with an explicit, isolated hosted-development transport.
import {transport} from './remote-transport.js';
import {t,language,applyLanguage,setLanguage} from './remote-i18n.js';
import {createHostedUI} from './remote-hosted.js';
const $=selector=>document.querySelector(selector);
const API='/api/rc/v1';
let credential,session,identity,socket,expiryTimer,enrolling=false,connecting=false,refreshing=false,resetting=false,approvalProblem=null,cursor=0;
let pendingSubmission=null,executionCapabilities=null,modesInitialized=false,selfModifyTouched=false,accountCapabilitiesSupported=false;
let applicationState="unknown",lifecycleSubmitting=null,selectionEpoch=0;
const identityTransactions=new Set();let hostedKeyGeneration=null;
const selectionChanged=()=>Object.assign(new Error('The selected computer changed.'),{code:'selection_changed'});
function identityContext(){return {epoch:selectionEpoch,storageKey:transport.credentialKey()};}
function requireIdentityContext(context){if(context.epoch!==selectionEpoch||context.storageKey!==transport.credentialKey())throw selectionChanged();if(!context.storageKey)throw new Error('Sign in and choose a computer first.');}
function advanceSelection(){selectionEpoch++;for(const transaction of identityTransactions){try{transaction.abort();}catch{}}return selectionEpoch;}
const modeLabels={multi_turn_enabled:"Multiturn",exec_report_enabled:"Exec-Report",acpx_enabled:"ACPX",self_modify_enabled:"Self-Modify",ask_execs_enabled:"Ask Execs",step_by_step_enabled:"Step by step"};
function executionSummary(options){return t('Executed with: ')+Object.entries(options).filter(([,on])=>on).map(([key])=>t(modeLabels[key]||key)).join(' · ');}
function capabilityHelp(){return has('chat')&&!has('monitor')&&!accountCapabilitiesSupported?'Update Tlachihuani-RC Server to use Conversation without View status.':'Start the updated Tlamatini application to use execution modes.';}
function selectedModes(){if(!executionCapabilities?.execution_options)throw new Error(capabilityHelp());return Object.fromEntries([...document.querySelectorAll("[data-mode]")].filter(e=>!e.disabled&&(e.dataset.mode!=="self_modify_enabled"||selfModifyTouched)).map(e=>[e.dataset.mode,e.checked]));}
function modeControls(){const supported=!!executionCapabilities?.execution_options&&has("chat");for(const input of document.querySelectorAll("[data-mode]")){input.disabled=!supported||(input.dataset.mode==="self_modify_enabled"&&(!executionCapabilities.self_modify_available||executionCapabilities.self_modify_locked));}$("#mode-state").textContent=!supported?capabilityHelp():executionCapabilities.self_modify_locked?"This model cannot currently fit Self-Modify. Choose a larger model in Models.":!executionCapabilities.self_modify_available?"Self-Modify needs a Tlamatini build that includes its source.":"Applied to your next message. Self-Modify follows Tlamatini’s shared setting.";}
function observeCapabilities(value){executionCapabilities=value||null;if(value&&!selfModifyTouched){$('[data-mode="self_modify_enabled"]').checked=!!value.self_modify_active;modesInitialized=true;}controls();}
for(const input of document.querySelectorAll("[data-mode]"))input.addEventListener("change",()=>{const multi=$('[data-mode="multi_turn_enabled"]');const needsMulti=["exec_report_enabled","acpx_enabled","ask_execs_enabled","step_by_step_enabled"];if(input.dataset.mode==="self_modify_enabled")selfModifyTouched=true;if(needsMulti.includes(input.dataset.mode)&&input.checked)multi.checked=true;if(input===multi&&!multi.checked)for(const key of needsMulti)$('[data-mode="'+key+'"]').checked=false;});
const receipts=new Map();
const encoder=new TextEncoder();
const b64=bytes=>btoa(String.fromCharCode(...new Uint8Array(bytes)));
const un64=text=>Uint8Array.from(atob(text),c=>c.charCodeAt(0));
const id=()=>crypto.randomUUID().replaceAll('-','');
const has=scope=>!!session?.scopes.includes(scope);
const operationName=kind=>t(({'conversation.history':'Recent conversation','conversation.submit':'Chat request','conversation.cancel':'Cancel request','conversation.permission':'Execution approval','application.start':'Start Tlamatini','application.stop':'Stop Tlamatini','application.status':'Application status'})[kind]||'Request');
function resultText(receipt){const value=receipt.result||{};if(value.cancelled)return 'Conversation cancelled.';if(receipt.kind==='conversation.history')return value.messages?.length?value.messages.map(m=>(m.role==='assistant'?'Tlamatini':'You')+': '+m.text).join('\n\n')+(value.truncated?'\n\nEarlier content is available in Tlamatini.':''):'No saved conversation messages yet.';if(value.text)return value.text;if(value.message)return value.message;if(value.resolved)return 'Execution decision received.';if(value.cancellation_requested)return 'Cancellation requested. Waiting for the conversation to stop.';if(receipt.kind==='application.stop')return value.status==='already_stopped'?'Tlamatini was already stopped.':'Tlamatini has stopped.';if(receipt.kind==='application.start')return value.ready?'Tlamatini is ready.':value.status==='starting'?'Tlamatini started. Its account is still loading.':'Tlamatini is running; its account connection is not ready yet.';return 'Request completed.';}
const notice=text=>{$('#notice').textContent=t(text);};
function connectionError(error){
  if(error.code==='device_unavailable')return 'This browser’s approval was removed or is no longer known. Choose “Set up this browser again”, then create a new invitation in XT.';
  if(error.code==='account_unavailable')return 'Remote access for this account is disabled. Enable your account in Tlamatini, then choose “Set up this browser again” and create a new invitation in XT.';
  if(error.code==='invalid_invitation')return 'This invitation is invalid, expired or already used. In XT → Remote-Controller → Access, create a new invitation and copy it here.';
  if(error.code==='enrollment_unavailable')return 'This approval request expired or is no longer available. Create a new invitation in XT and try again.';
  if(transport.hosted&&((error instanceof TypeError&&/fetch|network|load failed/i.test(error.message))||error.code==='connection_lost'))return 'The computer connection was interrupted.';
  if(error instanceof TypeError&&/fetch|network|load failed/i.test(error.message))return 'Cannot reach Remote-Controller on this PC. In XT → Remote-Controller → Runtime, start the server, wait for “ready”, then retry.';
  return error.message;
}
function entry(text){text=t(text);const li=document.createElement('li');li.textContent=`${new Date().toLocaleTimeString()} · ${text}`;$('#activity-list').prepend(li);while($('#activity-list').children.length>100)$('#activity-list').lastChild.remove();}
function message(text,kind,label){$('.empty')?.remove();const item=document.createElement('div');item.className='message '+kind;const title=document.createElement('small');title.textContent=t(label);const body=document.createElement('div');body.className='response-body';body.textContent=kind==='user'?text:t(text);item.append(title,body);$('#messages').append(item);item.scrollIntoView({block:'nearest'});return item;}

const responseLanguage=()=>identity?.binding.edition==='spanish'?'es':'en';
const responseText=(en,es)=>responseLanguage()==='es'?es:en;
const receiptErrors={
  multiturn_required:["Turn on Multiturn to use Exec-Report, ACPX, Ask Execs or Step by step.","Activa Multiturno para usar Exec-Report, ACPX, Ask Execs o Paso a paso."],
  account_busy:["Tlamatini is still answering another request for this account. Wait for it to finish, or cancel it.","Tlamatini todavía responde otra solicitud de esta cuenta. Espera a que termine o cancélala."],
  self_modify_too_small:["This model cannot fit Self-Modify. Turn Self-Modify off or choose a larger model.","Este modelo no puede usar Self-Modify. Desactiva Self-Modify o elige un modelo más grande."],
  self_modify_unavailable:["This Tlamatini build cannot use Self-Modify. Turn Self-Modify off.","Esta compilación de Tlamatini no puede usar Self-Modify. Desactiva Self-Modify."],
  account_unavailable:["Remote access for this account is disabled. Enable it in Tlamatini.","El acceso remoto de esta cuenta está deshabilitado. Habilítalo en Tlamatini."],
  account_disabled:["Remote access for this account was turned off before this request ran.","El acceso remoto de esta cuenta se desactivó antes de ejecutar esta solicitud."],
  application_unavailable:["Tlamatini is not ready. Start it, then try again.","Tlamatini no está listo. Inícialo e inténtalo de nuevo."],
  model_unavailable:["Tlamatini could not load its model. Check Config → Models, then try again.","Tlamatini no pudo cargar su modelo. Revisa Config → Models e inténtalo de nuevo."],
  context_unavailable:["The saved context could not be loaded. Load it again in Tlamatini.","No se pudo cargar el contexto guardado. Cárgalo de nuevo en Tlamatini."],
  authorization_changed:["This device's permission changed while the request ran. Send it again.","El permiso de este dispositivo cambió durante la solicitud. Envíala de nuevo."],
  permission_expired:["That execution approval has expired.","Esa aprobación de ejecución ya expiró."],
  user_cancelled:["Request cancelled.","Solicitud cancelada."],
  chat_no_answer:["Tlamatini finished without an answer. Try again.","Tlamatini terminó sin respuesta. Inténtalo de nuevo."],
  chat_failed:["Tlamatini could not answer this request. Check its log, then try again.","Tlamatini no pudo responder esta solicitud. Revisa su registro e inténtalo de nuevo."],
  invalid_execution_options:["These execution modes are not supported. Reload this page.","Estos modos de ejecución no son compatibles. Vuelve a cargar esta página."]
};
function receiptError(receipt){if(receipt.state==="outcome_unknown")return responseText("The result of this request is unknown. Check Tlamatini before sending it again.","No se conoce el resultado de esta solicitud. Revisa Tlamatini antes de enviarla de nuevo.");const code=receipt.error||"";const known=Object.hasOwn(receiptErrors,code)?receiptErrors[code]:null;if(known)return responseText(known[0],known[1]);if(receipt.state==="cancelled"&&!code)return responseText("Request cancelled.","Solicitud cancelada.");return code?responseText("The request did not complete ("+code+").","La solicitud no terminó ("+code+")."):responseText("The request did not complete.","La solicitud no terminó.");}
const activeDownloads=new Set();
async function downloadResponseFile(ticket,filename){
  if(!session||!has('chat'))throw new Error(responseText('Reconnect before saving a file.','Vuelve a conectarte antes de guardar un archivo.'));
  const owner=identity.binding.id, token=session.access_token;
  const controller=new AbortController();activeDownloads.add(controller);
  let timeout=null;const sent=()=>{if(timeout===null)timeout=setTimeout(()=>controller.abort(),120000);};
  try{
    const response=await transport.request('/response-file?ticket='+encodeURIComponent(ticket),{
      headers:{Authorization:'Bearer '+token},credentials:'omit',cache:'no-store',signal:controller.signal,onSent:sent
    });
    if(!response.ok){
      const result=await response.json().catch(()=>null);
      const messages={
        file_expired:['This file link expired. Load recent conversation and try again.','Este enlace caducó. Carga la conversación reciente e inténtalo de nuevo.'],
        file_changed:['The file changed after this reply. Ask Tlamatini to create a new attachment.','El archivo cambió después de esta respuesta. Pide a Tlamatini que genere un adjunto nuevo.'],
        file_missing:['This file is no longer available in Tlamatini.','Este archivo ya no está disponible en Tlamatini.'],
        file_forbidden:['This file link is no longer authorized. Load recent conversation to refresh eligible links.','Este enlace ya no está autorizado. Carga la conversación reciente para renovar los enlaces disponibles.'],
        file_too_large:['This file exceeds the 64 MiB limit.','Este archivo supera el límite de 64 MiB.'],
        application_unavailable:['Start Tlamatini before saving the file.','Inicia Tlamatini antes de guardar el archivo.']
      };
      if(response.status===401)clearSession(responseText('Reconnect to download the file.','Vuelve a conectarte para descargar el archivo.'));
      const pair=messages[result?.error?.code];
      throw new Error(pair?responseText(...pair):responseText('The file could not be downloaded. Try again.','No se pudo descargar el archivo. Inténtalo de nuevo.'));
    }
    await window.TlamatiniDownload.save(response,filename,{language:responseLanguage(),signal:controller.signal,
      authorized:()=>!!session&&identity?.binding.id===owner&&session.access_token===token});
  }catch(error){if(error.name==='AbortError')throw new Error(responseText('The download was interrupted. Try again.','La descarga se interrumpió. Inténtalo de nuevo.'));if(error instanceof TypeError)throw new Error(responseText('The download connection failed. Try again.','Falló la conexión de descarga. Inténtalo de nuevo.'));throw error;}
  finally{clearTimeout(timeout);activeDownloads.delete(controller);}
}
const responseWorkers=[Promise.resolve(),Promise.resolve(),Promise.resolve()];let responseWorker=0;
function loadCompleteResponse(ticket,binding){
  const slot=responseWorker++%responseWorkers.length;
  const next=responseWorkers[slot].then(()=>{
    if(!session||identity?.binding.id!==binding)throw new Error('Connection changed.');
    return request('/response-content?ticket='+encodeURIComponent(ticket));
  });
  responseWorkers[slot]=next.catch(()=>{});return next;
}
function renderResponse(target,value){
  const generation=Symbol('response');target.responseGeneration=generation;
  if(value.content_ticket){
    const binding=identity?.binding.id;
    const fallback={...value,content_ticket:undefined};
    renderResponse(target,fallback);target.responseGeneration=generation;
    const status=document.createElement('p');status.className='muted';status.textContent=responseText('Loading the complete response…','Cargando la respuesta completa…');target.append(status);
    void loadCompleteResponse(value.content_ticket,binding).then(full=>{
      if(target.responseGeneration!==generation||!session||identity?.binding.id!==binding||!target.isConnected)return;
      if(full.format!=='html'||typeof full.html!=='string'||full.content_ticket)throw new Error('Invalid formatted response.');
      renderResponse(target,{...full,role:value.role});
    }).catch(()=>{
      if(target.responseGeneration!==generation||!target.isConnected)return;
      status.textContent=responseText('Could not load the complete response. Load recent conversation to try again.','No se pudo cargar la respuesta completa. Carga la conversación reciente para intentarlo de nuevo.');
    });
    return;
  }
  if(window.TlamatiniResponse)window.TlamatiniResponse.render(target,value,{language:responseLanguage(),download:downloadResponseFile});
  else target.textContent=value.text||'';
}
function renderReceipt(target,receipt){
  const value=receipt.result||{};
  if(receipt.kind==='conversation.submit'&&!value.cancelled){renderResponse(target,value);return;}
  if(receipt.kind==='conversation.history'&&Array.isArray(value.messages)&&value.messages.length){
    target.replaceChildren();
    for(const row of value.messages){
      const card=document.createElement('section');card.className='history-message '+(row.role==='user'?'user':'assistant');
      const author=document.createElement('small');author.textContent=row.role==='assistant'?'Tlamatini':responseText('You','Tú');
      if(row.created_at){const date=new Date(row.created_at);if(!isNaN(date))author.textContent+=' · '+date.toLocaleString(responseLanguage());}
      const content=document.createElement('div');card.append(author,content);renderResponse(content,row);target.append(card);
    }
    if(value.truncated){const note=document.createElement('p');note.className='muted';note.textContent=responseText('Earlier content is available in Tlamatini.','El contenido anterior está disponible en Tlamatini.');target.append(note);}
    return;
  }
  target.textContent=t(resultText(receipt));
}

function route(){let route=location.hash.slice(1)||(session?'chat':'security');if(route==='computers'&&!transport.hosted)route='security';const valid=[...document.querySelectorAll('[data-panel]')].some(p=>p.dataset.panel===route);for(const panel of document.querySelectorAll('[data-panel]'))panel.hidden=panel.dataset.panel!==(valid?route:'chat');for(const link of document.querySelectorAll('nav a')){if(link.hash==='#'+(valid?route:'chat'))link.setAttribute('aria-current','page');else link.removeAttribute('aria-current');}}
addEventListener('hashchange',route);route();
function pendingLifecycle(){
  return lifecycleSubmitting||[...receipts.values()].find(item=>item.bindingId===identity?.binding.id
    && ['application.start','application.stop'].includes(item.kind)
    && !item.finished&&['accepted','running'].includes(item.state))?.kind||null;
}
function startAllowed(){return has('launch')&&!pendingLifecycle()&&!['ready','loading'].includes(applicationState);}
function stopAllowed(){return has('stop')&&!pendingLifecycle();}
function lifecycleControls(){
  const connected=!!session;
  const pending=pendingLifecycle();
  $('#start-app').disabled=!startAllowed();
  $('#stop-app').disabled=!stopAllowed();
  $('#start-app').textContent=pending==='application.start'?'Starting Tlamatini…':'Start Tlamatini';
  $('#stop-app').textContent=pending==='application.stop'?'Stopping Tlamatini…':'Stop Tlamatini…';
  const missing=[];
  if(!has('launch'))missing.push('Start Tlamatini');
  if(!has('stop'))missing.push('Stop Tlamatini');
  $('#lifecycle-help').textContent=!connected?'Connect this browser to use its approved controls.':pending
    ? (pending==='application.start'?'Starting Tlamatini.':'Stopping Tlamatini.')+' Waiting for confirmation; please do not repeat the request.'
    :missing.length
    ? 'This browser is not allowed to '+(missing.length===2?'start or stop Tlamatini':missing[0].toLowerCase())+'. In XT → Remote-Controller → Connections, select this browser and choose Change permissions. Reconnect after saving; no new invitation is needed.'
    : applicationState==='ready'?'Tlamatini is already running. Stop closes this edition for all its accounts. Remote-Controller and Torrenter remain independent.'
    : applicationState==='loading'?'Tlamatini is running and preparing this account. Start is unavailable while it is running.'
    : 'Start opens Tlamatini on this PC. Stop closes this edition for all its accounts. Remote-Controller and Torrenter remain independent.';
}
function controls(){modeControls();lifecycleControls();const connected=!!session;$('#connection').textContent=connected?'Connected':'Disconnected';$('#connection').classList.toggle('connected',connected);$('#send').disabled=!has('chat');$('#history').disabled=!has('chat');$('#open-workspace').disabled=!has('workspace')||!(executionCapabilities?.workspace_available||executionCapabilities?.web_port);$('#schedule-save').disabled=!has('schedules')||!has('chat');$('#disconnect').disabled=!connected;$('#connect').disabled=!credential?.deviceId||connected||connecting||resetting;$('#enroll').disabled=enrolling||connected||resetting;$('#enroll-form').hidden=!!credential?.deviceId;$('#chat-connection').hidden=connected;$('#chat-connect-link').textContent=credential?.deviceId?'Connect to Tlamatini':'Set up this browser';$('#reset-device').hidden=!credential?.deviceId&&!credential?.pending;$('#reset-device').disabled=connected||connecting||enrolling||resetting;$('#reset-help').hidden=$('#reset-device').hidden;$('#account-line').textContent=identity?`${identity.binding.display_name} · ${identity.binding.edition==='spanish'?'Español':'English'}`:t('Connect an approved device to begin.');$('#identity-title').textContent=approvalProblem?'This browser needs a new approval':credential?.deviceId?'This browser is enrolled':'This device is not enrolled';$('#identity-detail').textContent=approvalProblem?t(connectionError({code:approvalProblem})):identity?(language()==='es'?'Permisos: ':'Permissions: ')+identity.scopes.join(', ')+(language()==='es'?'. La sesión caduca a las ':'. Session expires ')+new Date(session.expires_at).toLocaleTimeString()+'.':credential?.deviceId?t('Reconnect with the approved device key. No new invitation is needed.'):t('Create an invitation in XT → Remote-Controller → Access, then compare this device’s fingerprint locally.');if(transport.hosted){$('#open-workspace').hidden=true;$('#connect').disabled=$('#connect').disabled||!transport.selected;$('#enroll-form').hidden=$('#enroll-form').hidden||!transport.selected;$('#chat-connect-link').href=transport.selected?'#security':'#computers';if(!transport.selected)$('#chat-connect-link').textContent=t('Sign in and choose a computer first.');$('#security-profile').textContent=t('Development lab · not public remote access');}applyLanguage();}
function clearSession(reason){for(const controller of activeDownloads)controller.abort();session=null;identity=null;applicationState="unknown";lifecycleSubmitting=null;executionCapabilities=null;modesInitialized=false;selfModifyTouched=false;clearTimeout(expiryTimer);if(socket){socket.onclose=null;socket.close();socket=null;}controls();$('#service-health').textContent=t('Connection unavailable');$('#observed-at').textContent=t('Previous observations are stale');$('#application-state').textContent=t('Not currently observed');if(reason){notice(reason);entry(reason);}}
async function request(path,{method='GET',body,headers={},authenticated=true,credentials='omit'}={}){
  // The 10-second reply clock starts when the request is actually sent; one still
  // waiting for a free connection slot has not left the phone yet.
  const epoch=selectionEpoch,controller=new AbortController();let timeout=null;const sent=()=>{if(timeout===null)timeout=setTimeout(()=>controller.abort(),10000);};
  try{
    const response=await transport.request(path,{method,body:body===undefined?undefined:JSON.stringify(body),headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(authenticated&&session?{Authorization:'Bearer '+session.access_token}:{}),...headers},credentials,cache:'no-store',signal:controller.signal,onSent:sent});
    let result=null;if(response.status!==204){try{result=await response.json();}catch{throw new Error('The server returned an invalid response.');}}
    if(epoch!==selectionEpoch)throw Object.assign(new Error('The selected computer changed.'),{code:'selection_changed'});
    if(!response.ok){if(response.status===401&&authenticated)clearSession('Session ended. Reconnect to continue.');const error=new Error(result?.error?.message||'Request failed ('+response.status+').');error.code=result?.error?.code;throw error;}
    return result;
  }catch(error){
    if(error.name==='AbortError')throw new Error(t('The request timed out. Its outcome may be unknown.'));
    if(error instanceof TypeError&&/fetch|network|load failed/i.test(error.message)){const detail=connectionError(error);if(epoch===selectionEpoch&&authenticated&&session)clearSession(detail);throw new Error(t(detail));}
    throw error;
  }finally{clearTimeout(timeout);}
}
async function database(action,value,context=identityContext()){
  // The identity belongs to the selection at invocation, never whichever
  // account happens to be selected when IndexedDB later opens or commits.
  requireIdentityContext(context);
  return new Promise((resolve,reject)=>{
    const open=indexedDB.open('tlachihuani-rc-device-v1',1);
    open.onupgradeneeded=()=>open.result.createObjectStore('identity');
    open.onerror=()=>reject(new Error('Device storage is unavailable. Enrollment was not saved.'));
    open.onsuccess=()=>{
      const db=open.result;let transaction;
      try{
        requireIdentityContext(context);
        transaction=db.transaction('identity',action==='get'?'readonly':'readwrite');identityTransactions.add(transaction);
        const object=transaction.objectStore('identity'),storageKey=context.storageKey;
        const operation=action==='get'?object.get(storageKey):action==='delete'?object.delete(storageKey):object.put(value,storageKey);
        let result;
        operation.onsuccess=()=>{result=operation.result;if(context.epoch!==selectionEpoch){try{transaction.abort();}catch{}}};
        const finish=()=>{identityTransactions.delete(transaction);db.close();};
        transaction.oncomplete=()=>{finish();try{requireIdentityContext(context);resolve(result);}catch(error){reject(error);}};
        transaction.onerror=transaction.onabort=()=>{finish();reject(context.epoch!==selectionEpoch?selectionChanged():new Error('Device storage could not be updated.'));};
      }catch(error){if(transaction)identityTransactions.delete(transaction);db.close();reject(error);}
    };
  });
}
async function connect(){
  if(connecting||session||resetting||!credential?.deviceId)return;
  const epoch=selectionEpoch,device=credential;connecting=true;controls();
  try{
    if(transport.hosted)await transport.open(device.publicKey);
    if(epoch!==selectionEpoch)return;
    const info=transport.validateInfo(await request('/info',{authenticated:false}));
    if(epoch!==selectionEpoch)return;
    accountCapabilitiesSupported=Array.isArray(info.features)&&info.features.includes('account-capabilities');
    const challenge=await request('/sessions/challenges',{method:'POST',body:{device_id:device.deviceId},authenticated:false});
    if(epoch!==selectionEpoch)return;
    const challengeBytes=un64(challenge.challenge),challengeText=new TextDecoder('utf-8',{fatal:true}).decode(challengeBytes).split('\n');
    if(challengeText.length!==7||challengeText[0]!=='Tlachihuani-RC login v1'||challengeText[2]!==transport.challengeOrigin(info)||(transport.hosted&&challengeText[1]!==transport.selected?.installation_id)||challengeText[3]!==device.deviceId||challengeText[4]!==challenge.id)throw new Error('The authentication challenge has the wrong purpose or origin.');
    const signature=await crypto.subtle.sign({name:'ECDSA',hash:'SHA-256'},device.key,challengeBytes);
    if(epoch!==selectionEpoch)return;
    const connected=await request('/sessions',{method:'POST',body:{device_id:device.deviceId,challenge_id:challenge.id,signature:b64(signature)},authenticated:false});
    if(epoch!==selectionEpoch)return;session=connected;
    const observed=await request('/session');if(epoch!==selectionEpoch)return;
    if(transport.hosted&&observed.binding?.id!==transport.selected?.binding_id)throw new Error('The selected computer identity did not match.');
    identity=observed;approvalProblem=null;cursor=identity.cursor;
    expiryTimer=setTimeout(()=>clearSession('Session expired. Reconnect to continue.'),Math.max(0,new Date(session.expires_at)-Date.now()));
    notice('');entry('Authenticated with the approved device key.');await refresh();if(has('monitor'))await channel();
  }catch(error){if(epoch===selectionEpoch){if(['device_unavailable','account_unavailable'].includes(error.code))approvalProblem=error.code;clearSession(connectionError(error));}}
  finally{if(epoch===selectionEpoch){connecting=false;controls();}}
}
async function channel(){if(transport.hosted)return;const ticket=await request('/connection-ticket',{method:'POST'});if(!session)return;socket=new WebSocket(location.origin.replace(/^http/,'ws')+API+'/channel','tlachihuani.rc.v1');socket.onopen=()=>socket.send(JSON.stringify({ticket:ticket.ticket}));socket.onmessage=event=>{try{const data=JSON.parse(event.data);for(const item of data.events||[])if(item.cursor>cursor)entry(`${item.kind} · ${item.outcome||'recorded'}`);cursor=data.cursor;void refreshReceipts();}catch{clearSession('The event channel returned an invalid message.');}};socket.onerror=()=>{};socket.onclose=()=>{socket=null;if(session)clearSession('Connection interrupted. Reconnect to verify the server and pending receipts.');};}
async function refresh(){if(!session||refreshing)return;const epoch=selectionEpoch;refreshing=true;try{if(has('monitor')){const snapshot=await request('/snapshot');if(epoch!==selectionEpoch)return;$('#service-health').textContent=snapshot.service.health;$('#observed-at').textContent='Observed '+new Date(snapshot.service.observed_at).toLocaleTimeString();applicationState=snapshot.application.state;$('#application-state').textContent=applicationState.replaceAll('_',' ');observeCapabilities(snapshot.application.capabilities);}else if((has('chat')||has('workspace'))&&accountCapabilitiesSupported)observeCapabilities(await request('/capabilities'));if(transport.hosted&&has('monitor')){const events=await request('/events?after='+cursor);for(const item of events.events||[])if(item.cursor>cursor)entry(item.kind+' · '+(item.outcome||'recorded'));cursor=events.cursor;}if(has('schedules'))await schedules();await refreshReceipts();}finally{if(epoch===selectionEpoch)refreshing=false;}}
async function refreshReceipts(){const epoch=selectionEpoch;for(const [operationId,item] of receipts){if(!session||item.finished)continue;const fetchSequence=item.fetchSequence=(item.fetchSequence||0)+1;try{const receipt=await request('/operations/'+operationId);if(epoch!==selectionEpoch)return;if(item.finished||fetchSequence<(item.appliedFetchSequence||0))continue;item.appliedFetchSequence=fetchSequence;if(receipt.state!==item.renderedState){item.state=receipt.state;item.renderedState=receipt.state;item.node.querySelector('small').textContent=operationName(receipt.kind)+' · '+t(receipt.state.replaceAll('_',' '));if(receipt.state==='succeeded'){renderReceipt(item.node.querySelector('.response-body'),receipt);item.node.classList.remove('error');if(receipt.result?.options){const modes=document.createElement('small');modes.className='execution-modes';modes.dataset.options=JSON.stringify(receipt.result.options);modes.textContent=executionSummary(receipt.result.options);item.node.append(modes);}item.finished=true;}else if(['failed','cancelled','outcome_unknown'].includes(receipt.state)){item.node.querySelector('.response-body').textContent=receiptError(receipt);item.node.classList.add('error');item.finished=receipt.state!=='outcome_unknown';}}renderOperationControls(receipt,item);}catch(error){if(epoch!==selectionEpoch)return;notice(error.message);}}if(epoch===selectionEpoch)controls();}
function renderOperationControls(receipt,item){
  if(item.finished||receipt.state==='outcome_unknown'){item.node.querySelector('.operation-actions')?.remove();item.node.querySelector('.permission')?.remove();return;}
  const progress=receipt.progress;
  if(progress?.type==='message'&&(progress.text||progress.html)){const stamp=JSON.stringify(progress);if(item.progressStamp!==stamp){renderResponse(item.node.querySelector('.response-body'),progress);item.progressStamp=stamp;}}
  if(receipt.kind!=='conversation.submit')return;
  if(!item.node.querySelector('.operation-actions')){
    const actions=document.createElement('div');actions.className='actions operation-actions';
    const cancel=document.createElement('button');cancel.className='secondary';cancel.textContent=t('Cancel request');
    cancel.addEventListener('click',async()=>{if(!has('chat'))return;cancel.disabled=true;try{
      const current=await request('/operations/'+receipt.id);
      if(current.state==='accepted')await request('/operations/'+receipt.id,{method:'DELETE'});
      else if(current.state==='running')await submit('conversation.cancel',{operation_id:receipt.id});
      await refreshReceipts();
    }catch(error){notice(error.message);cancel.disabled=false;}});
    actions.append(cancel);item.node.append(actions);
  }
  const permission=receipt.pending_permission;
  if(!permission){item.permissionStamp=null;item.node.querySelector('.permission')?.remove();return;}
  const stamp=JSON.stringify(permission);
  if(item.permissionStamp===stamp)return;
  item.permissionStamp=stamp;item.node.querySelector('.permission')?.remove();
  const card=document.createElement('section');card.className='permission';card.dataset.requestId=permission.request_id;
  const title=document.createElement('strong');title.textContent=t(permission.decision==='deny'?'Execution denied':'Allow this execution?');
  const details=document.createElement('dl');for(const [key,label] of [['tool_name','Tool'],['agent_display','Agent'],['kind','Action'],['program','Program'],['shell','Shell'],['parameters','Parameters']]){if(!permission[key])continue;const term=document.createElement('dt'),value=document.createElement('dd');term.textContent=t(label);value.textContent=permission[key];details.append(term,value);}
  card.append(title,details);
  if(!permission.decision){const actions=document.createElement('div');actions.className='actions';
    for(const decision of ['deny','proceed']){const button=document.createElement('button');button.textContent=t(decision==='deny'?'Deny':'Proceed');button.className='secondary';
      button.addEventListener('click',async()=>{if(!has('chat'))return;for(const b of actions.querySelectorAll('button'))b.disabled=true;
        try{await submit('conversation.permission',{operation_id:receipt.id,request_id:permission.request_id,decision});if(card.isConnected)title.textContent=t('Decision sent; waiting for Tlamatini.');}
        catch(error){if(card.isConnected){notice(error.message);for(const b of actions.querySelectorAll('button'))b.disabled=false;}}});
      actions.append(button);}card.append(actions);}
  item.node.append(card);
}
async function submit(operation,payload,key=id()){const receipt=await request('/operations',{method:'POST',headers:{'Idempotency-Key':key},body:{operation,payload,deadline_seconds:120}});const node=message('Waiting for Tlamatini.','',operationName(operation)+' · '+receipt.state);receipts.set(receipt.id,{node,kind:receipt.kind,bindingId:identity?.binding.id,state:receipt.state,finished:false});entry(operation+' accepted; awaiting the application result.');await refreshReceipts();return receipt;}
$('#chat-form').addEventListener('submit',async event=>{event.preventDefault();if(!has('chat'))return;const epoch=selectionEpoch,text=$('#prompt').value.trim();if(!text)return;$('#send').disabled=true;try{if(!pendingSubmission||pendingSubmission.text!==text){pendingSubmission={key:id(),text,options:selectedModes()};message(text,'user','You');}await submit('conversation.submit',{text,options:pendingSubmission.options},pendingSubmission.key);if(epoch!==selectionEpoch)return;pendingSubmission=null;selfModifyTouched=false;$('#prompt').value='';notice('');}catch(error){if(epoch===selectionEpoch)notice(error.message+' Retry keeps the same request key for this message.');}finally{if(epoch===selectionEpoch)controls();}});
async function requestLifecycle(kind){
  if(kind==='application.start'?!startAllowed():!stopAllowed())return;
  if(kind==='application.stop'&&!confirm(t('Stop this Tlamatini edition? Active conversations and every account in this edition will disconnect. Remote-Controller will stay available.')))return;
  const epoch=selectionEpoch;lifecycleSubmitting=kind;controls();
  try{await submit(kind,{});}catch(error){if(epoch===selectionEpoch)notice(error.message);}finally{if(epoch===selectionEpoch){lifecycleSubmitting=null;controls();}}
}
$('#start-app').addEventListener('click',()=>requestLifecycle('application.start'));
$('#stop-app').addEventListener('click',()=>requestLifecycle('application.stop'));
$('#history').addEventListener('click',async()=>{if(!has('chat'))return;const epoch=selectionEpoch;$('#history').disabled=true;try{await submit('conversation.history',{});}catch(error){if(epoch===selectionEpoch)notice(error.message);}finally{if(epoch===selectionEpoch)controls();}});
function refreshWithNotice(){const epoch=selectionEpoch;return refresh().catch(error=>{if(epoch===selectionEpoch)notice(error.message);});}
$('#refresh').addEventListener('click',refreshWithNotice);
$('#connect').addEventListener('click',connect);
$('#reset-device').addEventListener('click',async()=>{
  if(session||connecting||enrolling||resetting||(!credential?.deviceId&&!credential?.pending))return;
  if(!confirm(t('Forget this browser’s saved approval on this PC? You will need a new invitation and local approval to reconnect. Other devices are not changed.')))return;
  const context=identityContext();resetting=true;controls();
  try{
    await database('delete',undefined,context);requireIdentityContext(context);credential=null;approvalProblem=null;pendingSubmission=null;receipts.clear();
    $('#enroll-form').reset();$('#fingerprint').textContent='';$('#prompt').value='';$('#messages').replaceChildren();
    const empty=document.createElement('p');empty.className='empty';empty.textContent='Your conversation will appear here. Nothing is sent until you choose Send.';$('#messages').append(empty);
    clearSession('This browser is ready for a new invitation. In XT → Remote-Controller → Access, select your account and create an invitation.');
    $('#device-name').focus();
  }catch(error){if(context.epoch===selectionEpoch)notice(error.message);}finally{if(context.epoch===selectionEpoch){resetting=false;controls();}}
});
$('#disconnect').addEventListener('click',async()=>{const epoch=selectionEpoch;try{await request('/session',{method:'DELETE'});}catch{}finally{if(epoch===selectionEpoch)clearSession('Disconnected. The server and applications keep running.');}});
$('#enroll-form').addEventListener('submit',async event=>{
  event.preventDefault();if(enrolling||resetting)return;
  const context=identityContext();enrolling=true;controls();
  try{
    const key=transport.hosted?await ensureHostedKey():await crypto.subtle.generateKey({name:'ECDSA',namedCurve:'P-256'},false,['sign','verify']);
    requireIdentityContext(context);
    const spki=transport.hosted?un64(credential.publicKey):await crypto.subtle.exportKey('spki',key.publicKey);
    requireIdentityContext(context);
    if(transport.hosted)await transport.open(credential.publicKey);
    requireIdentityContext(context);
    const pending=await request('/enrollments',{method:'POST',authenticated:false,body:{invitation:$('#invitation').value,name:$('#device-name').value,public_key:b64(spki)}});
    requireIdentityContext(context);
    const enrolled={key:key.privateKey,publicKey:b64(spki),pending,deviceId:null};
    await database('put',enrolled,context);requireIdentityContext(context);
    credential=enrolled;approvalProblem=null;$('#invitation').value='';
    $('#fingerprint').textContent=t('Compare in XT: ')+pending.fingerprint;
    notice('Waiting for local approval in XT. Compare the complete fingerprint before approving.');await pollEnrollment();
  }catch(error){if(context.epoch===selectionEpoch)notice(connectionError(error));}
  finally{if(context.epoch===selectionEpoch){enrolling=false;controls();}}
});
async function pollEnrollment(){
  const context=identityContext();
  while(credential?.pending&&!credential.deviceId){
    requireIdentityContext(context);const device=credential,pending=device.pending;
    if(Date.now()>=new Date(pending.expires_at).getTime())throw new Error('Enrollment expired. Request a new invitation in XT.');
    const result=await request('/enrollments/poll',{method:'POST',authenticated:false,body:{id:pending.id,poll_secret:pending.poll_secret}});
    requireIdentityContext(context);
    if(result.status==='approved'){
      const approved={key:device.key,publicKey:device.publicKey,deviceId:result.device_id};
      await database('put',approved,context);requireIdentityContext(context);credential=approved;
      $('#fingerprint').textContent=t('Device approved. Its private signing key remains in this browser.');controls();await connect();
      if(context.epoch===selectionEpoch&&transport.hosted&&session)location.hash='chat';return;
    }
    if(result.status!=='pending')throw new Error('The local operator declined this request.');
    await new Promise(resolve=>setTimeout(resolve,1800));
  }
}
function scheduleBody(item){return {name:item.name,operation:item.operation,payload:item.payload,time_zone:item.time_zone,local_time:item.local_time,days:item.days,missed_run:item.missed_run,enabled:item.enabled,grace_minutes:item.grace_minutes};}
async function schedules(){const result=await request('/schedules');const target=$('#schedule-list');target.replaceChildren();for(const item of result.schedules){const card=document.createElement('article');const heading=document.createElement('h2');heading.textContent=item.name;const detail=document.createElement('p');detail.textContent=`${t(item.enabled?'Enabled':'Paused')} · ${item.local_time} · ${item.time_zone} · ${t('Next: ')}${new Date(item.next_run).toLocaleString()}`;const actions=document.createElement('div');actions.className='actions';for(const action of ['toggle','remove']){const button=document.createElement('button');button.className='secondary';button.textContent=t(action==='remove'?'Delete schedule':item.enabled?'Pause':'Enable');button.addEventListener('click',async()=>{button.disabled=true;try{if(action==='remove'){if(!confirm(t('Delete this schedule? Existing operation receipts will be retained.')))return;await request('/schedules/'+item.id,{method:'DELETE',headers:{'If-Match':String(item.revision)}});}else await request('/schedules/'+item.id,{method:'PUT',headers:{'If-Match':String(item.revision)},body:{...scheduleBody(item),enabled:!item.enabled}});await schedules();}catch(error){notice(error.message);}finally{button.disabled=false;}});actions.append(button);}card.append(heading,detail,actions);target.append(card);}if(!result.schedules.length){const empty=document.createElement('p');empty.className='muted';empty.textContent=t('No schedules for this account.');target.append(empty);}}
$('#schedule-form').addEventListener('submit',async event=>{event.preventDefault();if(!has('schedules')||!has('chat'))return;$('#schedule-save').disabled=true;try{await request('/schedules',{method:'POST',body:{name:$('#schedule-name').value,operation:'conversation.submit',payload:{text:$('#schedule-text').value},time_zone:$('#schedule-zone').value,local_time:$('#schedule-time').value,days:[0,1,2,3,4,5,6],missed_run:$('#schedule-missed').value,enabled:true,grace_minutes:15}});$('#schedule-form').reset();$('#schedule-zone').value=Intl.DateTimeFormat().resolvedOptions().timeZone;await schedules();notice('Schedule saved by Remote-Controller.');}catch(error){notice(error.message);}finally{controls();}});
$('#schedule-zone').value=Intl.DateTimeFormat().resolvedOptions().timeZone;
for(const [selector,name] of [['#opaque','opaque'],['#motion','reduce-motion']]){try{$(selector).checked=localStorage.getItem('rc-'+name)==='true';}catch{}document.body.classList.toggle(name,$(selector).checked);$(selector).addEventListener('change',()=>{document.body.classList.toggle(name,$(selector).checked);try{localStorage.setItem('rc-'+name,String($(selector).checked));}catch{notice('Appearance updated for this visit only.');}});}
// navigator.onLine reports Internet/network connectivity, not loopback health.
// Only an actual RC request or channel failure can invalidate the local link.
addEventListener('offline',controls);
addEventListener('online',controls);
setInterval(()=>{if(session)void refreshWithNotice();},5000);
async function ensureHostedKey(){
  if(!transport.hosted||!transport.selected)throw new Error('Sign in and choose a computer first.');
  const context=identityContext();
  if(credential?.key&&credential?.publicKey)return {privateKey:credential.key};
  if(hostedKeyGeneration?.context.epoch===context.epoch&&hostedKeyGeneration.context.storageKey===context.storageKey)return hostedKeyGeneration.promise;
  const pending={context};
  pending.promise=(async()=>{
    const pair=await crypto.subtle.generateKey({name:'ECDSA',namedCurve:'P-256'},false,['sign','verify']);
    requireIdentityContext(context);
    const generated={key:pair.privateKey,publicKey:b64(await crypto.subtle.exportKey('spki',pair.publicKey)),deviceId:null};
    requireIdentityContext(context);
    await database('put',generated,context);requireIdentityContext(context);credential=generated;
    return {privateKey:generated.key};
  })();
  hostedKeyGeneration=pending;
  try{return await pending.promise;}finally{if(hostedKeyGeneration===pending)hostedKeyGeneration=null;}
}
function clearAccountView(){
  pendingSubmission=null;approvalProblem=null;accountCapabilitiesSupported=false;receipts.clear();$('#messages').replaceChildren();$('#schedule-list').replaceChildren();$('#activity-list').replaceChildren();$('#prompt').value='';$('#fingerprint').textContent='';
  $('#enroll-form').reset();$('#schedule-form').reset();$('#schedule-zone').value=Intl.DateTimeFormat().resolvedOptions().timeZone;
  const empty=document.createElement('p');empty.className='empty';empty.textContent=t('Your conversation will appear here. Nothing is sent until you choose Send.');$('#messages').append(empty);
}
const hostedUI=createHostedUI({
  disconnect(){advanceSelection();connecting=false;enrolling=false;refreshing=false;resetting=false;transport.clearSelection();credential=null;clearSession();clearAccountView();},
  async select(computer,account){
    try{await openSelected(computer,account);}catch(error){throw Object.assign(new Error(connectionError(error)),{code:error.code});}
  }
});
async function openSelected(computer,account){
    const epoch=advanceSelection();connecting=false;enrolling=false;refreshing=false;resetting=false;clearSession();clearAccountView();credential=null;transport.select(computer,account);
    const saved=await database('get');if(epoch!==selectionEpoch)return;credential=saved;
    await ensureHostedKey();if(epoch!==selectionEpoch)return;
    await transport.open(credential.publicKey);if(epoch!==selectionEpoch)return;
    transport.validateInfo(await request('/info',{authenticated:false}));controls();location.hash='security';
    if(credential?.deviceId){await connect();if(session&&epoch===selectionEpoch)location.hash='chat';}
    else if(credential?.pending){enrolling=true;controls();try{await pollEnrollment();}finally{if(epoch===selectionEpoch){enrolling=false;controls();}}}
}
transport.onLost(error=>{if(transport.hosted)clearSession(error.message);});
$('#language').addEventListener('change',event=>setLanguage(event.target.value));
document.addEventListener('rc-language-change',()=>{controls();hostedUI.render();for(const element of document.querySelectorAll('.execution-modes[data-options]')){try{element.textContent=executionSummary(JSON.parse(element.dataset.options));}catch{}}});
async function initialize(){
  controls();
  try{
    await transport.initialize();
    if(transport.hosted){await hostedUI.activate();route();return;}
    credential=await database('get');controls();
    const info=await request('/info',{authenticated:false});
    $('#security-profile').textContent=t(info.security_profile==='local-only'?'Local-only connection · Remote post-quantum transport is unavailable.':'Unsupported security profile');
    if(credential?.pending&&!credential.deviceId){enrolling=true;controls();$('#fingerprint').textContent=t('Compare in XT: ')+credential.pending.fingerprint;await pollEnrollment();}
  }catch(error){notice(connectionError(error));}
  finally{
    enrolling=false;controls();
    if('serviceWorker' in navigator)navigator.serviceWorker.register('/remote-sw.js').catch(()=>{if(!$('#notice').textContent)notice('Offline shell unavailable. Live connections are unaffected.');});
  }
}
void initialize();

$('#open-workspace').addEventListener('click',async()=>{if(transport.hosted)return;try{const result=await request('/workspace',{method:'POST',credentials:'same-origin'});if(!/^\/workspace\/[a-f0-9]{32}\/agent\/agent\/$/.test(result.url))throw new Error('Invalid workspace destination.');location.assign(result.url);}catch(error){notice(error.message);}});
