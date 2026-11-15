// Tlachihuani — Created for Angela López Mendoza · XAIHT.
// Forty headed integration cases. The RC host is real and isolated; the main
// account adapter and file HTTP endpoint are explicit deterministic fixtures.
// This is source validation, not installed release or language-model acceptance.
import {spawn} from 'node:child_process';
import fs from 'node:fs/promises';
import path from 'node:path';
import http from 'node:http';
import assert from 'node:assert/strict';
import {randomUUID,createHash} from 'node:crypto';
import {deflateRawSync} from 'node:zlib';
import {root,launchBrowser,attachBrowser,poll,sleep} from './rc_installed_driver.mjs';

const out=path.join(root,'artifacts/rc-rich-responses',String(Date.now()));
const downloads=path.join(out,'downloads');
await fs.mkdir(downloads,{recursive:true});
const image=process.env.TL_RC_TEST_IMAGE||path.join(root,'src/Tlachihuani.RC.Server/bin/Release/net10.0-windows/Tlachihuani.RC.Server.exe');
const port=12486,base='http://127.0.0.1:'+port,nonce=randomUUID().replaceAll('-','');
const checks=[],files=new Map(),contents=new Map(),requests=[];
let host,page,browser,heartbeat,binding,adapter,device,browserClosed=false;
const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
const body="document.querySelector('#messages').lastElementChild.querySelector('.response-body')";
const rich=body;
const fixture=http.createServer((req,res)=>{
  const url=new URL(req.url,'http://127.0.0.1');
  if(url.pathname==='/agent/remote-controller/response-content/'){
    const content=contents.get(url.searchParams.get('ticket'));
    const allowed=!!content&&req.headers['x-tlachihuani-binding']===content.binding;
    requests.push({kind:'content',authorized:allowed});
    if(!allowed){res.writeHead(content?403:404,{'Content-Type':'application/json'});res.end('{"error":"Content unavailable"}');return;}
    const bytes=Buffer.from(JSON.stringify(content.value));res.writeHead(200,{'Content-Type':'application/json; charset=utf-8','Content-Length':bytes.length,'Cache-Control':'no-store'});res.end(bytes);return;
  }
  if(url.pathname!=='/agent/remote-controller/response-file/'){
    requests.push({kind:'unexpected',path:url.pathname});res.writeHead(404);res.end();return;
  }
  const ticket=url.searchParams.get('ticket'),file=files.get(ticket);
  const authorized=file&&req.headers['x-tlachihuani-binding']===file.binding;
  requests.push({kind:'download',name:file?.name||null,authorized:!!authorized});
  if(!file||!authorized||file.failure){
    const status=!file?404:!authorized?403:file.failure;
    res.writeHead(status,{'Content-Type':'application/json','Cache-Control':'no-store'});
    res.end(JSON.stringify({error:{code:'file_unavailable',message:'The file is no longer available. Load the recent conversation and try again.'}}));return;
  }
  res.writeHead(200,{'Content-Type':file.mime,'Content-Disposition':"attachment; filename*=UTF-8''"+encodeURIComponent(file.name),
    'Content-Length':String(file.bytes.length),'X-Content-SHA256':file.hashOverride||sha(file.bytes),'Cache-Control':'no-store','X-Content-Type-Options':'nosniff'});
  res.end(file.bytes);
});
await new Promise((resolve,reject)=>{fixture.once('error',reject);fixture.listen(0,'127.0.0.1',resolve);});
const mainPort=fixture.address().port;

async function native(request){
  return new Promise((resolve,reject)=>{
    const child=spawn(image,['--dev-control',nonce,String(host.pid)],{stdio:['pipe','pipe','inherit'],windowsHide:false});
    let output='';const timer=setTimeout(()=>{child.kill();reject(Error('Native test control timed out: '+request.op));},15000);
    child.stdout.on('data',chunk=>output+=chunk);
    child.once('error',error=>{clearTimeout(timer);reject(error);});
    child.once('exit',code=>{clearTimeout(timer);try{const value=JSON.parse(output);if(code||value.error)throw Error(value.error?.message||'Native test control failed');resolve(value);}catch(error){reject(error);}});
    child.stdin.on('error',()=>{});child.stdin.end(JSON.stringify(request)+'\n');
  });
}
async function runCase(language,number,name,action){
  const id=(language==='es'?'ES':'EN')+String(number).padStart(2,'0');
  console.log('RUN '+id+' '+name);
  try{await action();checks.push({id,language,name,status:'PASS'});console.log('PASS '+id+' '+name);}
  catch(error){checks.push({id,language,name,status:'FAIL',error:error.stack});console.error('FAIL '+id+' '+error.stack);process.exitCode=1;}
  await fs.writeFile(path.join(out,'results.json'),JSON.stringify(report(),null,2));
}
function report(){return{source:'real isolated RC service; explicit main adapter and download endpoint fixtures; headed foreground Chrome',
  requested:{english:20,spanish:20},passed:checks.filter(x=>x.status==='PASS').length,failed:checks.filter(x=>x.status==='FAIL').length,checks};}
async function enroll(edition){
  adapter=randomUUID().replaceAll('-','');
  binding=await native({op:'bind-account',edition,account_id:randomUUID().replaceAll('-',''),display_name:edition==='spanish'?'Pruebas de respuestas y archivos':'Response and file checks',adapter_id:adapter});
  const pulse=()=>native({op:'adapter-heartbeat',adapter_id:adapter,ready:true,capabilities:{execution_options:true,self_modify_available:true,self_modify_active:false,self_modify_locked:false,model:'explicit rich-response test adapter',web_port:mainPort}});
  await pulse();heartbeat=setInterval(()=>pulse().catch(error=>console.error('Fixture heartbeat: '+error.message)),3000);
  await page.navigate(base+'/remote.html#security');
  await page.until('!document.querySelector("#enroll").disabled','fresh enrollment');
  const invite=await native({op:'invite',binding_id:binding.id,scopes:['chat']});
  const name='Rich response '+edition;
  await page.field('#device-name',name);await page.field('#invitation',invite.invitation);await page.click('#enroll');
  await page.until('document.querySelector("#fingerprint").textContent.startsWith("Compare in XT:")','visible device fingerprint');
  const pending=(await native({op:'pending-enrollments'})).enrollments.find(x=>x.name===name);
  assert.ok(pending);assert.ok(await page.evaluate('document.querySelector("#fingerprint").textContent.includes('+JSON.stringify(pending.fingerprint)+')'));
  await native({op:'approve',enrollment_id:pending.id,fingerprint:pending.fingerprint});
  await page.until('document.querySelector("#connection").textContent==="Connected"&&!document.querySelector("[data-mode=multi_turn_enabled]").disabled','approved chat-only connection');
  device=(await native({op:'devices'})).devices.find(x=>x.name===name)?.id;
  assert.ok(device,'Approved device id');
  await page.click('nav a[href="#chat"]');
}
async function nextOperation(kind){return await poll(async()=>{
  const operation=(await native({op:'adapter-next',adapter_id:adapter})).operation;
  if(!operation)return false;
  assert.equal(operation.kind,kind);assert.equal(operation.binding_id,binding.id);return operation;
},'typed '+kind+' operation');}
async function sendResult(html,text,options={}){
  await page.click('nav a[href="#chat"]');
  await page.field('#prompt',options.prompt||text);await page.click('#send');
  const op=await nextOperation('conversation.submit');
  if(options.progress){
    await native({op:'adapter-progress',adapter_id:adapter,operation_id:op.id,progress:{type:'message',text,format:'html',html:options.progress,truncated:false}});
    await page.until(body+'.textContent.includes('+JSON.stringify(options.progressText||text)+')','rendered live progress');
    if(options.checkProgress)await options.checkProgress();
  }
  await native({op:'adapter-complete',adapter_id:adapter,operation_id:op.id,result:{text,html,format:'html',truncated:!!options.truncated,...(options.contentTicket?{content_ticket:options.contentTicket}:{})}});
  await page.until('document.querySelector("#messages").lastElementChild.querySelector("small").textContent.includes("succeeded")','completed response');
  await page.until('!!('+rich+')&&('+rich+').classList.contains("rc-rich-response")','rich response body');
  return op;
}
function addFile(name,mime,bytes,extra={}){
  const ticket=randomUUID().replaceAll('-','')+'.fixture.'+randomUUID().replaceAll('-','');
  files.set(ticket,{binding:binding.id,name,mime,bytes:Buffer.from(bytes),...extra});return ticket;
}
function fileHtml(title,ticket,name){return '<p>'+title+'</p><a href="#" data-rc-file="'+ticket+'" data-rc-filename="'+name+'">---Load in canvas: '+name+'---</a>';}
async function downloadFile(language,file){
  const ticket=addFile(file.name,file.mime,file.bytes);
  await sendResult(fileHtml(language==='es'?'Archivo preparado':'File ready',ticket,file.name),file.name);
  const button='#messages > .message:last-child .rc-file-download';
  const caption=await page.evaluate('document.querySelector('+JSON.stringify(button)+').textContent');
  assert.ok(caption.includes(language==='es'?'Guardar archivo':'Save file'),caption);
  assert.ok(!await page.evaluate(body+'.textContent.includes("Load in canvas")'),'Canvas action replaced');
  await page.click(button);
  const saved=path.join(downloads,file.name);
  await poll(async()=>{try{return sha(await fs.readFile(saved))===sha(file.bytes);}catch{return false;}},'exact downloaded '+file.name,30000);
  assert.equal(sha(await fs.readFile(saved)),sha(file.bytes),'Saved original binary bytes');
  assert.equal((await fs.stat(saved)).size,file.bytes.length);
  assert.ok(requests.some(r=>r.name===file.name&&r.authorized),'Fixed endpoint received the bound account');
  return saved;
}
function crc32(bytes){let crc=0xffffffff;for(const byte of bytes){crc^=byte;for(let i=0;i<8;i++)crc=(crc>>>1)^((crc&1)?0xedb88320:0);}return(crc^0xffffffff)>>>0;}
function zip(entries){
  let offset=0;const local=[],central=[];
  for(const [name,content] of entries){const filename=Buffer.from(name),data=Buffer.from(content),packed=deflateRawSync(data),crc=crc32(data);
    const header=Buffer.alloc(30);header.writeUInt32LE(0x04034b50,0);header.writeUInt16LE(20,4);header.writeUInt16LE(0x800,6);header.writeUInt16LE(8,8);header.writeUInt32LE(crc,14);header.writeUInt32LE(packed.length,18);header.writeUInt32LE(data.length,22);header.writeUInt16LE(filename.length,26);
    local.push(header,filename,packed);const entry=Buffer.alloc(46);entry.writeUInt32LE(0x02014b50,0);entry.writeUInt16LE(20,4);entry.writeUInt16LE(20,6);entry.writeUInt16LE(0x800,8);entry.writeUInt16LE(8,10);entry.writeUInt32LE(crc,16);entry.writeUInt32LE(packed.length,20);entry.writeUInt32LE(data.length,24);entry.writeUInt16LE(filename.length,28);entry.writeUInt32LE(offset,42);central.push(entry,filename);offset+=header.length+filename.length+packed.length;
  }
  const dir=Buffer.concat(central),end=Buffer.alloc(22);end.writeUInt32LE(0x06054b50);end.writeUInt16LE(entries.length,8);end.writeUInt16LE(entries.length,10);end.writeUInt32LE(dir.length,12);end.writeUInt32LE(offset,16);return Buffer.concat([...local,dir,end]);
}
function pdf(){
  const objects=['<< /Type /Catalog /Pages 2 0 R >>','<< /Type /Pages /Kids [3 0 R] /Count 1 >>','<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 100] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>','<< /Length '+Buffer.byteLength('BT /F1 12 Tf 20 50 Td (RC file test) Tj ET\n')+' >>\nstream\nBT /F1 12 Tf 20 50 Td (RC file test) Tj ET\nendstream','<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>'];
  let doc='%PDF-1.4\n',offsets=[0];for(let i=0;i<objects.length;i++){offsets.push(Buffer.byteLength(doc));doc+=(i+1)+' 0 obj\n'+objects[i]+'\nendobj\n';}const start=Buffer.byteLength(doc);doc+='xref\n0 6\n0000000000 65535 f \n'+offsets.slice(1).map(n=>String(n).padStart(10,'0')+' 00000 n \n').join('')+'trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n'+start+'\n%%EOF\n';return Buffer.from(doc);
}
function fileCases(language){
  const prefix=language==='es'?'informe español ñ':'English résumé';
  const text=language==='es'?'¡Hola, Ángela!\r\nInformación, pingüino, año. 😀\r\n':'Hello, Angela!\r\nUnicode résumé and café. 😀\r\n';
  const docx=zip([['[Content_Types].xml','<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>'],['_rels/.rels','<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>'],['word/document.xml','<?xml version="1.0" encoding="UTF-8"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>'+text.trim()+'</w:t></w:r></w:p></w:body></w:document>']]);
  return[
    {name:prefix+'.txt',mime:'text/plain; charset=utf-8',bytes:Buffer.from('\ufeff'+text)},
    {name:prefix+'.md',mime:'text/markdown; charset=utf-8',bytes:Buffer.from('# '+prefix+'\n\n**'+text.trim()+'**\n')},
    {name:prefix+'.html',mime:'text/html; charset=utf-8',bytes:Buffer.from('<!doctype html><meta charset="utf-8"><h1>'+prefix+'</h1><script>window.__downloadExecuted=true</script>')},
    {name:prefix+'.svg',mime:'image/svg+xml',bytes:Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="40" height="40"><rect width="40" height="40" fill="teal"/><script>window.__downloadExecuted=true</script></svg>')},
    {name:prefix+'.csv',mime:'text/csv; charset=utf-8',bytes:Buffer.from('\ufeffName,Value\r\n"'+prefix+'",42\r\n"comma, quote\"\"",3.14\r\n')},
    {name:prefix+'.json',mime:'application/json',bytes:Buffer.from(JSON.stringify({language,text,values:[0,false,null,1.25]},null,2))},
    {name:prefix+'.pdf',mime:'application/pdf',bytes:pdf()},
    {name:prefix+'.png',mime:'image/png',bytes:Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aVFkAAAAASUVORK5CYII=','base64')},
    {name:prefix+'.zip',mime:'application/zip',bytes:zip([[prefix+'/notes.txt',text],['bytes.bin',Buffer.from([0,255,128,10,13,0,42])]])},
    {name:prefix+'.docx',mime:'application/vnd.openxmlformats-officedocument.wordprocessingml.document',bytes:docx},
  ];
}
async function probeSession(){
  await page.focus();
  await page.evaluate('(async()=>{const device=await new Promise((resolve,reject)=>{const r=indexedDB.open("tlachihuani-rc-device-v1",1);r.onerror=reject;r.onsuccess=()=>{const db=r.result,q=db.transaction("identity").objectStore("identity").get(location.origin);q.onsuccess=()=>{resolve(q.result);db.close()};q.onerror=reject}});const post=async(p,b)=>fetch("/api/rc/v1"+p,{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(b)});const c=await(await post("/sessions/challenges",{device_id:device.deviceId})).json();const signature=await crypto.subtle.sign({name:"ECDSA",hash:"SHA-256"},device.key,Uint8Array.from(atob(c.challenge),c=>c.charCodeAt(0)));window.__richProbe=await(await post("/sessions",{device_id:device.deviceId,challenge_id:c.id,signature:btoa(String.fromCharCode(...new Uint8Array(signature)))})).json();if(!window.__richProbe.access_token)throw Error("Probe session failed");})()');
}
async function probeDownload(ticket,authenticated=true){
  return await page.evaluate('(async()=>{const response=await fetch('+JSON.stringify('/api/rc/v1/response-file?ticket='+encodeURIComponent(ticket))+',{headers:'+ (authenticated?'{Authorization:"Bearer "+window.__richProbe.access_token}':'{}')+',cache:"no-store"});await response.arrayBuffer();return response.status;})()');
}
async function reset(){
  clearInterval(heartbeat);await page.click('nav a[href="#security"]');
  if(await page.evaluate('document.querySelector("#connection").textContent==="Connected"')){
    try{await page.click('#disconnect');}
    catch(error){if(!/Disabled control/.test(error.message)||await page.evaluate('document.querySelector("#connection").textContent==="Connected"'))throw error;}
  }
  await page.until('document.querySelector("#connection").textContent==="Disconnected"','session ended before reset');
  await page.until('!document.querySelector("#reset-device").disabled','local reset after revocation');
  page.approveDialog('Forget this browser');await page.click('#reset-device');
  await page.until('!document.querySelector("#enroll-form").hidden','new edition enrollment');
}

try{
  host=spawn(image,['--console',path.join(out,'host'),nonce,String(port)],{cwd:root,stdio:'inherit',windowsHide:false});
  await new Promise((resolve,reject)=>{host.once('spawn',resolve);host.once('error',reject);});
  await poll(async()=>(await fetch(base+'/api/rc/v1/health')).ok,'isolated rich-response RC listener');
  browser=await launchBrowser(out);page=await attachBrowser(browser);
  await page.send('Browser.setDownloadBehavior',{behavior:'allow',downloadPath:downloads,eventsEnabled:true});
  for(const language of ['en','es']){
    await enroll(language==='es'?'spanish':'english');
    const title=language==='es'?'Hora actual':'Current time';
    const paragraph=language==='es'?'Leí el reloj de esta máquina.':'I read the clock on this machine.';
    await runCase(language,1,'Live canonical HTML preserves headings, cards and inline presentation',async()=>{
      await sendResult('<h2>'+title+'</h2><div style="padding:16px 20px;border-radius:12px;background:linear-gradient(135deg,#0a0a23 0%,#3a0ca3 100%);color:#ffffff;box-shadow:0 8px 24px rgba(58,12,163,.5)"><h3 style="color:#fde68a">'+title+'</h3><p style="font-size:1.5em;font-family:monospace">17:06:06</p><p>'+paragraph+'</p></div>',title);
      const view=await page.evaluate('(()=>{const r='+rich+',card=r.querySelector("[style*=linear-gradient]");return{heading:r.querySelector("h2")?.textContent,background:getComputedStyle(card).backgroundImage,radius:getComputedStyle(card).borderRadius,text:r.textContent};})()');
      assert.equal(view.heading,title);assert.ok(view.background.includes('linear-gradient'));assert.equal(view.radius,'12px');assert.ok(!view.text.includes('<div'));
    });
    await runCase(language,2,'Live progress and final code retain formatting without executing code',async()=>{
      await sendResult('<p><strong>'+paragraph+'</strong></p><pre><code>&lt;script&gt;window.__richUnsafe=1&lt;/script&gt;\nconst answer = 42;</code></pre>',paragraph,{progress:'<p><em>'+title+'…</em></p>',progressText:title,checkProgress:async()=>assert.equal(await page.evaluate(body+'.querySelector("em")?.textContent'),title+'…')});
      assert.ok(await page.evaluate(rich+'.querySelector("strong")?.textContent==='+JSON.stringify(paragraph)));assert.ok((await page.evaluate(rich+'.querySelector("code").textContent')).includes('<script>'));assert.equal(await page.evaluate('window.__richUnsafe||0'),0);
    });
    await runCase(language,3,'Wide tables and ASCII code stay usable at mobile width',async()=>{
      await page.send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});await page.setTouch(true);
      await sendResult('<table><thead><tr><th>'+title+'</th><th>'+('Column '.repeat(16))+'</th></tr></thead><tbody><tr><td>42</td><td>'+('0123456789'.repeat(22))+'</td></tr></tbody></table><pre class="ascii-diagram">'+('A----B----'.repeat(25))+'</pre>',title);
      assert.equal(await page.evaluate(rich+'.querySelectorAll("td").length'),2);
      assert.ok(await page.evaluate('document.documentElement.scrollWidth<=innerWidth+1'),'No page horizontal overflow');
      assert.ok(await page.evaluate('(()=>{const p='+rich+'.querySelector("pre");return p.scrollWidth>p.clientWidth&&["auto","scroll"].includes(getComputedStyle(p).overflowX)})()'),'Code has its own horizontal scroll');
      await page.photo(language+'-mobile-table');
    });
    await runCase(language,4,'Malformed answer tables cannot absorb the separate execution report',async()=>{
      await sendResult('<div><table><tr><td>'+paragraph+'<!--TLAMATINI_EXEC_REPORT_BOUNDARY--><div class="exec-report"><h3>'+ (language==='es'?'Informe de ejecución':'Execution report')+'</h3><table class="exec-report-table"><tr><td>File-Creator</td><td>OK</td></tr></table></div>',title);
      assert.ok(await page.evaluate('(()=>{const r='+rich+',a=r.querySelector(".automated-message-body"),b=r.querySelector(".automated-message-execreport");return !!a&&!!b&&!a.contains(b)&&!!b.querySelector(".exec-report-table")})()'));
      await page.photo(language+'-report-boundary');
    });
    await runCase(language,5,'Recent history renders each assistant response and keeps user markup literal',async()=>{
      await page.click('#history');const op=await nextOperation('conversation.history');
      const literal='<b>'+title+'</b><script>window.__richUnsafe=1</script>';
      await native({op:'adapter-complete',adapter_id:adapter,operation_id:op.id,result:{messages:[{id:1,role:'user',text:literal,created_at:'2026-11-14T17:00:00Z'},{id:2,role:'assistant',text:paragraph,html:'<h2>'+title+'</h2><p><strong>'+paragraph+'</strong></p>',format:'html',created_at:'2026-11-14T17:00:01Z'}],truncated:false}});
      await page.until('document.querySelector("#messages").lastElementChild.querySelector("small").textContent.includes("succeeded")','rich history response');
      assert.ok(await page.evaluate(body+'.textContent.includes('+JSON.stringify(literal)+')'));assert.equal(await page.evaluate(body+'.querySelectorAll("h2").length'),1);assert.equal(await page.evaluate('window.__richUnsafe||0'),0);assert.ok(!await page.evaluate(body+'.querySelector("script")'));
    });
    await runCase(language,6,'Active markup, handlers, unsafe URLs and network CSS are inert',async()=>{
      const before=requests.length;
      await sendResult('<p>'+paragraph+'</p><script>window.__richUnsafe=2</script><img src="http://127.0.0.1:'+mainPort+'/trap" onerror="window.__richUnsafe=3"><a href="javascript:window.__richUnsafe=4">unsafe</a><iframe src="http://127.0.0.1:'+mainPort+'/trap"></iframe><div style="position:fixed;inset:0;background-image:url(http://127.0.0.1:'+mainPort+'/trap)" onclick="window.__richUnsafe=5">contained</div><svg onload="window.__richUnsafe=6"></svg>',paragraph);
      await sleep(300);assert.equal(await page.evaluate('window.__richUnsafe||0'),0);assert.ok(!await page.evaluate(rich+'.querySelector("script,iframe,svg,[onclick],[onerror],[onload],[href^=javascript]")'));
      assert.equal(requests.slice(before).filter(x=>x.kind==='unexpected').length,0,'No markup-initiated network request');
      assert.ok(await page.evaluate('Array.from('+rich+'.querySelectorAll("*")).every(e=>getComputedStyle(e).position!=="fixed")'));
    });
    await runCase(language,7,'Ordinary external links stay safe and readable',async()=>{
      await sendResult('<p>'+paragraph+' <a href="https://example.com/reference?q=hello%20world">'+title+'</a></p>',title);
      const link=await page.evaluate('(()=>{const a='+rich+'.querySelector("a");return{href:a.href,text:a.textContent,target:a.target,rel:a.rel}})()');
      assert.equal(link.href,'https://example.com/reference?q=hello%20world');assert.equal(link.text,title);assert.equal(link.target,'_blank');assert.ok(link.rel.includes('noopener'));
    });
    await runCase(language,8,'Long rich responses arrive completely and genuine truncation is clearly identified',async()=>{
      const contentTicket=randomUUID().replaceAll('-','')+'.content.fixture';
      const marker=language==='es'?'FINAL COMPLETO — ÚLTIMO PÁRRAFO':'COMPLETE END — FINAL PARAGRAPH';
      const longHtml=('<p><strong>'+paragraph+'</strong> '+title+' — 0123456789</p>').repeat(1000)+'<h3>'+marker+'</h3>';
      assert.ok(Buffer.byteLength(longHtml)>32768);
      contents.set(contentTicket,{binding:binding.id,value:{format:'html',html:longHtml,text:paragraph,truncated:false}});
      await sendResult(null,paragraph,{contentTicket,truncated:true});
      await page.until(body+'.querySelector("h3")?.textContent==='+JSON.stringify(marker),'complete long rich response');
      assert.equal(await page.evaluate(body+'.querySelectorAll("p strong").length'),1000);
      assert.equal(await page.evaluate(body+'.querySelector(".rc-response-truncated")'),null);
      await sendResult(null,paragraph,{truncated:true});
      const text=await page.evaluate(body+'.textContent');assert.ok(text.includes(paragraph));assert.equal(await page.evaluate(body+'.querySelector(".rc-response-truncated")?.textContent'),language==='es'?'Esta respuesta se acortó durante la entrega.':'This response was shortened during delivery.');
      assert.ok(await page.evaluate('document.documentElement.scrollWidth<=innerWidth+1'));await page.photo(language+'-rich-response-mobile');
    });
    const samples=fileCases(language);
    for(let i=0;i<samples.length;i++)await runCase(language,9+i,'Save original '+path.extname(samples[i].name)+' bytes and Unicode filename',async()=>{
      await downloadFile(language,samples[i]);assert.equal(await page.evaluate('window.__downloadExecuted||false'),false,'Downloaded HTML/SVG is never executed');
    });
    await runCase(language,19,'Unavailable files show an error, save nothing, and allow an explicit retry',async()=>{
      const file={name:(language==='es'?'reintento español':'retry English')+'.txt',mime:'text/plain',bytes:Buffer.from(paragraph)},ticket=addFile(file.name,file.mime,file.bytes,{failure:410});
      await sendResult(fileHtml(title,ticket,file.name),file.name);
      const before=(await fs.readdir(downloads)).length;await page.click('#messages > .message:last-child .rc-file-download');
      await page.until(body+'.querySelector(".rc-file-status.error")?.textContent.length>0','visible download failure');
      assert.equal((await fs.readdir(downloads)).length,before,'Failure creates no fake document');
      await page.until('!document.querySelector("#messages > .message:last-child .rc-file-download").disabled','retry available');
      files.get(ticket).failure=0;files.get(ticket).hashOverride='0'.repeat(64);await page.click('#messages > .message:last-child .rc-file-download');
      await page.until(body+'.querySelector(".rc-file-status.error")?.textContent.length>0','corrupt download refused');
      assert.equal((await fs.readdir(downloads)).length,before,'Failed integrity check creates no document');
      files.get(ticket).hashOverride=null;await page.until('!document.querySelector("#messages > .message:last-child .rc-file-download").disabled','integrity retry available');await page.click('#messages > .message:last-child .rc-file-download');
      await poll(async()=>{try{return sha(await fs.readFile(path.join(downloads,file.name)))===sha(file.bytes);}catch{return false;}},'successful explicit retry');
    });
    await runCase(language,20,'Downloads enforce authentication, account binding and revocation',async()=>{
      const ticket=addFile(language+'-protected.bin','application/octet-stream',Buffer.from([0,255,42]));
      const before=requests.length;assert.equal(await probeDownload(ticket,false),401);assert.equal(requests.length,before,'Anonymous requests never reach the main app');
      await probeSession();files.get(ticket).binding='0'.repeat(32);assert.equal(await probeDownload(ticket),403,'Wrong account rejected');files.get(ticket).binding=binding.id;
      assert.equal(await probeDownload(ticket),200,'Approved matching account can read');
      const grant=(await native({op:'devices'})).devices.find(item=>item.id===device);
      await native({op:'set-device-scopes',device_id:device,revision:grant.revision,scopes:['monitor']});await probeSession();
      const limitedCount=requests.length;assert.equal(await probeDownload(ticket),403,'Monitoring permission cannot download conversation files');assert.equal(requests.length,limitedCount,'Scope refusal precedes upstream access');
      await native({op:'revoke',device_id:device});const count=requests.length;assert.equal(await probeDownload(ticket),401,'Revoked device denied');assert.equal(requests.length,count,'Revocation stops upstream file access');
      await page.until('document.querySelector("#connection").textContent==="Disconnected"','client observes revoked connection');
      assert.equal(page.errors.length,0,'No uncaught browser exceptions');
    });
    await reset();await page.setTouch(false);await page.send('Emulation.clearDeviceMetricsOverride');
  }
  assert.equal(checks.filter(c=>c.language==='en').length,20);assert.equal(checks.filter(c=>c.language==='es').length,20);
}catch(error){console.error(error.stack);checks.push({id:'HARNESS',name:'Harness infrastructure',status:'FAIL',error:error.stack});process.exitCode=1;}
finally{
  clearInterval(heartbeat);
  if(page){try{await page.send('Browser.close');}catch{}page.close();}
  if(browser)await poll(async()=>{try{await fetch('http://127.0.0.1:'+browser.debugPort+'/json/version');return false;}catch{return true;}},'owned QA Chrome closed',15000).then(()=>{browserClosed=true;}).catch(error=>{console.error(error.message);process.exitCode=1;});
  if(host&&host.exitCode===null){try{await native({op:'development-stop'});}catch(error){console.error(error.message);process.exitCode=1;}await poll(()=>host.exitCode!==null,'isolated RC host stopped',15000).catch(error=>{console.error(error.message);process.exitCode=1;});}
  await new Promise(resolve=>fixture.close(resolve));
  await fs.writeFile(path.join(out,'results.json'),JSON.stringify(report(),null,2));
  await fs.writeFile(path.join(out,'cleanup.json'),JSON.stringify({isolatedHostExited:!host||host.exitCode!==null,browserClosed:!browser||browserClosed,fixtureClosed:!fixture.listening},null,2));
  console.log('Rich-response evidence: '+out);console.log('Cases: '+checks.filter(c=>c.status==='PASS').length+' passed; '+checks.filter(c=>c.status==='FAIL').length+' failed.');
}
