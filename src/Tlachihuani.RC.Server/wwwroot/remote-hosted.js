// Tlamatini-RC · Angela López Mendoza · XAIHT
import {transport} from './remote-transport.js';
import {t,language,applyLanguage} from './remote-i18n.js';
const $=selector=>document.querySelector(selector);
let computers=[],loading=false,selecting=false,revision=0;
function status(value){$('#hosted-status').textContent=t(value);}
function node(tag,text,className){const item=document.createElement(tag);if(text!==undefined)item.textContent=text;if(className)item.className=className;return item;}
export function createHostedUI({select,disconnect}){
  const render=()=>{
    if(!transport.hosted)return;
    const user=transport.bootstrap.user,oidc=transport.bootstrap.authentication_mode==='oidc';
    $('#hosted-login-form').hidden=!!user||oidc;$('#hosted-oidc').hidden=!!user||!oidc;$('#hosted-directory').hidden=!user;
    $('#hosted-banner-description').textContent=t(oidc?'This development connection uses your configured identity provider and loopback test connections. The relay can read test traffic. Internet access, end-to-end encryption and post-quantum protection are unavailable.':'This isolated lab uses local test accounts and loopback connections. The relay can read this test traffic. It does not provide end-to-end or post-quantum protection, or Internet access.');
    $('#hosted-signout').hidden=!user;$('#hosted-refresh').disabled=loading||selecting;
    $('#hosted-login').disabled=loading;$('#hosted-login').textContent=t(loading&&!user?'Signing in…':'Sign in');
    $('#hosted-user').textContent=user?user.username:'';
    $('#hosted-selected').textContent=transport.selected?transport.selected.name+' · '+transport.selected.display_name+' · '+(transport.selected.edition==='spanish'?'Español':'English'):'';
    const list=$('#hosted-computers');list.replaceChildren();
    if(user&&!computers.length&&!loading)list.append(node('p',t('No computers are linked to this lab account.'),'muted'));
    for(const computer of computers){
      const card=node('article');const heading=node('h2',computer.name);const state=node('span',t(computer.online?'Online':'Computer unavailable'),'badge'+(computer.online?' connected':''));
      const top=node('div',undefined,'actions');top.append(heading,state);card.append(top);
      if(!computer.online){card.append(node('p',t('Connect Remote-Controller on this computer before trying again.'),'muted'));if(computer.last_seen){const time=new Date(computer.last_seen);if(!Number.isNaN(time.valueOf()))card.append(node('small',(document.documentElement.lang==='es'?'Última conexión: ':'Last seen: ')+time.toLocaleString()));}}
      if(!computer.accounts.length)card.append(node('p',t('No enabled Tlamatini accounts are available on this computer.'),'muted'));
      for(const account of computer.accounts){
        const row=node('div',undefined,'hosted-account');const detail=node('div');detail.append(node('strong',account.display_name),node('small',account.edition==='spanish'?'Español':'English'));
        const button=node('button',t('Open account'));button.type='button';button.dataset.hostedOpen=computer.installation_id;button.dataset.binding=account.binding_id;button.disabled=!computer.online||loading||selecting;
        button.addEventListener('click',async()=>{if(selecting)return;const version=revision;selecting=true;status('Opening the selected account…');render();try{await select(computer,account);if(version===revision)status('');}catch(error){if(version===revision)status(error.message);}finally{selecting=false;render();}});
        row.append(detail,button);card.append(row);
      }
      list.append(card);
    }
    applyLanguage();
  };
  const refresh=async()=>{if(!transport.hosted||loading||selecting)return;const version=++revision,previousUserId=transport.bootstrap.user?.id??null;loading=true;render();try{await transport.refreshIdentity();if(version!==revision)return;const currentUserId=transport.bootstrap.user?.id??null;if(previousUserId!==currentUserId){computers=[];disconnect();}if(!transport.bootstrap.user){computers=[];disconnect();status('');return;}const result=await transport.computers();if(version!==revision)return;computers=result;status('');}catch(error){if(version===revision)status(error.message);}finally{if(version===revision){loading=false;render();}}};
  $('#hosted-oidc-login').addEventListener('click',()=>{if(transport.bootstrap?.authentication_mode!=='oidc'||transport.bootstrap.login_url!=='/auth/login')return;location.assign('/auth/login?language='+language());});
  $('#hosted-login-form').addEventListener('submit',async event=>{event.preventDefault();if(loading)return;loading=true;render();status('');try{await transport.login($('#hosted-username').value.trim(),$('#hosted-password').value);computers=await transport.computers();status('Choose a computer and account to continue.');}catch(error){status(error.message);}finally{$('#hosted-password').value='';loading=false;render();}});
  $('#hosted-signout').addEventListener('click',async()=>{if(loading)return;loading=true;revision++;disconnect();render();try{await transport.logout();computers=[];status('');location.hash='computers';}catch(error){status(error.message);}finally{loading=false;render();}});
  $('#hosted-refresh').addEventListener('click',refresh);
  document.addEventListener('rc-language-change',render);
  return {
    async activate(){
      $('#hosted-banner').hidden=false;$('#hosted-navigation').hidden=false;$('#open-workspace').hidden=true;
      $('#workspace-help').textContent=t('Files from replies can be saved here. Full workspace and voice are unavailable in this connection profile.');
      $('#security-profile').textContent=t('Development lab · not public remote access');
      $('#security-explanation').textContent=t('This development connection is restricted to the lab. The relay can read test traffic. Internet access, end-to-end encryption and post-quantum protection are unavailable.');
      $('#chat-connect-link').href='#computers';location.hash='computers';render();if(transport.bootstrap.user)await refresh();
      const returned=new URL(location.href),error=returned.searchParams.get('auth_error');
      if(error){returned.searchParams.delete('auth_error');history.replaceState(null,'',returned.pathname+returned.search+returned.hash);status(error==='identity_not_linked'?'This identity is not linked to a permitted XAIHT account. Contact the account owner.':'Sign-in did not complete. Try again.');}
    },render,refresh
  };
}
