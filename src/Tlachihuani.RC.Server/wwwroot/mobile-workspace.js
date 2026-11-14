// Tlamatini-RC · Angela López Mendoza · XAIHT
(() => {
  'use strict';
  const mobile = window.TlamatiniMobile;
  if (!mobile || window.self !== window.top) return;
  const es = mobile.edition === 'spanish' || (!mobile.edition && document.documentElement.lang.startsWith('es'));
  document.documentElement.lang = es ? 'es' : 'en';
  const t = (en, spanish) => es ? spanish : en;
  const byId = id => document.getElementById(id);
  const element = (tag, text, className) => {const node=document.createElement(tag); if(text)node.textContent=text;if(className)node.className=className;return node;};
  document.documentElement.classList.add('tlm-mobile');
  const header=element('header',null,'mobile-header');
  const home=element('a','RC','mobile-home'); home.href='/'; home.dataset.rcHome='true';
  const title=element('strong','Tlamatini');
  const subtitle=element('span',es?'Español':'English','mobile-edition');
  const heading=element('div');heading.append(title,subtitle);header.append(home,heading);
  // The transport deliberately rebases app links; this explicit RC control
  // returns to the connection/lifecycle screen outside the application gateway.
  home.addEventListener('click',event=>{event.preventDefault();location.assign('/');});
  document.body.prepend(header);
  window.TlamatiniMobilePicker = async ({file=false,saveName=''}={}) => new Promise(resolve=>{
    const dialog=element('dialog',null,'mobile-file-picker');
    const heading=element('h2',saveName?t('Save on Tlamatini','Guardar en Tlamatini'):t('Files on Tlamatini','Archivos en Tlamatini'));
    const address=element('input');address.type='text';address.setAttribute('aria-label',t('Folder path','Ruta de carpeta'));
    const status=element('p');status.setAttribute('role','status');
    const roots=element('select');roots.setAttribute('aria-label',t('Permitted locations','Ubicaciones permitidas'));
    const actions=element('div',null,'mobile-picker-actions');
    const up=element('button',t('Up','Subir')),go=element('button',t('Open folder','Abrir carpeta'));
    actions.append(up,go);const listing=element('div',null,'mobile-picker-list');
    const name=element('input');name.type='text';name.value=saveName;name.placeholder=t('File name','Nombre del archivo');name.hidden=!saveName;
    const footer=element('footer');const cancel=element('button',t('Cancel','Cancelar'));const choose=element('button',saveName?t('Save here','Guardar aquí'):t('Use this folder','Usar esta carpeta'));choose.hidden=file;footer.append(cancel,choose);
    dialog.append(heading,roots,address,actions,status,listing,name,footer);document.body.append(dialog);
    let current='',parent=null,done=false;
    async function reply(response){
      if(response.redirected||!response.headers.get('Content-Type')?.includes('application/json'))throw new Error(t('Reconnect to Tlamatini and try again.','Vuelve a conectarte a Tlamatini e inténtalo de nuevo.'));
      const body=await response.json();if(!response.ok)throw new Error(body.error?.message||body.error||response.statusText);return body;
    }
    const finish=value=>{if(done)return;done=true;dialog.close();dialog.remove();resolve(value);};
    cancel.addEventListener('click',()=>finish(null));dialog.addEventListener('cancel',event=>{event.preventDefault();finish(null);});
    async function load(path='',offset=0){
      status.textContent=t('Loading…','Cargando…');choose.disabled=true;
      try{
        const result=await fetch('/agent/mobile/files/?'+new URLSearchParams({path,offset:String(offset)}),{credentials:'same-origin'});const body=await reply(result);
        current=body.path;parent=body.parent;address.value=current;up.disabled=!parent;
        if(!offset){listing.replaceChildren();roots.replaceChildren();for(const path of body.roots){const option=element('option',path);option.value=path;roots.append(option);}roots.value=body.roots.find(path=>current.toLowerCase().startsWith(path.toLowerCase()))||body.roots[0];}
        for(const entry of body.entries){if(file&&!entry.directory&&entry.name.toLowerCase()!=='db.sqlite3')continue;const button=element('button',(entry.directory?'▸ ':'')+entry.name);button.type='button';button.disabled=!entry.directory&&!file;button.addEventListener('click',()=>entry.directory?load(entry.path):finish(entry.path));listing.append(button);}
        if(body.next_offset!==null){const more=element('button',t('Load more','Cargar más'));more.addEventListener('click',()=>{more.remove();void load(current,body.next_offset);});listing.append(more);}
        status.textContent=file?t('Select a db.sqlite3 file.','Selecciona un archivo db.sqlite3.'):t('Choose a folder or enter its path.','Elige una carpeta o escribe su ruta.');choose.disabled=false;
      }catch(error){status.textContent=error.message;}
    }
    up.addEventListener('click',()=>load(parent));go.addEventListener('click',()=>load(address.value));roots.addEventListener('change',()=>load(roots.value));
    address.addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();void load(address.value);}});
    choose.addEventListener('click',async()=>{
      if(!saveName){finish(current);return;}
      choose.disabled=true;
      try{const body=new FormData();body.set('directory',current);body.set('name',name.value);const csrf=document.querySelector('[name=csrfmiddlewaretoken]')?.value;if(csrf)body.set('csrfmiddlewaretoken',csrf);const response=await fetch('/agent/mobile/canvas/selection/',{method:'POST',body,credentials:'same-origin'});const selection=await reply(response);
        if(selection.exists&&!confirm(t('Replace the existing file?\n','¿Reemplazar el archivo existente?\n')+selection.path)){choose.disabled=false;return;}finish(selection);
      }catch(error){status.textContent=error.message;choose.disabled=false;}
    });
    dialog.showModal();void load();
  });
  if(byId('transfer-grid')&&byId('rows')){
    document.body.classList.add('mobile-torrenter');
    const grid=byId('transfer-grid'),rows=byId('rows');
    const cards=element('section',null,'mobile-transfer-cards');cards.setAttribute('aria-label',t('Downloads','Descargas'));grid.before(cards);
    const filters=document.querySelector('#workspace .layout>aside');
    if(filters){const sheet=element('details',null,'mobile-transfer-filters');sheet.append(element('summary',t('Search & filters','Búsqueda y filtros')));filters.before(sheet);sheet.append(filters);}
    const commands=document.querySelector('#workspace>nav');
    if(commands){const sheet=element('details',null,'mobile-transfer-commands');sheet.append(element('summary',t('Transfer actions','Acciones de transferencia')));commands.before(sheet);sheet.append(commands);}
    const sortbar=element('div',null,'mobile-transfer-sort');
    const sort=element('select');sort.setAttribute('aria-label',t('Sort downloads','Ordenar descargas'));
    for(const button of grid.querySelectorAll('[data-sort]')){const option=element('option',button.textContent.replace(/[↕↑↓]/g,'').trim());option.value=button.dataset.sort;sort.append(option);}
    const secondary=element('label');const secondaryBox=element('input');secondaryBox.type='checkbox';secondary.append(secondaryBox,document.createTextNode(t('Add sort level','Añadir nivel de orden')));
    const order=element('button',t('Reverse order','Invertir orden'));order.type='button';
    function applySort(){grid.querySelector('[data-sort="'+sort.value+'"]')?.dispatchEvent(new MouseEvent('click',{bubbles:true,shiftKey:secondaryBox.checked}));}
    sort.addEventListener('change',applySort);order.addEventListener('click',applySort);sortbar.append(sort,order,secondary);
    const all=byId('select-page');if(all){const label=element('label');label.append(all,document.createTextNode(t('Select page','Seleccionar página')));sortbar.append(label);}
    cards.before(sortbar);
    let queued=false;
    function renderCards(){
      queued=false;const focused=document.activeElement?.closest('[data-mobile-job]')?.dataset.mobileJob;
      const expanded=new Set([...cards.querySelectorAll('details[open]')].map(node=>node.closest('[data-mobile-job]')?.dataset.mobileJob));
      cards.replaceChildren();
      const headers=new Map([...grid.querySelectorAll('th[data-column]')].map(cell=>[cell.dataset.column,cell.textContent.replace(/[↕↑↓]/g,'').trim()]));
      for(const row of rows.querySelectorAll('tr')){
        if(!row.dataset.jobId){cards.append(element('p',row.textContent.trim(),'mobile-empty-transfers'));continue;}
        const card=element('article',null,'mobile-transfer-card');card.dataset.mobileJob=row.dataset.jobId;
        card.classList.toggle('selected',row.getAttribute('aria-selected')==='true');
        const originalCheck=row.querySelector('input[type=checkbox]'),originalTitle=row.querySelector('button');
        const top=element('div',null,'mobile-transfer-title');
        const box=element('input');box.type='checkbox';box.checked=!!originalCheck?.checked;box.setAttribute('aria-label',originalCheck?.getAttribute('aria-label')||t('Select transfer','Seleccionar transferencia'));
        box.addEventListener('change',()=>{originalCheck?.click();box.checked=!!originalCheck?.checked;});
        const title=element('button',originalTitle?.textContent||row.getAttribute('aria-label'));title.type='button';title.addEventListener('click',()=>originalTitle?.click());top.append(box,title);card.append(top);
        const metrics=element('dl');const more=element('details');more.open=expanded.has(row.dataset.jobId);more.append(element('summary',t('All transfer information','Toda la información')));const other=element('dl');more.append(other);
        for(const cell of row.querySelectorAll('td[data-column]')){
          const column=Number(cell.dataset.column);if(column<2||cell.hidden||getComputedStyle(cell).display==='none')continue;
          const term=element('dt',headers.get(String(column))||String(column));const value=element('dd',cell.textContent.trim());
          const meter=cell.querySelector('progress');if(meter)value.prepend(meter.cloneNode(true));
          (new Set([2,3,4,6,7]).has(column)?metrics:other).append(term,value);
        }
        card.append(metrics,more);cards.append(card);
        if(focused===row.dataset.jobId)title.focus({preventScroll:true});
      }
    }
    const update=()=>{if(!queued){queued=true;queueMicrotask(renderCards);}};
    new MutationObserver(update).observe(grid,{subtree:true,childList:true,attributes:true,attributeFilter:['hidden','style','aria-selected','data-column']});
    update();
  }
  const navbar=byId('navbar-container');
  if (!navbar) {
    document.body.classList.add('mobile-secondary');
    const back=element('button',t('Back to chat','Volver al chat'));back.type='button';back.addEventListener('click',()=>location.assign(mobile.prefix+'/agent/agent/'));header.append(back);
    return;
  }
  document.body.classList.add('mobile-chat-app');
  const avatar=byId('tlm-avatar-dock');
  if(avatar){const status=element('button',t('Status','Estado'),'mobile-status');status.type='button';status.setAttribute('aria-label',t('Hear Tlamatini’s status','Escuchar el estado de Tlamatini'));status.addEventListener('click',()=>avatar.click());header.append(status);}
  // Phone photo selection uses the same checked upload/chip path as desktop
  // paste and drop. It does not require a keyboard or host clipboard access.
  const composer=document.querySelector('.chat-composer-actions');
  if(composer&&window.TlamatiniFileDrop){
    const attach=element('button',t('Photo','Foto'),'mobile-attach');attach.type='button';attach.id='mobile-attach-image';attach.setAttribute('aria-label',t('Attach an image from this device','Adjuntar una imagen de este dispositivo'));
    const input=element('input');input.type='file';input.accept='image/*';input.multiple=true;input.hidden=true;input.id='mobile-image-input';
    attach.addEventListener('click',()=>input.click());
    input.addEventListener('change',async()=>{const files=[...input.files];input.value='';if(files.length)await window.TlamatiniFileDrop.dropEntries(files.map(file=>({file,isDirectory:false})));});
    composer.prepend(attach,input);
    byId('chat-image-chips')?.addEventListener('click',event=>{const chip=event.target.closest('.chat-img-chip');if(chip&&!event.target.closest('button'))chip.dispatchEvent(new MouseEvent('dblclick',{bubbles:true}));});
  }
  const excluded=new Set(['agentic-control-panel','prompt-flow-panel','open-admin']);
  for(const id of excluded)byId(id)?.closest('li')?.remove();
  const sourceItems=[...navbar.querySelectorAll('#menu-editor a, #menu-editor button')].filter(node=>!node.classList.contains('dropdown-toggle')&&!node.classList.contains('navbar-toggler')&&node.id!=='contextMobile');
  let generated=0;
  for(const source of sourceItems)if(!source.id)source.id='mobile-original-'+(++generated);
  const panels=element('main',null,'mobile-actions');panels.hidden=true;panels.id='mobile-actions';
  const search=element('input');search.type='search';search.placeholder=t('Find a setting or action…','Buscar ajuste o acción…');search.setAttribute('aria-label',search.placeholder);panels.append(search);
  const groups=element('div');panels.append(groups);document.body.append(panels);
  const actions=[];
  const groupLabels=new Map();
  const normalize=node=>node.textContent.replace(/✓/g,'').replace(/\s+/g,' ').trim();
  for(const source of sourceItems){
    const list=source.closest('.dropdown-menu');
    const group=list?normalize(byId(list.getAttribute('aria-labelledby'))||list.previousElementSibling):t('Files & session','Archivos y sesión');
    groupLabels.set(source.id,group);
    actions.push({source,group,label:normalize(source)});
  }
  const extra=byId('open-in-menu-list');
  const context=element('section',null,'mobile-context');
  context.append(element('strong',t('Current context','Contexto actual')));
  const contextValue=element('p');context.append(contextValue);
  panels.prepend(context);
  const syncContext=()=>{contextValue.textContent=byId('contextData')?.textContent.trim()||t('No context selected','Sin contexto seleccionado');};syncContext();
  if(byId('contextData'))new MutationObserver(syncContext).observe(byId('contextData'),{childList:true,subtree:true,characterData:true});
  function invoke(source){
    if(source.disabled||source.getAttribute('aria-disabled')==='true')return;
    if(source.id==='views-canvas-toggle'){show('canvas');return;}
    if(source.id==='views-terminals-toggle'){source.click();show('canvas');return;}
    if(source.id==='views-reset-layout'){source.click();show('chat');return;}
    if(['open-button','save-as-button','set-file-context'].includes(source.id)){show('canvas');source.click();return;}
    if(source.id==='view-context-dir-in-canvas'){source.click();show('canvas');return;}
    const href=source.getAttribute('href');
    if(href&&href!=='#'&&!source.hasAttribute('onclick')){location.assign(mobile.route(href));return;}
    source.click();
  }
  function render(category='all'){
    groups.replaceChildren();
    const query=search.value.toLocaleLowerCase();
    const config=new Set(['config-menu-button','skills-menu-button','external-menu-button','db-menu-button']);
    const entries=[...actions];
    if(extra)for(const source of extra.querySelectorAll('a,button'))entries.push({source,group:t('Open context in','Abrir contexto en'),label:normalize(source)});
    const sections=new Map();
    for(const entry of entries){
      const menu=entry.source.closest('.dropdown-menu')?.getAttribute('aria-labelledby');
      if(category==='settings'&&!config.has(menu))continue;
      if(category==='downloads'&&!entry.label.toLowerCase().includes('torrenter'))continue;
      if(query&&!(entry.label+' '+entry.group).toLocaleLowerCase().includes(query))continue;
      let section=sections.get(entry.group);
      if(!section){section=element('section',null,'mobile-action-group');section.append(element('h2',entry.group));sections.set(entry.group,section);groups.append(section);}
      const button=element('button',entry.label,'mobile-action');button.type='button';button.dataset.source=entry.source.id;
      button.disabled=!!entry.source.disabled||entry.source.getAttribute('aria-disabled')==='true';
      button.title=entry.source.title;button.addEventListener('click',()=>invoke(entry.source));section.append(button);
      if(button.disabled&&entry.source.getAttribute('aria-describedby'))section.append(element('p',byId(entry.source.getAttribute('aria-describedby'))?.textContent||'', 'mobile-action-help'));
    }
    if(!sections.size)groups.append(element('p',t('No matching actions.','No hay acciones coincidentes.')));
  }
  let current='chat';
  function show(view){
    current=view;document.body.dataset.mobileView=view;
    panels.hidden=view==='chat'||view==='canvas';
    if(!panels.hidden)render(view==='more'?'all':view);
    for(const button of nav.querySelectorAll('button'))button.setAttribute('aria-current',button.dataset.view===view?'page':'false');
    if(view==='canvas'&&byId('views-canvas-toggle')?.getAttribute('aria-checked')==='false')byId('views-canvas-toggle').click();
    dispatchEvent(new Event('resize'));
  }
  const nav=element('nav',null,'mobile-tabbar');nav.setAttribute('aria-label',t('Main navigation','Navegación principal'));
  for(const [view,label]of[['chat',t('Chat','Chat')],['canvas',t('Canvas','Lienzo')],['downloads',t('Downloads','Descargas')],['settings',t('Settings','Ajustes')],['more',t('More','Más')]]){
    const button=element('button',label);button.type='button';button.dataset.view=view;button.addEventListener('click',()=>show(view));nav.append(button);
  }
  document.body.append(nav);search.addEventListener('input',()=>render(current==='more'?'all':current));
  // Generated-file links keep the application's checked canvas loader. Make
  // its destination visible in the phone navigation as it is on desktop.
  byId('chat-log')?.addEventListener('click',event=>{
    if(event.target.closest('.chat-load-canvas-link'))show('canvas');
  });
  const modes=byId('tools-div');
  if(modes){const details=element('details',null,'mobile-mode-sheet');const summary=element('summary',t('Modes & prompt tools','Modos y herramientas'));details.append(summary);modes.before(details);details.append(modes);}
  // Keep original listeners and live enablement; only the navigation changes.
  new MutationObserver(()=>{if(!panels.hidden)render(current==='more'?'all':current);}).observe(navbar,{subtree:true,attributes:true,attributeFilter:['disabled','aria-disabled']});
  const report=()=>({included:actions.map(a=>({id:a.source.id,label:a.label,group:a.group})),contextActions:[...extra?.querySelectorAll('a,button')||[]].map(source=>({id:source.id,label:normalize(source)})),excluded:[...excluded],view:current});
  Object.defineProperty(window,'TlamatiniMobileInventory',{value:report});
  show('chat');
})();
