// Tlachihuani — Angela López Mendoza · XAIHT.
// Actual non-root, read-only Linux image; network namespace has no host port.
import fs from 'node:fs/promises';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {randomUUID,randomBytes,pbkdf2Sync} from 'node:crypto';
import assert from 'node:assert/strict';
import {root} from './rc_installed_driver.mjs';

const output=path.resolve(process.env.TL_RC_HOSTED_TEST_OUTPUT||path.join(root,'artifacts/rc-hosted-container',String(Date.now())));
const image=process.env.TL_RC_HOSTED_DOCKER_IMAGE||'tlachihuani-rc-cloud:development';
const name='tlachihuani-rc-check-'+randomUUID().replaceAll('-','');
const fixture=path.join(output,'fixture'),state=path.join(output,'state');
await fs.mkdir(fixture,{recursive:true});await fs.mkdir(state,{recursive:true});
const password=randomBytes(32).toString('base64url'),salt=randomBytes(24);
for(const [env,target] of [['TL_RC_HOSTED_CERT','cert.pem'],['TL_RC_HOSTED_PFX','cert.pfx']]){
  assert.ok(process.env[env],env+' required');await fs.copyFile(process.env[env],path.join(fixture,target));
}
await fs.writeFile(path.join(fixture,'config.json'),JSON.stringify({schema:1,security_profile:'development-loopback',origin:'https://127.0.0.1:12493',certificate_path:'/lab/cert.pfx',certificate_password:process.env.TL_RC_HOSTED_PFX_PASSWORD,state_path:'/state/denials.json',users:[{id:randomUUID().replaceAll('-',''),username:'container-fixture',password_salt:salt.toString('base64'),password_hash:pbkdf2Sync(password,salt,210000,32,'sha256').toString('base64')}],computers:[]}));
let owned=false;
async function command(args,{expected=0,env=process.env}={}){
  console.log('Docker check: '+args.slice(0,2).join(' '));
  return await new Promise((resolve,reject)=>{
    const child=spawn('docker',args,{stdio:['ignore','pipe','pipe'],windowsHide:false,env});let output='';
    const consume=bytes=>{const text=bytes.toString();output+=text;process.stdout.write(text);};child.stdout.on('data',consume);child.stderr.on('data',consume);
    child.once('error',reject);child.once('exit',code=>code===expected?resolve(output):reject(Error('Docker command exit '+code+' (expected '+expected+')')));
  });
}
try{
  await command(['run','--rm','--network','none',image],{expected:2});
  await command(['run','--detach','--name',name,'--network','none','--read-only','--cap-drop','ALL','--security-opt','no-new-privileges',
    '--mount','type=bind,source='+fixture+',target=/lab,readonly',
    '--mount','type=bind,source='+state+',target=/state',image,'--development','/lab/config.json']);owned=true;
  const config=JSON.parse(await command(['inspect',name]))[0];
  assert.notEqual(config.Config.User,'0');assert.notEqual(config.Config.User,'');assert.equal(config.HostConfig.ReadonlyRootfs,true);assert.deepEqual(config.HostConfig.PortBindings,{});
  const probe=path.join(root,'tests/Tlachihuani.RC.ContainerChecks/bin/Release/net10.0');
  const result=await command(['run','--rm','--network','container:'+name,'--read-only','--cap-drop','ALL','--security-opt','no-new-privileges',
    '--mount','type=bind,source='+fixture+',target=/lab,readonly','--mount','type=bind,source='+probe+',target=/probe,readonly',
    '--env','TL_RC_CONTAINER_PASSWORD','--entrypoint','dotnet',image,'/probe/Tlachihuani.RC.ContainerChecks.dll'],{env:{...process.env,TL_RC_CONTAINER_PASSWORD:password}});
  const report=JSON.parse(result.trim().split(/\r?\n/).at(-1));
  await fs.writeFile(path.join(output,'results.json'),JSON.stringify({...report,container:{nonRoot:true,readOnly:true,hostPorts:false,network:'none',image}},null,2));
}finally{
  if(owned){try{const logs=await command(['logs',name]);await fs.writeFile(path.join(output,'cloud.log'),logs);}finally{await command(['rm','--force',name]);}}
  await fs.writeFile(path.join(output,'cleanup.json'),JSON.stringify({ownedContainerRemoved:owned},null,2));
}
