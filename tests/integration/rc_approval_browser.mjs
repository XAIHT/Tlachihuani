// Tlachihuani — created for Angela López Mendoza · XAIHT.
// Production approval scripts in a visible browser; deterministic event/receipt fixtures.
import fs from 'node:fs/promises';
import path from 'node:path';
import http from 'node:http';
import assert from 'node:assert/strict';
import {launchBrowser,attachBrowser,root,sleep} from './rc_installed_driver.mjs';
const output=path.resolve(process.argv[2]);
await fs.mkdir(output,{recursive:true});
const roots={english:'C:/Development/XAIHT/Tlamatini',spanish:'C:/Development/XAIHT/Tlamatini-Spanish'};
const sources={};
for(const [language,base] of Object.entries(roots)){
 const agent=base+'/Tlamatini/agent';
 const dialogs=await fs.readFile(agent+'/static/agent/js/agent_page_dialogs.js','utf8');
 const chat=await fs.readFile(agent+'/static/agent/js/agent_page_chat.js','utf8');
 const handlerStart=chat.indexOf('chatSocket.onmessage = function (e) {');
 const handlerEnd=chat.indexOf('    // Handle session-restored:',handlerStart);
 assert.ok(handlerStart>0&&handlerEnd>handlerStart);
 sources[language]={agent,dialogs:dialogs.slice(dialogs.indexOf('const EXEC_PERM_PROCEED_CSS'),dialogs.indexOf('// Omissions dialog')),handler:chat.slice(handlerStart,handlerEnd)+'};\n'+chat.slice(chat.indexOf('chatSocket.onerror ='),chat.indexOf('// --- Chat input history'))};
}
const remote=await fs.readFile(path.join(root,'src/Tlachihuani.RC.Server/wwwroot/remote.js'),'utf8');
const remoteStart=remote.indexOf('async function refreshReceipts()');
const remoteEnd=remote.indexOf('async function submit(',remoteStart);
assert.ok(remoteStart>0&&remoteEnd>remoteStart);
const rcFunctions=remote.slice(remoteStart,remoteEnd);
const server=http.createServer(async(req,res)=>{try{
 const url=new URL(req.url,'http://127.0.0.1'),language=url.searchParams.get('language')||'english',source=sources[language];
 if(!source){res.writeHead(404).end();return;}
 if(url.pathname==='/dialog.css'){
  res.setHeader('Content-Type','text/css');
  res.end((await Promise.all(['ui_tokens.css','agent_page.css','dialog_theme.css'].map(name=>fs.readFile(source.agent+'/static/agent/css/'+name,'utf8')))).join('\n'));return;
 }
 if(url.pathname==='/jquery.js'||url.pathname==='/jquery-ui.js'){
  const relative=url.pathname==='/jquery.js'?'jquery/dist/jquery.min.js':url.pathname==='/jquery-ui.js'?'jquery-ui/jquery-ui.min.js':'jquery-ui/jquery-ui.min.css';
  res.setHeader('Content-Type',url.pathname.endsWith('.css')?'text/css':'text/javascript');res.end(await fs.readFile(source.agent+'/static/agent/vendor/frontend/'+relative));return;
 }
 if(url.pathname==='/approval.js'){res.setHeader('Content-Type','text/javascript');res.end(source.dialogs+'\n'+source.handler+'\n'+rcFunctions);return;}
 res.setHeader('Content-Type','text/html');res.end(`<!doctype html><meta charset="utf-8"><title>Approval synchronization QA — ${language}</title>
 <link rel="stylesheet" href="/dialog.css"><style>body{font:18px system-ui;background:#172832;color:#def;padding:32px}textarea{display:block;width:92%;height:60px}.ui-dialog{color:#172832}#rc-card{padding:25px;border:1px solid #779;margin:20px}.permission{padding:25px;background:#29454b}button{padding:14px;margin:8px}</style>
 <h1>${language}: approval synchronization</h1><p>Development fixture · real production dialog and receipt code · no installed applications</p>
 <div id="exec-permission-dialog-message" title="${language==='spanish'?'Permiso de ejecución requerido':'Execution permission required'}"><div id="exec-perm-agent"></div><div id="exec-perm-toolname"></div><textarea id="exec-perm-params"></textarea><textarea id="exec-perm-program"></textarea><textarea id="exec-perm-shell"></textarea></div>
 <article id="rc-card"><small>Chat request</small><div class="response-body"></div></article>
 <script src="/jquery.js"></script><script src="/jquery-ui.js"></script><script>
 window.sent=[];window.chatSocket={};window.applyDisconnectedSocketUi=message=>{window.disconnectedMessage=message;};window.sendChatSocketMessage=s=>sent.push(JSON.parse(s));
 window.session={};window.selectionEpoch=1;window.receipts=new Map();window.pendingFetch=[];
 window.request=()=>new Promise(resolve=>pendingFetch.push(resolve));window.controls=()=>{};window.notice=()=>{};window.has=()=>true;window.t=s=>s;window.operationName=s=>s;window.receiptError=r=>r.error||r.state;
 window.renderResponse=(node,p)=>{node.textContent=p.text||''};window.renderReceipt=(node,r)=>{node.textContent=r.result?.text||''};
 window.submit=async(kind,payload)=>{sent.push({kind,...payload});return {}};
 window.item={node:document.getElementById('rc-card'),kind:'conversation.submit',finished:false};
 window.receipt={id:'chat-fixture',kind:'conversation.submit',state:'running'};
 window.deliver=(type,id,decision)=>chatSocket.onmessage({data:JSON.stringify({type,detail:{request_id:id,decision,tool_name:'Executer',program:'Get-Date -Format o',parameters:'read-only fixture'}})});
 </script><script src="/approval.js?language=${language}"></script>`);
 }catch(error){res.writeHead(500).end(String(error));}});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const base='http://127.0.0.1:'+server.address().port;
let browser;const results=[];
async function check(name,expression){await browser.focus();assert.equal(await browser.evaluate(expression),true,name);results.push({name,passed:true});console.log('PASS '+name);}
try{
 browser=await attachBrowser(await launchBrowser(output));
 await browser.send('Network.enable');
 for(const language of Object.keys(roots)){
  await browser.navigate(base+'/?language='+language);
  await browser.until("typeof resolveExecPermissionDialog==='function'",'production approval code');
  const deliver=(type,id,decision)=>browser.evaluate('deliver('+JSON.stringify(type)+','+JSON.stringify(id)+','+JSON.stringify(decision)+')');
  const opened="jQuery('#exec-permission-dialog-message').dialog('isOpen')";
  await deliver('exec-permission-request','a');await check(language+' prompt opens',opened);
  await browser.photo(language+'-pending');
  await deliver('exec-permission-resolved','a','proceed');await check(language+' remote approval closes without sending denial','!'+opened+'&&sent.length===0&&document.body.style.overflow!=="hidden"');
  await browser.photo(language+'-resolved');
  await deliver('exec-permission-request','a');await check(language+' delayed resolved prompt stays closed','!'+opened);
  await deliver('exec-permission-request','b');await deliver('exec-permission-resolved','a','deny');await check(language+' older resolution preserves newer prompt',opened);
  await deliver('exec-permission-resolved','b','deny');await check(language+' remote denial closes prompt','!'+opened+'&&sent.length===0');
  await deliver('exec-permission-resolved','c','deny');await deliver('exec-permission-request','c');await check(language+' resolution before prompt prevents stale modal','!'+opened);
  await deliver('exec-permission-request','d');await deliver('exec-permission-request','e');await check(language+' replacement sends no spurious denial',opened+'&&sent.length===0');
  await deliver('exec-permission-resolved','d','deny');await check(language+' replaced prompt cannot close current modal',opened);
  await browser.click('.ui-dialog-buttonpane button:first-child');await check(language+' local Proceed submits exactly one request',"!"+opened+"&&sent.length===1&&sent[0].request_id==='e'&&sent[0].decision==='proceed'");
  await deliver('exec-permission-request','e');await check(language+' duplicate after local answer stays closed','!'+opened+'&&sent.length===1');
  await deliver('exec-permission-resolved','e','proceed');await check(language+' echoed decision has no duplicate response','sent.length===1');
  await deliver('exec-permission-request','f');await browser.click('.ui-dialog-buttonpane button:last-child');await check(language+' local Deny submits exactly once',"!"+opened+"&&sent.length===2&&sent[1].request_id==='f'&&sent[1].decision==='deny'");
  await deliver('exec-permission-request','g');await browser.evaluate('chatSocket.onclose({})');await check(language+' socket loss removes stale modal without answering','!'+opened+'&&sent.length===2&&!!disconnectedMessage');
  await deliver('exec-permission-request','h');await browser.evaluate('chatSocket.onerror({})');await check(language+' socket error removes stale modal without answering','!'+opened+'&&sent.length===2');
  await browser.evaluate("sent=[];receipt.pending_permission={type:'permission',request_id:'rc-a',program:'Get-Date -Format o'};renderOperationControls(receipt,item)");
  await check(language+' RC shows pending approval',"document.querySelector('.permission').dataset.requestId==='rc-a'");
  await browser.click('.permission button:last-child');await check(language+' RC approval submits matching identity once',"sent.length===1&&sent[0].request_id==='rc-a'&&sent[0].decision==='proceed'");
  await browser.evaluate('receipt.pending_permission=null;renderOperationControls(receipt,item)');await check(language+' RC resolved state removes prompt',"!document.querySelector('.permission')&&item.permissionStamp===null");
  await browser.evaluate("receipt.pending_permission={type:'permission',request_id:'rc-b'};renderOperationControls(receipt,item)");await check(language+' next RC approval stays actionable',"document.querySelector('.permission').dataset.requestId==='rc-b'");
  await browser.evaluate("receipts.set(receipt.id,item);window.first=refreshReceipts();window.second=refreshReceipts();pendingFetch[1]({...receipt,pending_permission:null});");
  await browser.until("!document.querySelector('.permission')",'new receipt resolves prompt');
  await browser.evaluate('pendingFetch[0]({...receipt});Promise.all([first,second])');await check(language+' older HTTP receipt cannot restore resolved approval',"!document.querySelector('.permission')");
  await browser.evaluate("item.finished=true;renderOperationControls(receipt,item)");await check(language+' completion removes approval and cancel controls',"!document.querySelector('.permission')&&!document.querySelector('.operation-actions')");
 }
 assert.ok(browser.responses.some(r=>r.path==='/dialog.css'&&r.status===200),'Actual application dialog styles loaded');
 assert.ok(!browser.responses.some(r=>['/dialog.css','/jquery.js','/jquery-ui.js','/approval.js'].includes(r.path)&&r.status!==200),'No failed approval assets');
 assert.deepEqual(browser.errors,[],'No browser exceptions');results.push({name:'No browser exceptions',passed:true});
 await fs.writeFile(path.join(output,'results.json'),JSON.stringify({passed:true,total:results.length,fixture:'production scripts with deterministic event and receipt inputs',results},null,2));
 console.log('Approval browser checks: '+results.length+' passed');
}catch(error){await fs.writeFile(path.join(output,'results.json'),JSON.stringify({passed:false,error:String(error),results},null,2));throw error;}
finally{if(browser){await browser.send('Browser.close').catch(()=>{});browser.close();}await new Promise(resolve=>server.close(resolve));}
