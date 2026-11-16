// Visible, read-only evidence when the real Chrome window and document disagree.
import fs from 'node:fs/promises';
import path from 'node:path';
import {root,attachBrowser} from './rc_installed_driver.mjs';
const output=path.resolve(process.argv[2]);if(!output.startsWith(path.join(root,'artifacts')+path.sep))throw new Error('Expected owned test artifacts.');
const page=await attachBrowser(JSON.parse(await fs.readFile(path.join(output,'browser.json'),'utf8')));
try{
 if(process.argv[3])await page.send('Emulation.setDeviceMetricsOverride',{width:Number(process.argv[3]),height:844,deviceScaleFactor:1,mobile:true});
 try{await page.focus();}catch(error){console.log('Document focus observation: '+error.message);}
 const state=await page.evaluate('({title:document.title,path:location.pathname,focused:document.hasFocus(),visibility:document.visibilityState,active:document.activeElement?.tagName,width:innerWidth,scrollWidth:document.documentElement.scrollWidth})');
 console.log(state);await fs.writeFile(path.join(output,'focus-diagnosis.json'),JSON.stringify(state,null,2));
 const csrf=await page.evaluate(`(()=>{const cookies=document.cookie.split(';').map(c=>c.trim()).filter(c=>c.startsWith('csrftoken=')),cookie=cookies[0]?.slice(10),form=document.querySelector('[name=csrfmiddlewaretoken]')?.value;const alphabet='abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789',secret=t=>t?.length===64?[...t.slice(32)].map((c,i)=>alphabet[(alphabet.indexOf(c)-alphabet.indexOf(t[i])+62)%62]).join(''):t;return{cookieCount:cookies.length,cookiePresent:!!cookie,formPresent:!!form,sameSecret:secret(cookie)===secret(form),cookieLength:cookie?.length,formLength:form?.length}})()`);console.log(csrf);await fs.writeFile(path.join(output,'csrf-diagnosis.json'),JSON.stringify(csrf,null,2));
 const files=await page.evaluate(`({inputs:[...document.querySelectorAll('input[type=file]')].map(e=>({id:e.id,files:[...e.files].map(f=>({name:f.name,type:f.type,size:f.size}))})),chips:document.querySelector('#chat-image-chips')?.outerHTML,message:document.querySelector('#chat-message-input')?.value,resources:performance.getEntriesByType('resource').filter(e=>/image|upload/.test(e.name)).map(e=>({name:e.name,status:e.responseStatus})),notices:[...document.querySelectorAll('[role=status]')].map(e=>e.textContent)})`);
 console.log(files);await fs.writeFile(path.join(output,'file-diagnosis.json'),JSON.stringify(files,null,2));
 const layout=await page.evaluate(`['body','#chat-container','#chat-workspace','#main-chat-container','#subchat-container','#chat-log','#tools-chat-form-container','#chat-form','#chat-message-input','.chat-composer-actions'].map(selector=>{const e=document.querySelector(selector);if(!e)return{selector};const r=e.getBoundingClientRect(),s=getComputedStyle(e);return{selector,box:{x:r.x,y:r.y,width:r.width,height:r.height},style:Object.fromEntries(['display','position','left','width','minWidth','maxWidth','flex','flexDirection','justifyContent','alignItems','transform','overflow','margin','padding'].map(k=>[k,s[k]])),inline:e.getAttribute('style')}})`);
 console.log(layout);await fs.writeFile(path.join(output,'layout-diagnosis.json'),JSON.stringify(layout,null,2));
 // Whole desktop only, taken by Tlamatini's Shoter agent; never a page-only capture.
 await page.photo('focus-diagnosis');
}finally{page.close();}
