import fs from 'node:fs/promises';
import path from 'node:path';
import {attachBrowser} from './rc_installed_driver.mjs';
const output=process.argv[2];
const page=await attachBrowser(JSON.parse(await fs.readFile(path.join(output,'browser.json'),'utf8')));
try{
 await page.focus();
 const observed=await page.evaluate(`({origin:location.origin,focused:document.hasFocus(),connection:document.querySelector('#connection')?.textContent,application:document.querySelector('#application-state')?.textContent,receipts:[...document.querySelectorAll('.message:not(.user)')].map(e=>({state:e.querySelector('small')?.textContent,answer:e.querySelector('span')?.textContent}))})`);
 await fs.writeFile(path.join(output,'observed-receipts.json'),JSON.stringify(observed,null,2));console.log(observed);
}finally{page.close();}
