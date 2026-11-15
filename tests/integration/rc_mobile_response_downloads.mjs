// Tlachihuani — Created for Angela López Mendoza · XAIHT.
// Visible fixture acceptance for the exact full-workspace script and each main
// edition's extracted buildAutomatedMessageElement. It does not replace Django
// authorization tests or certify an installed mobile gateway.
import fs from 'node:fs/promises';
import path from 'node:path';
import http from 'node:http';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {root,launchBrowser,attachBrowser,poll,sleep} from './rc_installed_driver.mjs';

const out=path.join(root,'artifacts/rc-rich-responses','workspace-'+Date.now());
const downloads=path.join(out,'downloads');await fs.mkdir(downloads,{recursive:true});
const web=path.join(root,'src/Tlachihuani.RC.Server/wwwroot');
const checks=[],requests=[],files=new Map(),renderers={};
const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
let page,browser,browserClosed=false;
for(const [lang,checkout] of [['en','Tlamatini'],['es','Tlamatini-Spanish']]){
  const source=await fs.readFile('C:/Development/XAIHT/'+checkout+'/Tlamatini/agent/static/agent/js/agent_page_chat.js','utf8');
  const start=source.indexOf('function buildAutomatedMessageElement('),end=source.indexOf('function appendChatMessage(',start);
  assert.ok(start>=0&&end>start,'Main renderer source boundaries');
  renderers[lang]={code:'const EXEC_REPORT_BOUNDARY="<!--TLAMATINI_EXEC_REPORT_BOUNDARY-->";\n'+source.slice(start,end),sha256:sha(Buffer.from(source.slice(start,end)))};
}
const escapeHtml=value=>String(value).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;').replaceAll("'",'&#39;');
function canvasHtml(name,title){return '<div style="padding:16px 20px;border-radius:12px;background:linear-gradient(135deg,#0a0a23 0%,#3a0ca3 100%);color:#ffffff;box-shadow:0 8px 24px rgba(58,12,163,.5)"><h2 style="color:#fde68a">'+title+'</h2><p style="font-size:1.5em;font-family:monospace">17:06:06</p><p>'+title+'</p></div><a href="#" onclick="'+escapeHtml('loadCanvas('+JSON.stringify(name)+');')+'">---Load in canvas: '+escapeHtml(name)+'---</a>';}
function fixtureScript(lang){
  return 'window.TlamatiniMobile={edition:'+JSON.stringify(lang==='es'?'spanish':'english')+',prefix:"",route:value=>value};window.__saveAttempts=0;new MutationObserver(records=>{for(const record of records)for(const node of record.addedNodes)if(node.nodeType===1&&node.matches("a[download]"))window.__saveAttempts++;}).observe(document.documentElement,{childList:true,subtree:true});window.__canvasCalls=0;window.loadCanvas=()=>window.__canvasCalls++;window.appendFixture=(id,html)=>{const node=document.createElement("div");node.className="message bot-message";node.dataset.messageId=String(id);node.append(buildAutomatedMessageElement(html));document.querySelector("#chat-log").append(node);};';
}
const server=http.createServer(async(req,res)=>{
  try{
    const url=new URL(req.url,'http://127.0.0.1');const lang=url.searchParams.get('lang')==='es'?'es':'en';
    if(url.pathname==='/'){
      res.writeHead(200,{'Content-Type':'text/html; charset=utf-8','Cache-Control':'no-store'});
      const title=lang==='es'?'Respuestas de Tlamatini':'Tlamatini responses';
      res.end('<!doctype html><html lang="'+lang+'"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>'+title+'</title><link rel="stylesheet" href="/mobile-workspace.css"><style>body{font:16px system-ui;margin:0;padding:80px 18px 110px;background:#102b30;color:#edf6f7}#chat-log{max-width:700px;margin:auto}.message{padding:18px;margin:12px 0;background:#203840;border-radius:12px;overflow-wrap:anywhere}button{padding:14px;max-width:100%;white-space:normal}a{color:#8be0d2}</style><script src="/main-render.js?lang='+lang+'"></script><script src="/fixture.js?lang='+lang+'"></script><script defer src="/mobile-download.js"></script><script defer src="/mobile-workspace.js"></script></head><body><div id="navbar-container"><div id="menu-editor"></div></div><div id="chat-log"></div></body></html>');return;
    }
    if(url.pathname==='/main-render.js'){res.writeHead(200,{'Content-Type':'text/javascript; charset=utf-8'});res.end(renderers[lang].code);return;}
    if(url.pathname==='/fixture.js'){res.writeHead(200,{'Content-Type':'text/javascript; charset=utf-8'});res.end(fixtureScript(lang));return;}
    if(['/mobile-workspace.js','/mobile-download.js','/mobile-workspace.css'].includes(url.pathname)){
      res.writeHead(200,{'Content-Type':url.pathname.endsWith('.css')?'text/css; charset=utf-8':'text/javascript; charset=utf-8'});res.end(await fs.readFile(path.join(web,url.pathname.slice(1))));return;
    }
    if(url.pathname==='/agent/mobile/response-file/'){
      const id=url.searchParams.get('message_id'),name=url.searchParams.get('name'),file=files.get(id+'\n'+name);
      requests.push({id,name,found:!!file});
      if(!file||file.status){res.writeHead(file?.status||404,{'Content-Type':'application/json'});res.end('{"error":"File unavailable"}');return;}
      res.writeHead(200,{'Content-Type':file.mime,'Content-Length':file.bytes.length,'Content-Disposition':"attachment; filename*=UTF-8''"+encodeURIComponent(file.downloadName||file.name),'X-Content-SHA256':file.badHash?'0'.repeat(64):sha(file.bytes)});res.end(file.bytes);return;
    }
    res.writeHead(404);res.end();
  }catch(error){res.writeHead(500);res.end('Fixture failed: '+error.message);}
});
await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(0,'127.0.0.1',resolve);});
const base='http://127.0.0.1:'+server.address().port;
async function test(lang,index,name,fn){
  const id=(lang==='es'?'ES':'EN')+'M'+index;console.log('RUN '+id+' '+name);
  try{await fn();checks.push({id,name,status:'PASS'});console.log('PASS '+id+' '+name);}
  catch(error){checks.push({id,name,status:'FAIL',error:error.stack});console.error(error.stack);process.exitCode=1;}
}
function addFile(id,name,mime,bytes,extra={}){const file={id:String(id),name,mime,bytes:Buffer.from(bytes),...extra};files.set(file.id+'\n'+name,file);return file;}
async function append(file,title){
  await page.focus();await page.evaluate('window.appendFixture('+JSON.stringify(file.id)+','+JSON.stringify(canvasHtml(file.name,title))+')');
  await page.until('!!document.querySelector(\'.message[data-message-id="'+file.id+'"] .mobile-response-download\')','saved response download conversion');
}
const button=file=>'.message[data-message-id="'+file.id+'"] .mobile-response-download';
const status=file=>'.message[data-message-id="'+file.id+'"] .mobile-response-download-status';
async function save(file){
  const attempts=await page.evaluate('window.__saveAttempts');await page.click(button(file));
  await poll(async()=>{try{return sha(await fs.readFile(path.join(downloads,file.name)))===sha(file.bytes);}catch{return false;}},'saved '+file.name);
  assert.equal((await fs.stat(path.join(downloads,file.name))).size,file.bytes.length);assert.equal(await page.evaluate('window.__saveAttempts'),attempts+1,'Exactly one browser save was triggered');
}
try{
  browser=await launchBrowser(out);page=await attachBrowser(browser);
  await page.send('Browser.setDownloadBehavior',{behavior:'allow',downloadPath:downloads,eventsEnabled:true});
  await page.send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});await page.setTouch(true);
  for(const lang of ['en','es']){
    await page.navigate(base+'/?lang='+lang);await page.until('document.body.dataset.mobileView==="chat"','full workspace mobile navigation');
    const title=lang==='es'?'Archivo creado':'Created file',prefix=lang==='es'?'móvil español ñ':'mobile English résumé';
    const text=addFile(lang==='es'?201:101,prefix+'.txt','text/plain; charset=utf-8',Buffer.from('\ufeff'+title+'\r\nÁngela 😀\r\n'));
    await test(lang,1,'Main history renderer transforms its canvas action into localized Save file',async()=>{
      await append(text,title);assert.equal(await page.evaluate('document.querySelector(\'.message[data-message-id="'+text.id+'"] h2\').textContent'),title);
      const caption=await page.evaluate('document.querySelector('+JSON.stringify(button(text))+').textContent');assert.ok(caption.startsWith(lang==='es'?'Guardar archivo: ':'Save file: '));
      assert.equal(await page.evaluate('document.querySelectorAll("#chat-log a[onclick]").length'),0);
      assert.ok(await page.evaluate('getComputedStyle(document.querySelector(\'.message[data-message-id="'+text.id+'"] [style*=linear-gradient]\')).backgroundImage.includes("linear-gradient")'));
      await page.focus();await page.evaluate('document.querySelector(\'.message[data-message-id="'+text.id+'"]\').scrollIntoView({block:"center",behavior:"instant"})');await page.photo(lang+'-main-gradient-and-save');
    });
    await test(lang,2,'Unicode text downloads retain exact bytes and filename',async()=>{await save(text);assert.equal(await page.evaluate('window.__canvasCalls'),0);assert.equal(await page.evaluate('document.body.dataset.mobileView'),'chat');});
    await test(lang,3,'New HTML response downloads locally without navigation or script execution',async()=>{
      const file=addFile(lang==='es'?202:102,prefix+'.html','text/html; charset=utf-8',Buffer.from('<!doctype html><meta charset="utf-8"><h1>'+title+'</h1><script>window.__fileExecuted=true</script>'));
      await append(file,title);await save(file);assert.equal(await page.evaluate('window.__fileExecuted||false'),false);assert.equal(await page.evaluate('location.pathname'),'/');
    });
    await test(lang,4,'New SVG and PNG files use original binary download delivery',async()=>{
      const svg=addFile(lang==='es'?203:103,prefix+'.svg','image/svg+xml',Buffer.from('<svg xmlns="http://www.w3.org/2000/svg"><rect width="40" height="40"/><script>window.__fileExecuted=true</script></svg>'));
      const png=addFile(lang==='es'?204:104,prefix+'.png','image/png',Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aVFkAAAAASUVORK5CYII=','base64'));
      for(const file of [svg,png]){await append(file,title);await save(file);}assert.equal(await page.evaluate('window.__fileExecuted||false'),false);
    });
    await test(lang,5,'Missing and corrupted files save nothing and keep explicit retry available',async()=>{
      const file=addFile(lang==='es'?205:105,prefix+' retry.json','application/json',Buffer.from(JSON.stringify({title,ok:true})),{status:410});
      await append(file,title);const beforeFiles=await fs.readdir(downloads),attempts=await page.evaluate('window.__saveAttempts');await page.click(button(file));
      await page.until('!document.querySelector('+JSON.stringify(button(file))+').disabled&&document.querySelector('+JSON.stringify(status(file))+').textContent.includes(' +JSON.stringify(lang==='es'?'no está disponible':'unavailable')+')','missing file error');assert.equal(await page.evaluate('window.__saveAttempts'),attempts,'Failure does not trigger a browser save');assert.equal(await fs.stat(path.join(downloads,file.name)).then(()=>true,()=>false),false,'Failure produces no requested file');await fs.writeFile(path.join(out,lang+'-missing-file-directory.json'),JSON.stringify({before:beforeFiles,after:await fs.readdir(downloads)},null,2));
      file.status=0;file.badHash=true;await page.click(button(file));await page.until('!document.querySelector('+JSON.stringify(button(file))+').disabled&&document.querySelector('+JSON.stringify(status(file))+').textContent.includes('+JSON.stringify(lang==='es'?'verificación':'verification')+')','integrity error');assert.equal(await page.evaluate('window.__saveAttempts'),attempts,'Corrupt bytes do not trigger a browser save');assert.equal(await fs.stat(path.join(downloads,file.name)).then(()=>true,()=>false),false,'Integrity failure produces no requested file');await fs.writeFile(path.join(out,lang+'-integrity-file-directory.json'),JSON.stringify({before:beforeFiles,after:await fs.readdir(downloads)},null,2));
      file.badHash=false;await save(file);
    });
    await test(lang,6,'Long Unicode names preserve extension, saved-message identity and mobile layout',async()=>{
      const long=addFile(lang==='es'?206:106,prefix+' long-name.pdf','application/pdf',Buffer.from('%PDF-1.4\n% '+title+'\n%%EOF\n'),{downloadName:prefix+' '+('😀'.repeat(140))+'.pdf'});
      await append(long,title);const previous=new Set(await fs.readdir(downloads));await page.click(button(long));
      const saved=await poll(async()=>{for(const name of await fs.readdir(downloads)){if(previous.has(name)||!name.endsWith('.pdf'))continue;try{if(sha(await fs.readFile(path.join(downloads,name)))===sha(long.bytes))return name;}catch{}}return false;},'long Unicode PDF saved');
      assert.ok(saved.length<=240&&saved.endsWith('.pdf'),'Bounded filename retains PDF extension');assert.ok(saved.includes('😀'),'Emoji survived shortening');assert.ok(!Array.from(saved).some(character=>character.length===1&&character.charCodeAt(0)>=0xd800&&character.charCodeAt(0)<=0xdfff),'No unpaired UTF-16 surrogate');
      await fs.writeFile(path.join(out,lang+'-long-filename.json'),JSON.stringify({advertisedUnits:long.downloadName.length,savedName:saved,savedUnits:saved.length,sha256:sha(await fs.readFile(path.join(downloads,saved)))},null,2));
      for(const file of files.values())if(file.id.startsWith(lang==='es'?'2':'1'))assert.ok(requests.some(r=>r.id===file.id&&r.name===file.name&&r.found),'Exact id/name selected: '+file.name);
      assert.equal(await page.evaluate('window.__canvasCalls'),0);assert.equal(page.errors.length,0);assert.ok(await page.evaluate('document.documentElement.scrollWidth<=innerWidth+1'));await page.photo(lang+'-full-workspace-downloads');
    });
  }
}catch(error){checks.push({id:'HARNESS',name:'Harness infrastructure',status:'FAIL',error:error.stack});console.error(error.stack);process.exitCode=1;}
finally{
  if(page){try{await page.send('Browser.close');}catch{}page.close();}
  if(browser)await poll(async()=>{try{await fetch('http://127.0.0.1:'+browser.debugPort+'/json/version');return false;}catch{return true;}},'owned workspace browser closed',15000).then(()=>{browserClosed=true;}).catch(error=>{console.error(error.message);process.exitCode=1;});
  await new Promise(resolve=>server.close(resolve));
  await fs.writeFile(path.join(out,'results.json'),JSON.stringify({source:'exact full-workspace assets and extracted main renderer in explicit local fixture; headed Chrome; no installed or gateway acceptance',mainRenderers:Object.fromEntries(Object.entries(renderers).map(([lang,item])=>[lang,item.sha256])),passed:checks.filter(c=>c.status==='PASS').length,failed:checks.filter(c=>c.status==='FAIL').length,checks},null,2));
  await fs.writeFile(path.join(out,'cleanup.json'),JSON.stringify({browserClosed:!browser||browserClosed,fixtureClosed:!server.listening},null,2));
  console.log('Full-workspace download evidence: '+out);
}
