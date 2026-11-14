// Tlachihuani — Created for Angela López Mendoza · XAIHT.
// Shared headed-browser driver for installed acceptance. No response fixtures.
import {spawn} from 'node:child_process';
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
export const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
export const python='C:/Development/XAIHT/Tlamatini/python/python.exe';
export const rcImage='C:/Program Files/XAIHT/Tlachihuani/RCServer/Tlachihuani.RC.Server.exe';
export const sleep=ms=>new Promise(r=>setTimeout(r,ms));
export async function poll(fn,label,timeout=30000) {
  const deadline=Date.now()+timeout;let last;
  while(Date.now()<deadline){try{const value=await fn();if(value)return value;}catch(e){last=e;}await sleep(250);}
  throw new Error('Timed out: '+label+(last?'; '+last.message:''));
}
export async function native(body) {
  return await new Promise((resolve,reject)=>{
    const child=spawn(rcImage,['--control'],{stdio:['pipe','pipe','inherit'],windowsHide:false});
    let output='';const timer=setTimeout(()=>{child.kill();reject(new Error('Native helper timeout: '+body.op));},15000);
    child.stdout.on('data',b=>{output+=b;if(output.length>65536){child.kill();reject(new Error('Oversize native response'));}});
    child.once('error',e=>{clearTimeout(timer);reject(e);});
    child.once('exit',code=>{clearTimeout(timer);try{const v=JSON.parse(output);if(code||v.error)throw new Error(v.error?.message||'Native helper failed');resolve(v);}catch(e){reject(e);}});
    child.stdin.on('error',()=>{});child.stdin.end(JSON.stringify(body)+'\n');
  });
}
export async function launchBrowser(output) {
  const profile=path.join(output,'profile'),scratch=path.join(output,'scratch');
  await fs.mkdir(path.join(profile,'Default'),{recursive:true});await fs.mkdir(scratch,{recursive:true});
  // This owned QA profile uses the application's seeded local credentials.
  // Suppress password-manager UI here so it cannot cover a headed test. The
  // user's normal browser profile and application credentials are untouched.
  // Chromium pref: components/password_manager/core/common/password_manager_pref_names.h
  await fs.writeFile(path.join(profile,'Default','Preferences'),JSON.stringify({credentials_enable_service:false,credentials_enable_autosignin:false,profile:{password_manager_enabled:false,password_manager_leak_detection:false}}));
  await fs.rm(path.join(profile,'DevToolsActivePort'),{force:true});
  const startedAt=Date.now();
  const browser=spawn('C:/Program Files/Google/Chrome/Application/chrome.exe',[
    '--user-data-dir='+profile,'--remote-debugging-port=0','--remote-debugging-address=127.0.0.1',
    '--no-first-run','--no-default-browser-check','--disable-background-networking','--disable-sync',
    '--disable-extensions','--window-size=1200,1000','--new-window','about:blank',
  ],{stdio:'ignore',detached:true,windowsHide:false,env:{...process.env,TEMP:scratch,TMP:scratch,TMPDIR:scratch}});
  await new Promise((r,j)=>{browser.once('spawn',r);browser.once('error',j);});browser.unref();
  const debugPort=await poll(async()=>Number((await fs.readFile(path.join(profile,'DevToolsActivePort'),'utf8')).split('\n')[0]),'headed Chrome');
  const metadata={pid:browser.pid,startedAt,debugPort,output};
  await fs.writeFile(path.join(output,'browser.json'),JSON.stringify(metadata,null,2));
  return metadata;
}
export async function attachBrowser(metadata, desiredUrl) {
  const pages=await(await fetch(`http://127.0.0.1:${metadata.debugPort}/json/list`)).json();
  const target=pages.find(x=>x.type==='page'&&(desiredUrl?x.url===desiredUrl:metadata.targetId?x.id===metadata.targetId:true));assert.ok(target,'Owned Chrome page');
  metadata.targetId=target.id;
  await fs.writeFile(path.join(metadata.output,'browser.json'),JSON.stringify(metadata,null,2));
  const ws=new WebSocket(target.webSocketDebuggerUrl);await new Promise((r,j)=>{ws.addEventListener('open',r,{once:true});ws.addEventListener('error',j,{once:true});});
  const pending=new Map();let sequence=0;const errors=[];const dialogs=[];const responses=[];let dialogApproval=null;
  ws.onmessage=({data})=>{const e=JSON.parse(data);if(e.id&&pending.has(e.id)){const p=pending.get(e.id);pending.delete(e.id);clearTimeout(p.timer);e.error?p.reject(new Error(e.error.message)):p.resolve(e.result);}if(e.method==='Runtime.exceptionThrown')errors.push(e.params.exceptionDetails.exception?.description||e.params.exceptionDetails.text);if(e.method==='Network.responseReceived'){const r=e.params.response;responses.push({path:new URL(r.url).pathname,status:r.status});if(responses.length>256)responses.shift();}if(e.method==='Page.javascriptDialogOpening'){dialogs.push(e.params);const accept=!!dialogApproval&&e.params.message.includes(dialogApproval);dialogApproval=null;void send('Page.handleJavaScriptDialog',{accept});}};
  const send=(method,params={})=>new Promise((resolve,reject)=>{const id=++sequence;const timer=setTimeout(()=>{pending.delete(id);reject(new Error('CDP timeout: '+method));},15000);pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({id,method,params}));});
  const evaluate=async expression=>{const r=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw new Error(r.exceptionDetails.exception?.description||r.exceptionDetails.text);return r.result.value;};
  let identity=metadata.window&&metadata.creation_time?{window:metadata.window,creation_time:metadata.creation_time}:{};
  const focus=async()=>{
    await send('Page.bringToFront');
    const args=[path.join(root,'tests/integration/rc_browser_focus.py'),'--pid',String(metadata.pid),'--started-after-ms',String(metadata.startedAt),'--visible-development'];
    if(identity.creation_time)args.push('--creation-time',identity.creation_time);
    if(identity.window)args.push('--window',identity.window);
    // Tlamatini animates its document title while connecting. Once Windows has
    // pinned this owned HWND and process creation time, a changing title must
    // not make a verified window appear to belong to someone else.
    const title=await evaluate('document.title');if(title&&!identity.window)args.push('--title',title);
    identity=await new Promise((resolve,reject)=>{const p=spawn(python,args,{stdio:['ignore','pipe','inherit'],windowsHide:false});let out='';p.stdout.on('data',b=>out+=b);p.once('error',reject);p.once('exit',code=>{try{const v=JSON.parse(out);if(code||!v.foreground||!v.visible)throw new Error(v.error||'Chrome foreground verification failed');resolve(v);}catch(e){reject(e);}});});
    await send('Page.bringToFront');
    // Windows can foreground Chrome while its address bar still owns focus.
    // Click only a verified inert page background to focus the real renderer;
    // never emulate focus or dismiss a dialog to make the assertion pass.
    await sleep(350);
    if(!await evaluate('document.hasFocus()')){
      const background=await evaluate(`(()=>{
        if(document.visibilityState!=='visible')return null;
        for(const [x,y] of [[8,8],[8,innerHeight/2],[innerWidth/2,8],[innerWidth-30,innerHeight-12]]){
          const e=document.elementFromPoint(x,y);
          if(e&&['BODY','HTML','MAIN','DIV','HEADER','SECTION','NAV','ASIDE','FOOTER'].includes(e.tagName)
            &&!e.closest('a,button,input,textarea,select,summary,[role=button],[tabindex],[onclick]'))return{x,y};
        }
        return null;
      })()`);
      if(background){
        await send('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,...background});
        await send('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,...background});
        await sleep(100);
        if(!await evaluate('document.hasFocus()')){
          await new Promise((resolve,reject)=>{
            const click=spawn(python,[...args,'--content-x',String(background.x),'--content-y',String(background.y)],{stdio:['ignore','pipe','inherit'],windowsHide:false});
            let output='';click.stdout.on('data',chunk=>output+=chunk);click.once('error',reject);
            click.once('exit',code=>{try{const result=JSON.parse(output);if(code||!result.foreground||!result.content_focus_click)throw Error(result.error||'Real content focus failed');resolve();}catch(error){reject(error);}});
          });
        }
      }
    }
    await poll(()=>evaluate("document.hasFocus()&&document.visibilityState==='visible'"),'real document foreground',5000);
    metadata.window=identity.window;metadata.creation_time=identity.creation_time;
    await fs.writeFile(path.join(metadata.output,'browser.json'),JSON.stringify(metadata,null,2));
    await fs.appendFile(path.join(metadata.output,'foreground.jsonl'),JSON.stringify(identity)+'\n');
  };
  const until=(expression,label,timeout)=>{
    let focusedAt=0;
    return poll(async()=>{if(Date.now()-focusedAt>=10000){await focus();focusedAt=Date.now();}return evaluate(expression);},label,timeout);
  };
  let touch=false;
  const setTouch=async enabled=>{await send('Emulation.setTouchEmulationEnabled',{enabled,maxTouchPoints:1});touch=enabled;};
  const click=async selector=>{await focus();await evaluate(`document.querySelector(${JSON.stringify(selector)}).scrollIntoView({block:'center',behavior:'instant'})`);const point=await evaluate(`(()=>{const e=document.querySelector(${JSON.stringify(selector)}),r=e.getBoundingClientRect(),x=r.x+r.width/2,y=r.y+r.height/2;if(e.disabled)throw new Error('Disabled control');if(!e.contains(document.elementFromPoint(x,y)))throw new Error('Obstructed control');return{x,y};})()`);if(touch){await send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{...point,radiusX:1,radiusY:1,force:1,id:0}]});await send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});}else{await send('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,...point});await send('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,...point});}};
  const field=async(selector,text)=>{await click(selector);await send('Input.dispatchKeyEvent',{type:'keyDown',key:'a',code:'KeyA',windowsVirtualKeyCode:65,modifiers:2});await send('Input.dispatchKeyEvent',{type:'keyUp',key:'a',code:'KeyA',windowsVirtualKeyCode:65,modifiers:2});await send('Input.insertText',{text});};
  const navigate=async url=>{await focus();await send('Page.navigate',{url});await until(`location.href===${JSON.stringify(url)}&&document.readyState==='complete'`,'navigation',60000);};
  const photo=async name=>{await focus();const shot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await fs.writeFile(path.join(metadata.output,name+'.png'),Buffer.from(shot.data,'base64'));};
  await send('Page.enable');await send('Runtime.enable');
  return{send,evaluate,focus,until,click,field,navigate,photo,setTouch,errors,dialogs,responses,approveDialog:message=>{dialogApproval=message;},close:()=>ws.close()};
}
