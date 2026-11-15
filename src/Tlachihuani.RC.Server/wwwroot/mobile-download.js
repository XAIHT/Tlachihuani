// Tlamatini-RC · Angela López Mendoza · XAIHT
// Shared by Conversation and the full mobile workspace. Never execute received files.
(() => {
  'use strict';
  async function save(response,filename,{language='en',signal,authorized=()=>true}={}){
    const t=(en,es)=>language==='es'?es:en;
    if(!response.ok||response.redirected)throw new Error(t('The file is unavailable. Reconnect or reload the conversation and try again.','El archivo no está disponible. Vuelve a conectarte o carga la conversación e inténtalo de nuevo.'));
    const length=Number(response.headers.get('Content-Length'));
    const expected=response.headers.get('X-Content-SHA256');
    if(!response.headers.has('Content-Length')||!Number.isSafeInteger(length)||length<0||length>64*1024*1024||!/^[a-f0-9]{64}$/i.test(expected||''))
      throw new Error(t('The file could not be verified. Update Tlamatini.','No se pudo verificar el archivo. Actualiza Tlamatini.'));
    const reader=response.body.getReader(),chunks=[];let total=0;
    while(true){const {done,value}=await reader.read();if(done)break;total+=value.length;if(total>length){await reader.cancel();throw new Error(t('The download has an invalid size.','La descarga tiene un tamaño incorrecto.'));}chunks.push(value);}
    if(total!==length)throw new Error(t('The download was incomplete. Try again.','La descarga quedó incompleta. Inténtalo de nuevo.'));
    const blob=new Blob(chunks,{type:response.headers.get('Content-Type')||'application/octet-stream'});
    const digest=[...new Uint8Array(await crypto.subtle.digest('SHA-256',await blob.arrayBuffer()))].map(b=>b.toString(16).padStart(2,'0')).join('');
    if(digest!==expected.toLowerCase())throw new Error(t('File verification failed. Try again.','Falló la verificación del archivo. Inténtalo de nuevo.'));
    if(signal?.aborted||!authorized())
      throw new Error(t('The connection changed. Reconnect before saving.','La conexión cambió. Vuelve a conectarte antes de guardar.'));
    const disposition=response.headers.get('Content-Disposition')||'';
    const encoded=disposition.match(/filename\*=UTF-8''([^;]+)/i),quoted=disposition.match(/filename="([^"\r\n]+)"/i);
    if(encoded){try{filename=decodeURIComponent(encoded[1]);}catch{throw new Error(t('Invalid file name.','Nombre de archivo no válido.'));}}
    else if(quoted)filename=quoted[1];
    let name=String(filename||'download').replace(/[\\/:*?"<>|\x00-\x1f\x7f]/g,'_').replace(/[. ]+$/,'')||'download';
    // Windows/browser limits count UTF-16 units; preserve the extension and
    // never cut an emoji's surrogate pair while shortening a long basename.
    if(name.length>240){
      const dot=name.lastIndexOf('.'),extension=dot>0&&name.length-dot<=32?name.slice(dot):'';
      const stem=extension?name.slice(0,dot):name;let shortened='';
      for(const character of stem){if(shortened.length+character.length>240-extension.length)break;shortened+=character;}
      name=(shortened.replace(/[. ]+$/,'')||'download')+extension;
    }
    const url=URL.createObjectURL(blob),link=document.createElement('a');link.href=url;link.download=name;link.hidden=true;document.body.append(link);link.click();link.remove();
    setTimeout(()=>URL.revokeObjectURL(url),60000);
  }
  window.TlamatiniDownload=Object.freeze({save});
})();
