// Actual headed playback verification; serves only the finished tutorial folder.
import fs from 'node:fs/promises';
import {createReadStream} from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import assert from 'node:assert/strict';
import {pathToFileURL} from 'node:url';
import {root,launchBrowser,attachBrowser,sleep} from '../integration/rc_installed_driver.mjs';
const folder=path.resolve(process.argv[2]||path.join(root,'artifacts/rc-hosted-video/watch'));
const out=path.resolve(process.argv[3]||path.join(path.dirname(folder),'player-verification'));
const seekIndexes=[0,5,7,8,10,11];
const meta=JSON.parse(await fs.readFile(path.join(folder,'videos.json'),'utf8'));
const allowed=new Set(['Watch.html','verification.json',...Object.values(meta).flatMap(x=>[x.file,x.captions])]);
const types={'.html':'text/html; charset=utf-8','.mp4':'video/mp4','.vtt':'text/vtt; charset=utf-8','.json':'application/json'};
const server=http.createServer(async(req,res)=>{try{
 const name=decodeURIComponent(new URL(req.url,'http://127.0.0.1').pathname.slice(1));if(!allowed.has(name)){res.writeHead(404);res.end();return;}
 const file=path.join(folder,name),stat=await fs.stat(file);let start=0,end=stat.size-1,status=200;
 if(req.headers.range){const m=/^bytes=(\d+)-(\d*)$/.exec(req.headers.range);if(!m){res.writeHead(416);res.end();return;}start=Number(m[1]);end=m[2]?Math.min(Number(m[2]),end):end;if(start>end){res.writeHead(416);res.end();return;}status=206;}
 res.writeHead(status,{'Content-Type':types[path.extname(name)],'Accept-Ranges':'bytes','Content-Length':end-start+1,...(status===206?{'Content-Range':`bytes ${start}-${end}/${stat.size}`}:{})});createReadStream(file,{start,end}).pipe(res);
 }catch(e){res.writeHead(500);res.end();}});
let page;const checks=[];
try{
 await new Promise(resolve=>server.listen(12495,'127.0.0.1',resolve));const browser=await launchBrowser(out);page=await attachBrowser(browser);await page.navigate('http://127.0.0.1:12495/Watch.html');
 for(const language of ['en','es']){
  await page.click('main > button:nth-of-type('+(language==='en'?1:2)+')');
  await page.until('document.documentElement.lang==='+JSON.stringify(language)+'&&document.querySelector("video").readyState>=2','loaded playable tutorial',30000);
  const observed=await page.evaluate('(()=>{const v=document.querySelector("video");return{duration:v.duration,width:v.videoWidth,height:v.videoHeight,error:v.error?.message||null,chapters:document.querySelectorAll("#chapters button").length};})()');
  assert.equal(observed.error,null);assert.equal(observed.width,1600);assert.equal(observed.height,1080);assert.equal(observed.chapters,12);assert.ok(Math.abs(observed.duration-meta[language].duration)<1);
  for(const index of seekIndexes){
   await page.click('#chapters button:nth-child('+(index+1)+')');await page.until('!document.querySelector("video").paused&&document.querySelector("video").currentTime>'+meta[language].chapters[index].start,'chapter playback');await sleep(1200);
   const time=await page.evaluate('document.querySelector("video").currentTime');assert.ok(time>=meta[language].chapters[index].start&&time<meta[language].chapters[index].start+8);await page.photo(language+'-chapter-'+(index+1));
  }
  await page.until('document.querySelector("video").textTracks[0]?.cues?.length>10','localized captions loaded');
  checks.push({language,status:'PASS',...observed,chapterSeeks:seekIndexes.length,captions:true});console.log('PASS '+language+' headed playback, narration track, captions and '+seekIndexes.length+' chapter seeks');
 }
 await page.navigate(pathToFileURL(path.join(folder,'Watch.html')).href);
 await page.until('document.querySelector("video").readyState>=2','offline file player');
 await page.click('#chapters button:nth-child(6)');
 await page.until('!document.querySelector("video").paused&&document.querySelector("video").currentTime>1&&document.querySelector("video").textTracks[0]?.cues?.length>10','offline video and embedded captions');
 await page.photo('offline-player');
 checks.push({mode:'file URL, no local server needed',status:'PASS',captions:true,playback:true});
 console.log('PASS offline double-click player, video and embedded captions');
 await fs.writeFile(path.join(folder,'player-verification.json'),JSON.stringify({checks},null,2));
}finally{if(page){await page.send('Browser.close').catch(()=>{});page.close();}server.closeAllConnections();await new Promise(resolve=>server.close(resolve));}
