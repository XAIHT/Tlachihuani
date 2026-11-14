// Installed, headed mobile acceptance. These are interface checks, not model prompts.
// Opens existing dialogs and cancels them; never changes a database or starts an update.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import {root,attachBrowser,sleep} from './rc_installed_driver.mjs';
const edition=process.argv[2];assert.ok(['english','spanish'].includes(edition));
const output=path.join(process.env.TLAMATINI_ACCEPTANCE_ROOT||path.join(root,'artifacts/rc-local-diagnosis'),edition+'-installed');
const metadata=JSON.parse(await fs.readFile(path.join(output,'browser.json'),'utf8'));
const page=await attachBrowser(metadata),results=[];
await page.setTouch(true);
const origin=process.env.TLAMATINI_RC_ORIGIN||'http://127.0.0.1:12481';
const evidenceKind=process.env.TLAMATINI_MOBILE_CHECK_KIND||'installed headed mobile interface';
const visible=selector=>`(()=>{const e=document.querySelector(${JSON.stringify(selector)});return !!e&&e.getClientRects().length>0&&getComputedStyle(e).visibility!=='hidden'})()`;
const report=()=>fs.writeFile(path.join(output,'mobile-results.json'),JSON.stringify({edition,kind:evidenceKind,passed:results.filter(x=>x.passed).length,failed:results.filter(x=>!x.passed).length,results,uncaught:page.errors},null,2));
async function check(name,fn){try{await page.focus();await fn();results.push({name,passed:true});console.log('PASS MOBILE '+edition+' '+name);}catch(error){results.push({name,passed:false,error:error.stack});try{await page.photo('mobile-failure-'+results.length);}catch{}throw error;}finally{await report();}}
async function resize(width,height=844){await page.send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:true});await sleep(250);}
async function tab(name){await page.click('.mobile-tabbar [data-view="'+name+'"]');}
async function noOverflow(){const size=await page.evaluate('({viewport:innerWidth,document:document.documentElement.scrollWidth,body:document.body.scrollWidth})');assert.ok(size.document<=size.viewport+1&&size.body<=size.viewport+1,JSON.stringify(size));}
async function bounds(selector){const box=await page.evaluate(`(()=>{const r=document.querySelector(${JSON.stringify(selector)}).getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height,vw:innerWidth,vh:innerHeight}})()`);assert.ok(box.width>0&&box.height>0&&box.x>=-1&&box.x+box.width<=box.vw+1&&box.y>=-1&&box.y+box.height<=box.vh+1,JSON.stringify(box));}
async function openAction(id){await tab('more');await page.field('.mobile-actions input[type=search]','');await page.click('.mobile-action[data-source="'+id+'"]');}
async function chooseFile(selector,filename){
 const {root:documentRoot}=await page.send('DOM.getDocument');
 const {nodeId}=await page.send('DOM.querySelector',{nodeId:documentRoot.nodeId,selector});assert.ok(nodeId,'File input exists');
 await page.send('DOM.setFileInputFiles',{nodeId,files:[filename]});
}
async function closeDialog(selector){
  if(selector==='#external-mcps-dialog-message'){await page.click('#external-mcps-close');await page.until('!'+visible(selector),'MCP catalog closed');return;}
  await page.evaluate('document.querySelectorAll("[data-acceptance-close]").forEach(e=>e.removeAttribute("data-acceptance-close"))');
  const close=await page.evaluate(`(()=>{const e=document.querySelector(${JSON.stringify(selector)}),dialog=e.closest('.ui-dialog')||e;const b=[...dialog.querySelectorAll('button')].find(b=>!b.disabled&&b.getClientRects().length&&(b.classList.contains('ui-dialog-titlebar-close')||/^(Close|Cerrar|Cancel|Cancelar)$/i.test(b.getAttribute('aria-label')||b.textContent.trim())));if(!b)return null;b.dataset.acceptanceClose='true';return '[data-acceptance-close=true]'})()`);
  assert.ok(close,'Dialog has a usable close control');await page.click(close);await page.until('!'+visible(selector),'dialog closed');
}
let prefix;
try{
 await resize(390);
 await check('approved RC connection opens the full workspace',async()=>{
  if(!await page.evaluate('!!document.querySelector("#connection")'))await page.navigate(origin+'/remote.html#security');
  if(await page.evaluate('document.querySelector("#connection").textContent!=="Connected"')){await page.evaluate("location.hash='security'");await page.click('#connect');await page.until('document.querySelector("#connection").textContent==="Connected"','approved connection');}
  await page.evaluate("location.hash='chat'");await page.until('!document.querySelector("#open-workspace").disabled','live full workspace capability',60000);await page.click('#open-workspace');
  await page.until('location.pathname.startsWith("/workspace/")&&document.readyState==="complete"','gateway login');
  prefix=await page.evaluate('window.TlamatiniMobile.prefix');assert.match(prefix,/^\/workspace\/[a-f0-9]{32}$/);
 });
 await check('normal account login works through the mobile gateway',async()=>{
  if(await page.evaluate('!!document.querySelector("#id_username")')){
   await page.field('#id_username',process.env.TLAMATINI_USER||'user');await page.field('#id_password',process.env.TLAMATINI_PASSWORD||'changeme');await page.click('form button[type=submit]');
  }
  await page.until('!!window.TlamatiniMobileInventory&&!!document.querySelector("#chat-message-input")','real mobile chat',60000);
  assert.equal(await page.evaluate('document.documentElement.lang'),edition==='english'?'en':'es');
 });
 const inventory=await page.evaluate('window.TlamatiniMobileInventory()');await fs.writeFile(path.join(output,'mobile-menu-inventory.json'),JSON.stringify(inventory,null,2));
 if(!process.env.TLAMATINI_MOBILE_FOCUS){
 const required=['open-button','save-as-button','views-canvas-toggle','views-terminals-toggle','views-reset-layout','set-dir-context','set-file-context','view-context-dir-in-canvas','clear-context','set-omissions','remote-controller-panel','browse-skills','configure-skills','skills-diagnostics','reload-skill-registry','external-mcps','enable-mcps','enable-agents','config-models','config-urls','config-contacts','access-keys-wizard','config-interaction','config-voice','config-mic','backup-database','set-database','re-connect-button','about-button','usage-button','check-updates-button','logout-button'];
 await tab('more');
 for(const id of required)await check('menu is available: '+id,async()=>{assert.ok(inventory.included.some(x=>x.id===id));assert.ok(await page.evaluate('!!document.querySelector('+JSON.stringify('.mobile-action[data-source="'+id+'"]')+')'));});
 await check('Torrenter Sources and Workspace are both available',async()=>{const entries=inventory.included.filter(x=>/Torrenter/i.test(x.label));assert.equal(entries.length,2);});
 await check('only the three requested panels are excluded',async()=>{assert.deepEqual(inventory.excluded.sort(),['agentic-control-panel','open-admin','prompt-flow-panel'].sort());for(const id of inventory.excluded)assert.equal(await page.evaluate('!!document.getElementById('+JSON.stringify(id)+')'),false);});
 for(const width of [360,390,768]){
  await resize(width);for(const view of ['chat','canvas','downloads','settings','more'])await check(view+' layout at '+width+' pixels',async()=>{await tab(view);await noOverflow();await bounds('.mobile-header');await bounds('.mobile-tabbar');if(view==='chat')await bounds('#chat-message-input');});
  await page.photo('mobile-menu-'+width);
 }
 await resize(390);await tab('chat');await page.photo('mobile-chat');
 await check('all eight main execution/context switches remain available',async()=>{
  await page.click('.mobile-mode-sheet summary');for(const id of ['multi-turn-enabled','exec-report-enabled','acpx-enabled','ask-execs-enabled','step-by-step-enabled','compact-mode-enabled','self-modify-enabled','internetEnabled'])assert.ok(await page.evaluate('!!document.getElementById('+JSON.stringify(id)+')'),id);
  await page.photo('mobile-execution-modes');await page.click('.mobile-mode-sheet summary');
 });
 await check('status and photo actions have touch controls',async()=>{assert.ok(await page.evaluate(visible('.mobile-status')));assert.ok(await page.evaluate(visible('#mobile-attach-image')));});
 }
 await resize(390);await tab('chat');
 if(process.env.TLAMATINI_MOBILE_FOCUS!=='pages'){
 const fixtureDirectory=path.join(edition==='english'?'C:/Tlamatini/Temp':'C:/Tlamatini-Spanish/Temp','rc-mobile-'+Date.now());
 await fs.mkdir(fixtureDirectory,{recursive:true});
 const imageFile=path.join(fixtureDirectory,'mobile-photo.png'),textFile=path.join(fixtureDirectory,'mobile-document.txt'),savedFile=path.join(fixtureDirectory,'mobile-saved.txt');
 await fs.writeFile(imageFile,Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=','base64'));
 await fs.writeFile(textFile,'Mobile canvas original\n');
 await page.send('Page.setInterceptFileChooserDialog',{enabled:true});
 await check('phone photo selection uploads a real image and previews it',async()=>{
  await page.click('#mobile-attach-image');await chooseFile('#mobile-image-input',imageFile);
  await page.until('!!document.querySelector(".chat-img-chip")','image upload result',30000);
  assert.equal(await page.evaluate('document.querySelector(".chat-img-chip-error")?.textContent||""'),'');
  const uploaded=(await page.evaluate('document.querySelector("#chat-message-input").value')).split(String.fromCharCode(92)).filter(Boolean).join('/');
  assert.ok(uploaded.startsWith((edition==='english'?'C:/Tlamatini':'C:/Tlamatini-Spanish')+'/Temp/image_'));assert.ok((await fs.stat(uploaded)).size>0);
  await page.click('.chat-img-chip-thumb');await page.until(visible('#chat-img-preview-overlay'),'single tap photo preview');
  assert.ok(await page.evaluate('document.querySelector(".chat-img-preview-full").naturalWidth>0'));await bounds('.chat-img-preview-window');await page.photo('mobile-photo-preview');
  await page.click('#chat-img-preview-overlay .about-close-btn');await page.click('.chat-img-chip-close');
  assert.equal(await page.evaluate('!!document.querySelector(".chat-img-chip")'),false);
 });
 await check('phone file picker opens a real document in the canvas',async()=>{
  // Exercise the HTML file input used on phones without File System Access.
  await page.evaluate('window.showOpenFilePicker=undefined');await openAction('open-button');
  await page.until('!!document.querySelector("body>input[type=file]")','phone document input');
  await chooseFile('body>input[type=file]',textFile);
  await page.until('window.TlamatiniCanvasEditing?.text==="Mobile canvas original\\n"','canvas document content');
  assert.equal(await page.evaluate('window.TlamatiniMobileInventory().view'),'canvas');await bounds('#canvas-container');
 });
 await check('chat generated-file links reveal the canvas and preserve content when missing',async()=>{
  // A deliberately absent generated-file name exercises the real formatter,
  // touch handler, gateway request and normal application error path. It does
  // not create a database row or stand in for model-prompt acceptance.
  const missing='rc-acceptance-missing-'+Date.now()+'.txt';
  await page.send('Network.enable');page.responses.length=0;
  await tab('chat');
  await page.evaluate(`(()=>{const link=buildSafeLoadCanvasLink(${JSON.stringify('<a onclick=\'loadCanvas("'+missing+'");\'>Open generated test file</a>')});if(!link)throw new Error('The normal safe formatter rejected the test link');link.id='mobile-generated-link-check';document.querySelector('#chat-log').append(link);})()`);
  await page.click('#mobile-generated-link-check');
  await page.until(visible('#tlm-popup-host'),'normal missing generated-file message');
  assert.ok((await page.evaluate('document.querySelector("#tlm-popup-host").textContent')).includes(missing));
  assert.equal(await page.evaluate('window.TlamatiniMobileInventory().view'),'canvas');
  assert.equal(await page.evaluate('window.TlamatiniCanvasEditing.text'),'Mobile canvas original\n');
  assert.equal(await page.evaluate('window.TlamatiniCanvasEditing.dirty'),false);
  const loaded=page.responses.filter(e=>e.path===prefix+'/agent/load_canvas/'+missing+'/');
  assert.equal(loaded.length,1);assert.equal(loaded[0].status,404);
  await page.send('Network.disable');
  await page.click('#tlm-popup-host .tlmpop-x');await bounds('#canvas-container');
  await page.evaluate('document.querySelector("#mobile-generated-link-check").remove()');
 });
 await check('canvas edit and cancelled Save As retain the unsaved text',async()=>{
  await page.field('#text-editor [contenteditable=true]','Mobile canvas edited\n');
  await page.until('window.TlamatiniCanvasEditing.dirty','real editor change');await page.click('#canvas-save-as-button');
  await page.until(visible('.mobile-file-picker'),'phone Save As picker');await page.click('.mobile-file-picker footer button:first-child');
  await page.until('!window.TlamatiniCanvasEditing.busy','cancelled save');assert.equal(await page.evaluate('window.TlamatiniCanvasEditing.dirty'),true);
  assert.equal(await page.evaluate('window.TlamatiniCanvasEditing.text'),'Mobile canvas edited\n');
 });
 await check('canvas Save As writes the chosen host file through mobile controls',async()=>{
  await page.click('#canvas-save-as-button');await page.until(visible('.mobile-file-picker'),'phone Save As picker');
  await page.field('.mobile-file-picker>input:first-of-type',fixtureDirectory);await page.click('.mobile-picker-actions button:last-child');
  await page.until('!document.querySelector(".mobile-file-picker footer button:last-child").disabled','chosen directory listed');
  await page.field('.mobile-file-picker>input:last-of-type',path.basename(savedFile));await page.click('.mobile-file-picker footer button:last-child');
  await page.until('!window.TlamatiniCanvasEditing.busy&&!window.TlamatiniCanvasEditing.dirty','saved canvas',30000);
  assert.equal(await fs.readFile(savedFile,'utf8'),'Mobile canvas edited\n');await page.photo('mobile-canvas-saved');
 });
 await check('canvas refuses to overwrite an externally changed revision',async()=>{
  await fs.writeFile(savedFile,'External revision\n');await page.field('#text-editor [contenteditable=true]','My unsaved revision\n');await page.click('#canvas-save-button');
  await page.until('!window.TlamatiniCanvasEditing.busy&&/^(Not saved|No se guardó)/.test(window.TlamatiniCanvasEditing.notice)','revision conflict');
  assert.equal(await fs.readFile(savedFile,'utf8'),'External revision\n');assert.equal(await page.evaluate('window.TlamatiniCanvasEditing.dirty'),true);
 });
 await check('conflicted canvas edits can be saved as a separate copy',async()=>{
  await page.click('#canvas-save-as-button');await page.until(visible('.mobile-file-picker'),'conflict recovery Save As');
  await page.field('.mobile-file-picker>input:first-of-type',fixtureDirectory);await page.click('.mobile-picker-actions button:last-child');
  await page.until('!document.querySelector(".mobile-file-picker footer button:last-child").disabled','recovery directory listed');
  await page.field('.mobile-file-picker>input:last-of-type','mobile-conflict-copy.txt');await page.click('.mobile-file-picker footer button:last-child');
  await page.until('!window.TlamatiniCanvasEditing.busy&&!window.TlamatiniCanvasEditing.dirty','saved separate revision',30000);
  assert.equal(await fs.readFile(path.join(fixtureDirectory,'mobile-conflict-copy.txt'),'utf8'),'My unsaved revision\n');assert.equal(await fs.readFile(savedFile,'utf8'),'External revision\n');
 });
 await check('terminal connects and can be shown and hidden on a phone',async()=>{
  await openAction('views-terminals-toggle');await page.until('window.TlamatiniTerminals?.isReady()&&window.TlamatiniTerminals.isVisible()','real terminal WebSocket',30000);
  await page.until('window.TlamatiniTerminals.state().terminals.some(t=>t.rows>=8&&t.cols>=20)','usable terminal rows and columns',30000);
  await page.until('window.TlamatiniTerminals.textOf().trim().length>0','real terminal output arrives without entering a command',30000);
  await page.evaluate('document.querySelector("#tlm-terminals").scrollIntoView({block:"center"})');await bounds('#tlm-terminals');await page.photo('mobile-terminals');
  await openAction('views-terminals-toggle');assert.equal(await page.evaluate('window.TlamatiniTerminals.isVisible()'),false);
 });
 const dialogs=[['set-omissions','#omissions-dialog-message'],['enable-mcps','#mcps-dialog-message'],['enable-agents','#agents-dialog-message'],['browse-skills','#skills-browse-dialog-message'],['configure-skills','#skills-configure-dialog-message'],['skills-diagnostics','#skills-diagnostics-dialog-message'],['external-mcps','#external-mcps-dialog-message'],['config-models','#config-models-dialog-message'],['config-urls','#config-urls-dialog-message'],['config-contacts','#contacts-dialog-message'],['access-keys-wizard','#access-keys-wizard-dialog-message'],['backup-database','#backup-db-dialog-message'],['set-database','#set-db-dialog-message']];
 for(const [id,selector]of dialogs)await check('open and cancel '+id,async()=>{await openAction(id);await page.until(visible(selector),'normal application dialog',30000);await bounds(selector);await noOverflow();await closeDialog(selector);});
 for(const [id,dialog,close]of [['config-interaction','#interaction-settings-form','#interaction-settings-close'],['config-voice','#tlm-voice-overlay .tlm-modal','#tlm-voice-close'],['config-mic','#tlm-mic-form','#tlm-mic-close'],['about-button','#about-overlay .about-window','#about-overlay .about-close-btn']])await check('open and close '+id,async()=>{await openAction(id);await page.until(visible(dialog),'settings dialog',30000);await bounds(dialog);await noOverflow();await page.photo('mobile-'+id);await page.click(close);await page.until('!'+visible(dialog),'settings closed');});
 await check('directory selection is usable inside the phone',async()=>{await openAction('set-dir-context');await page.until(visible('.mobile-file-picker'),'mobile folder picker',20000);await page.until('document.querySelector(".mobile-file-picker select").options.length>0','permitted roots');await bounds('.mobile-file-picker');await page.photo('mobile-folder-picker');await page.click('.mobile-file-picker footer button:first-child');});
 }
 await check('signed device renewal retains the application login',async()=>{await page.evaluate('window.TlamatiniMobile.renew()');assert.equal(await page.evaluate('!!document.querySelector("#mobile-renewal-error")'),false);const identity=await page.evaluate('fetch("/agent/remote-controller/session/").then(async r=>({status:r.status,body:await r.json()}))');assert.equal(identity.status,200);assert.equal(identity.body.authenticated,true);assert.equal(identity.body.edition,edition);});
 for(const suffix of ['/agent/torrenter/sources/','/agent/torrenter/workspace/','/agent/remote-controller/'])await check('included page loads: '+suffix,async()=>{await page.navigate(origin+prefix+suffix);await page.until(visible('.mobile-header'),'mobile page header');await noOverflow();assert.equal(await page.evaluate('!!document.querySelector("#id_password")'),false);assert.equal(await page.evaluate('document.title.includes("connection")'),false);if(suffix.includes('/workspace/')){
  await page.until('document.querySelector("#message")?.textContent==="Connected."&&!!document.querySelector("#page-summary")?.textContent','real account queue loaded',30000);
  const queue=await page.evaluate('({rows:document.querySelectorAll("#rows tr[data-job-id]").length,cards:document.querySelectorAll(".mobile-transfer-card").length,empty:document.querySelector(".mobile-empty-transfers")?.textContent,metrics:[...document.querySelectorAll(".mobile-transfer-card")].map(c=>c.querySelectorAll("dt").length)})');
  assert.equal(queue.cards,queue.rows);if(queue.rows)assert.ok(queue.metrics.every(n=>n>0));else assert.ok(queue.empty?.length>0,'Empty queue explains its state');
  await bounds('.mobile-transfer-sort');await page.click('.mobile-transfer-filters summary');await bounds('#search');await page.click('.mobile-transfer-filters summary');
 }await page.photo('mobile-page-'+suffix.split('/').filter(Boolean).at(-1));});
 await page.navigate(origin+prefix+'/agent/agent/');await page.until('!!window.TlamatiniMobileInventory','return to mobile chat');
 await check('no uncaught browser exceptions',async()=>assert.deepEqual(page.errors,[]));
 await page.photo('mobile-complete');
 await check('mobile logout returns to the normal sign-in page',async()=>{await openAction('logout-button');await page.until('!!document.querySelector("#id_username")&&!!document.querySelector("#id_password")','normal account logout',30000);await page.photo('mobile-logged-out');});
}catch(error){console.error(error);process.exitCode=1;}
finally{await report();page.close();console.log('MOBILE '+edition+': '+results.filter(x=>x.passed).length+'/'+results.length+' passed.');}
