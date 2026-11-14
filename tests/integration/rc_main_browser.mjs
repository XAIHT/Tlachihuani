// Tlachihuani / Tlamatini — Created for Angela López Mendoza · XAIHT
// Run only from the verified visible development console. Chrome stays open.
// Real account pages, service, adapters, consumer, history and approval broker;
// the isolated fixtures replace model inference and explicitly suppress host effects.
import {spawn} from 'node:child_process';
import fs from 'node:fs/promises';
import {existsSync} from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomUUID} from 'node:crypto';
import assert from 'node:assert/strict';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const workspace = path.resolve(root, '../..');
const artifacts = path.join(root, 'artifacts');
const runId = 'main-browser-' + Date.now();
const output = path.join(artifacts, 'rc-implementation', runId);
const image = process.env.TL_RC_TEST_IMAGE && path.resolve(process.env.TL_RC_TEST_IMAGE);
const python = process.env.TL_RC_TEST_PYTHON || path.join(workspace, 'Tlamatini', 'python', 'python.exe');
const chrome = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const nonce = randomUUID().replaceAll('-', '');
const base = 'http://127.0.0.1:12487';
const forbiddenHistory = 'OTHER_ACCOUNT_QA_SENTINEL';
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const checks = [], evidence = [], fixtures = [], tabs = [], foregroundChecks = [];
let host, failures = 0;

function record(label, condition) {
  assert.ok(condition, label);
  checks.push({label, passed: true});
  console.log('PASS MAIN BROWSER ' + label);
}

function processDone(child) {
  return new Promise(resolve => {
    child.once('exit', (code, signal) => resolve({code, signal}));
    child.once('error', error => resolve({error: error.message}));
  });
}

async function start(executable, args, options = {}) {
  const startedAt = Date.now();
  const child = spawn(executable, args, {cwd: root, stdio: 'inherit', windowsHide: false, ...options});
  child.startedAt = startedAt;
  child.done = processDone(child);
  await new Promise((resolve, reject) => {
    child.once('spawn', resolve);
    child.once('error', reject);
  });
  return child;
}

async function boundedExit(child, label, milliseconds = 25000) {
  let timer;
  const result = await Promise.race([
    child.done,
    new Promise(resolve => { timer = setTimeout(() => resolve({timeout: true}), milliseconds); }),
  ]);
  clearTimeout(timer);
  if (result.timeout || result.error || result.code !== 0) {
    throw new Error(label + ' did not stop cleanly: ' + JSON.stringify(result));
  }
}

async function native(body) {
  return new Promise((resolve, reject) => {
    const child = spawn(image, ['--dev-control', nonce, String(host.pid)], {
      cwd: root, stdio: ['pipe', 'pipe', 'inherit'], windowsHide: false,
    });
    let response = '', settled = false;
    const finish = (error, result) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      error ? reject(error) : resolve(result);
    };
    const timer = setTimeout(() => {
      // The handle belongs to this single helper, never a looked-up PID.
      child.kill();
      finish(new Error('QA native helper timed out: ' + body.op));
    }, 15000);
    child.stdout.on('data', chunk => {
      response += chunk;
      if (Buffer.byteLength(response) > 65536) {
        child.kill();
        finish(new Error('QA native response exceeded its limit'));
      }
    });
    child.once('error', error => finish(error));
    child.once('exit', code => {
      try {
        const result = JSON.parse(response);
        if (code || result.error) throw new Error(result.error?.message || 'QA native control failed');
        finish(null, result);
      } catch (error) { finish(error); }
    });
    child.stdin.on('error', error => finish(error));
    child.stdin.end(JSON.stringify(body) + '\n');
  });
}

async function poll(test, label, timeout = 30000) {
  const deadline = Date.now() + timeout;
  let last;
  while (Date.now() < deadline) {
    try { const value = await test(); if (value) return value; }
    catch (error) { last = error; }
    await sleep(150);
  }
  throw new Error('Timed out: ' + label + (last ? '; ' + last.message : ''));
}

async function connect(url, fixture) {
  const ws = new WebSocket(url);
  await new Promise((resolve, reject) => {
    ws.addEventListener('open', resolve, {once: true});
    ws.addEventListener('error', reject, {once: true});
  });
  const pending = new Map(), responses = new Map(), reading = new Set();
  const tab = {fixture, errors: [], responseAudit: {read: 0, leaked: false, cancelledResults: 0, unreadable: []}, close: () => ws.close()};
  let next = 0;
  tab.send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++next;
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error('CDP timed out: ' + method));
    }, 12000);
    pending.set(id, {resolve, reject, timer});
    ws.send(JSON.stringify({id, method, params}));
  });
  tab.flushAudit = async () => { await Promise.allSettled([...reading]); };
  ws.addEventListener('close', () => {
    for (const item of pending.values()) {
      clearTimeout(item.timer);
      item.reject(new Error('CDP connection closed'));
    }
    pending.clear();
  });
  ws.onmessage = ({data}) => {
    const event = JSON.parse(data);
    if (event.id && pending.has(event.id)) {
      const item = pending.get(event.id);
      pending.delete(event.id);
      clearTimeout(item.timer);
      event.error ? item.reject(new Error(event.error.message)) : item.resolve(event.result);
    }
    if (event.method === 'Runtime.exceptionThrown') tab.errors.push(event.params.exceptionDetails);
    if (event.method === 'Network.responseReceived' && event.params.response.url.startsWith(base + '/api/rc/v1/')) {
      responses.set(event.params.requestId, new URL(event.params.response.url).pathname);
    }
    if (event.method === 'Network.loadingFailed') responses.delete(event.params.requestId);
    if (event.method === 'Network.loadingFinished' && responses.has(event.params.requestId)) {
      const requestId = event.params.requestId, endpoint = responses.get(requestId);
      responses.delete(requestId);
      const task = tab.send('Network.getResponseBody', {requestId}).then(result => {
        const body = result.base64Encoded ? Buffer.from(result.body, 'base64').toString('utf8') : result.body;
        tab.responseAudit.read++;
        tab.responseAudit.leaked ||= body.includes(forbiddenHistory);
        if (endpoint.startsWith('/api/rc/v1/operations')) {
          try {
            const receipt = JSON.parse(body);
            if (receipt.kind === 'conversation.submit' && receipt.state === 'succeeded' && receipt.result?.cancelled === true) tab.responseAudit.cancelledResults++;
          } catch { /* Content validation belongs to the real client. */ }
        }
        // Never retain bearer tokens, invitation values or response content in evidence.
      }).catch(error => tab.responseAudit.unreadable.push({endpoint, error: error.message}));
      reading.add(task);
      void task.finally(() => reading.delete(task));
    }
  };
  tabs.push(tab);
  await tab.send('Page.enable');
  await tab.send('Runtime.enable');
  await tab.send('Network.enable');
  return tab;
}

async function evaluate(tab, expression) {
  const result = await tab.send('Runtime.evaluate', {expression, returnByValue: true, awaitPromise: true});
  if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text);
  return result.result.value;
}

const until = (tab, expression, label, timeout) => poll(() => evaluate(tab, expression), label, timeout);

async function nativeForeground(tab, restore, phase) {
  const fixture = tab.fixture;
  assert.ok(fixture?.browser && fixture.browser.exitCode === null, 'The owned headed Chrome process must still be alive');
  const args = ['-B', path.join(root, 'tests', 'integration', 'rc_browser_focus.py'),
    '--pid', String(fixture.browser.pid), '--started-after-ms', String(fixture.browser.startedAt), '--visible-development'];
  if (fixture.browserCreationTime) args.push('--creation-time', fixture.browserCreationTime);
  if (tab.browserWindow) args.push('--window', tab.browserWindow);
  const documentTitle = await evaluate(tab, 'document.title');
  if (documentTitle) args.push('--title', documentTitle);
  if (!restore) args.push('--verify-only');
  const child = await start(python, args, {stdio: ['ignore', 'pipe', 'inherit']});
  let response = '';
  child.stdout.on('data', chunk => { response += chunk; });
  try { await boundedExit(child, 'Owned Chrome foreground verification', 15000); }
  catch (error) { throw new Error(error.message + '; ' + response.trim()); }
  const result = JSON.parse(response);
  assert.ok(result.foreground && result.visible && result.pid === fixture.browser.pid,
    'Windows must verify the intended owned Chrome window is actually visible and foreground');
  fixture.browserCreationTime ||= result.creation_time;
  assert.equal(result.creation_time, fixture.browserCreationTime, 'Chrome process identity must remain pinned');
  tab.browserWindow ||= result.window;
  assert.equal(result.window, tab.browserWindow, 'The actual Chrome window for this target must remain pinned');
  const proof = {edition: fixture.edition, phase, ...result};
  foregroundChecks.push(proof);
  tab.lastForeground = proof;
  return proof;
}

async function verifyForeground(tab, phase) {
  const proof = await nativeForeground(tab, false, phase);
  assert.ok(await evaluate(tab, `document.visibilityState==='visible'&&document.hasFocus()`),
    'The intended real Chrome tab must be visible and focused before ' + phase);
  return proof;
}

async function front(tab, phase = 'browser interaction') {
  // Page.bringToFront selects the tab but Windows may retain another Chrome
  // process in foreground. Restore only this owned process, then verify both
  // its actual HWND and the selected document. No focus emulation is used.
  await tab.send('Page.bringToFront');
  await nativeForeground(tab, true, phase);
  await tab.send('Page.bringToFront');
  await until(tab, `document.visibilityState==='visible'&&document.hasFocus()`, 'real document focus', 5000);
  return verifyForeground(tab, phase);
}

async function click(tab, selector) {
  await front(tab);
  await evaluate(tab, `document.querySelector(${JSON.stringify(selector)}).scrollIntoView({block:'center',behavior:'instant'})`);
  await sleep(100);
  const point = await evaluate(tab, `(()=>{const e=document.querySelector(${JSON.stringify(selector)}),r=e.getBoundingClientRect(),x=r.x+r.width/2,y=r.y+r.height/2;if(e.disabled)throw new Error('Disabled control');if(!e.contains(document.elementFromPoint(x,y)))throw new Error('Obstructed control');return{x,y};})()`);
  await verifyForeground(tab, 'click ' + selector);
  await tab.send('Input.dispatchMouseEvent', {type: 'mousePressed', button: 'left', clickCount: 1, ...point});
  await tab.send('Input.dispatchMouseEvent', {type: 'mouseReleased', button: 'left', clickCount: 1, ...point});
  await sleep(150);
}

async function field(tab, selector, value) {
  await click(tab, selector);
  await verifyForeground(tab, 'enter field ' + selector);
  await evaluate(tab, `(()=>{const e=document.querySelector(${JSON.stringify(selector)});e.value=${JSON.stringify(value)};e.dispatchEvent(new Event('input',{bubbles:true}));})()`);
}

async function route(tab, section) {
  await front(tab);
  await evaluate(tab, `location.hash=${JSON.stringify(section)}`);
  await sleep(250);
}

async function navigate(tab, url, readySelector) {
  await front(tab);
  const marker = randomUUID();
  await evaluate(tab, `globalThis.__qaNavigation=${JSON.stringify(marker)}`);
  await tab.send('Page.navigate', {url});
  await until(tab, `globalThis.__qaNavigation!==${JSON.stringify(marker)}&&location.href===${JSON.stringify(url)}&&document.readyState==='complete'&&!!document.querySelector(${JSON.stringify(readySelector)})`, url);
}

async function viewport(tab, width, height) {
  await tab.send('Emulation.setDeviceMetricsOverride', {
    width, height, deviceScaleFactor: 1, mobile: width < 760, screenWidth: width, screenHeight: height,
  });
  await sleep(250);
}

async function capture(tab, label, focusSelector = null) {
  await front(tab, 'capture ' + label);
  await evaluate(tab, focusSelector
    ? `document.querySelector(${JSON.stringify(focusSelector)}).scrollIntoView({block:'start',behavior:'instant'})`
    : 'scrollTo(0,0)');
  await sleep(700);
  await verifyForeground(tab, 'page capture ' + label);
  const result = await tab.send('Page.captureScreenshot', {format: 'png', captureBeyondViewport: false});
  const pageFile = path.join(output, label + '-page.png');
  await fs.writeFile(pageFile, Buffer.from(result.data, 'base64'));
  const desktopLabel = runId + '-' + label;
  await verifyForeground(tab, 'desktop capture ' + label);
  const shot = await start(python, [path.join(artifacts, 'rc-implementation', 'capture.py'), desktopLabel]);
  await boundedExit(shot, 'Shoter evidence', 30000);
  const foreground = await verifyForeground(tab, 'completed desktop capture ' + label);
  const desktopFile = path.join(output, label + '-desktop.png');
  await fs.copyFile(path.join(artifacts, 'rc-implementation', 'evidence', desktopLabel + '.png'), desktopFile);
  evidence.push({label, page: pageFile, desktop: desktopFile, foreground});
}

async function status(fixture) {
  const response = await fetch(fixture.ready.origin + '/qa/status');
  assert.equal(response.status, 200, 'QA status must be available');
  const result = await response.json();
  assert.ok(!JSON.stringify(result).includes(forbiddenHistory), 'QA account status must not expose the other account');
  assert.equal(result.other_account_history_count, 1, 'Other account history must remain unchanged');
  return result;
}

async function launchFixture(edition, port) {
  const editionRoot = path.join(workspace, edition === 'english' ? 'Tlamatini' : 'Tlamatini-Spanish');
  const directory = path.join(output, edition);
  const child = await start(python, [
    path.join(root, 'tests', 'integration', 'rc_main_fixture.py'),
    '--edition-root', editionRoot, '--port', String(port), '--output', path.join(directory, 'fixture'),
    '--rc-image', image, '--dev-nonce', nonce, '--dev-pid', String(host.pid), '--visible-development',
  ], {env: {...process.env, PYTHONDONTWRITEBYTECODE: '1'}});
  const fixture = {edition, child, directory};
  fixtures.push(fixture);
  fixture.ready = await poll(async () => {
    if (child.exitCode !== null) throw new Error(edition + ' fixture exited: ' + child.exitCode);
    return JSON.parse(await fs.readFile(path.join(directory, 'fixture', 'ready.json'), 'utf8'));
  }, edition + ' fixture startup', 60000);
  assert.equal(fixture.ready.edition, edition);
  assert.equal(fixture.ready.pid, child.pid);

  const profile = path.join(directory, 'chrome-profile');
  const scratch = path.join(directory, 'scratch');
  await fs.mkdir(path.join(profile, 'Default'), {recursive: true});
  await fs.mkdir(scratch, {recursive: true});
  // QA browsers must not display a password-save bubble over the actual controls.
  await fs.writeFile(path.join(profile, 'Default', 'Preferences'), JSON.stringify({
    credentials_enable_service: false,
    credentials_enable_autosignin: false,
    profile: {password_manager_enabled: false},
  }));
  fixture.browser = await start(chrome, [
    '--user-data-dir=' + profile, '--remote-debugging-port=0', '--remote-debugging-address=127.0.0.1',
    '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
    '--disable-sync', '--disable-extensions', '--window-size=1100,1000', '--new-window', 'about:blank',
  ], {stdio: 'ignore', env: {...process.env, TEMP: scratch, TMP: scratch, TMPDIR: scratch}});
  fixture.browser.unref();
  const debugPort = await poll(async () => Number((await fs.readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0]), edition + ' visible Chrome');
  fixture.debugOrigin = 'http://127.0.0.1:' + debugPort;
  const first = await poll(async () => {
    const available = await (await fetch(fixture.debugOrigin + '/json/list')).json();
    return available.find(tab => tab.type === 'page');
  }, edition + ' Chrome page');
  fixture.main = await connect(first.webSocketDebuggerUrl, fixture);
  await navigate(fixture.main, fixture.ready.account_url, '#account-state');
  await capture(fixture.main, edition + '-account-before-enable');
  return fixture;
}

async function enableAccount(fixture) {
  const {edition, main, ready} = fixture;
  record(edition + ': real account page has the isolated authenticated identity',
    await evaluate(main, `document.querySelector('.account-name').textContent.trim()===${JSON.stringify(ready.username)}`));
  record(edition + ': actual RC service is ready', await evaluate(main, `document.querySelector('#service-state').classList.contains('active')`));
  record(edition + ': enrollment initially disabled', !await evaluate(main, `document.querySelector('#account-state').classList.contains('active')`));
  record(edition + ': enrollment renders a real CSRF token', await evaluate(main, `document.querySelector('input[name="csrfmiddlewaretoken"]').value.length>=32`));
  await viewport(main, 393, 852);
  record(edition + ': phone enrollment has no horizontal overflow', await evaluate(main, 'document.documentElement.scrollWidth<=innerWidth+1'));
  await capture(main, edition + '-account-phone');
  await viewport(main, 1100, 900);

  await field(main, '#current-password', 'QA-deliberately-wrong-password');
  await click(main, 'form:has(input[value="enable"]) button[type="submit"]');
  await until(main, `!!document.querySelector('.notice.error[role="alert"]')`, edition + ' rejects wrong password');
  record(edition + ': wrong password cannot create or enable a binding', !(await status(fixture)).profile?.enabled);
  await field(main, '#current-password', ready.password);
  await click(main, 'form:has(input[value="enable"]) button[type="submit"]');
  await until(main, `document.querySelector('#account-state')?.classList.contains('active')`, edition + ' account enabled');
  fixture.binding = (await status(fixture)).profile;
  const bindings = (await native({op: 'accounts'})).accounts;
  const binding = bindings.find(item => item.id === fixture.binding.binding_id);
  record(edition + ': password enable creates a real native account binding', !!binding);
  record(edition + ': immutable account and adapter identities reach the service',
    binding.account_id === fixture.binding.account_id && binding.adapter_id === fixture.binding.adapter_id && binding.edition === edition);
  await navigate(main, ready.account_url, '#account-state');
  record(edition + ': account identity survives page reload', JSON.stringify((await status(fixture)).profile) === JSON.stringify(fixture.binding));
  await capture(main, edition + '-account-enabled');
  await poll(async () => (await native({op: 'adapter-status', binding_id: fixture.binding.binding_id})).ready,
    edition + ' actual account worker heartbeat');
  record(edition + ': actual AccountWorker reports readiness', true);
}

async function enrollClient(fixture) {
  const {edition, debugOrigin, main, binding} = fixture;
  const target = await main.send('Target.createTarget', {url: 'about:blank'});
  const page = await poll(async () => (await (await fetch(debugOrigin + '/json/list')).json()).find(tab => tab.id === target.targetId), edition + ' RC tab');
  const tab = fixture.remote = await connect(page.webSocketDebuggerUrl, fixture);
  await navigate(tab, base + '/remote.html', '#prompt');
  record(edition + ': disconnected client cannot send', await evaluate(tab, 'document.querySelector("#send").disabled'));
  await route(tab, 'security');
  await until(tab, `document.querySelector('#security-profile').textContent.includes('Local-only')`, edition + ' security profile');
  record(edition + ': invitation field does not trigger password enrollment', await evaluate(tab, `document.querySelector('#invitation').type==='text'&&document.querySelector('#invitation').autocomplete==='one-time-code'`));
  await capture(tab, edition + '-client-enrollment');
  await viewport(tab, 393, 852);
  record(edition + ': phone device enrollment has no horizontal overflow', await evaluate(tab, 'document.documentElement.scrollWidth<=innerWidth+1'));
  await capture(tab, edition + '-client-enrollment-phone');
  await viewport(tab, 1100, 900);
  const invitation = await native({op: 'invite', binding_id: binding.binding_id, scopes: ['monitor', 'chat']});
  await field(tab, '#device-name', 'Main integration QA ' + edition);
  await field(tab, '#invitation', invitation.invitation);
  await click(tab, '#enroll');
  await until(tab, `document.querySelector('#fingerprint').textContent.includes('Compare in XT:')`, edition + ' pending device approval');
  record(edition + ': device approval is required before connection', await evaluate(tab, `document.querySelector('#connection').textContent==='Disconnected'`));
  const pending = await poll(async () => (await native({op: 'pending-enrollments'})).enrollments.find(item => item.binding_id === binding.binding_id), edition + ' native pending enrollment');
  record(edition + ': browser fingerprint matches native approval', await evaluate(tab, `document.querySelector('#fingerprint').textContent.includes(${JSON.stringify(pending.fingerprint)})`));
  await native({op: 'approve', enrollment_id: pending.id, fingerprint: pending.fingerprint});
  await until(tab, `document.querySelector('#connection').textContent==='Connected'`, edition + ' WebCrypto connection');
  record(edition + ': approved browser displays its actual edition', await evaluate(tab, `document.querySelector('#account-line').textContent.includes(${JSON.stringify(edition)})`));
  record(edition + ': bearer session is absent from local and session storage', await evaluate(tab, `![...Object.values(localStorage),...Object.values(sessionStorage)].some(value=>/access_token|Bearer/.test(value))`));
  record(edition + ': browser private key is not exportable', await evaluate(tab, `new Promise((resolve,reject)=>{const r=indexedDB.open('tlachihuani-rc-device-v1',1);r.onsuccess=()=>{const db=r.result,q=db.transaction('identity').objectStore('identity').get(location.origin);q.onsuccess=()=>{resolve(q.result?.key.extractable===false);db.close()};q.onerror=reject};r.onerror=reject})`));
  await route(tab, 'monitor');
  await click(tab, '#refresh');
  await until(tab, `document.querySelector('#application-state').textContent==='ready'`, edition + ' client sees actual adapter');
  record(edition + ': client monitoring sees the real main adapter', true);
}

async function submitChat(fixture, label, text) {
  const tab = fixture.remote;
  await route(tab, 'chat');
  await until(tab, '!document.querySelector("#send").disabled', fixture.edition + ' chat available');
  const before = await evaluate(tab, `document.querySelectorAll('#messages .message:not(.user)').length`);
  await field(tab, '#prompt', text);
  await click(tab, '#send');
  await until(tab, `document.querySelectorAll('#messages .message:not(.user)').length>${before}`, fixture.edition + ' accepted ' + label);
  await evaluate(tab, `document.querySelectorAll('#messages .message:not(.user)')[${before}].setAttribute('data-qa-operation',${JSON.stringify(label)})`);
  return `[data-qa-operation="${label}"]`;
}

async function completed(tab, selector, marker, label) {
  await until(tab, `(()=>{const e=document.querySelector(${JSON.stringify(selector)});return e?.querySelector('small')?.textContent.includes('succeeded')&&e.textContent.includes(${JSON.stringify(marker)})})()`, label, 45000);
}

async function exerciseChat(fixture) {
  const {edition, remote: tab} = fixture;
  const marker = 'QA_MODEL_' + edition.toUpperCase() + '_';
  const prompt = 'Account-owned ' + edition + ' chat ' + runId + ' <img src=x onerror="window.qaInjected=true">';
  const ordinary = await submitChat(fixture, 'ordinary', prompt);
  await completed(tab, ordinary, marker + 'REPLY', edition + ' real consumer reply');
  const after = await status(fixture);
  const modelEvents = after.events.filter(event => event.kind === 'model.fixture');
  record(edition + ': actual consumer invokes the deterministic model boundary once', modelEvents.length === 1);
  record(edition + ': actual database contains this account prompt and answer',
    after.history.some(row => row.message.includes(prompt)) && after.history.some(row => row.message.includes(marker + 'REPLY')));
  record(edition + ': history remains owned by the executing account', after.history.every(row => row.conversation_user_id === modelEvents[0].user_id));
  record(edition + ': bot reply decodes the original literal angle brackets and quotes', await evaluate(tab,
    `document.querySelector(${JSON.stringify(ordinary + ' > span')}).textContent.includes(${JSON.stringify(prompt)})`));
  record(edition + ': decoded bot reply contains no HTML elements or entity artifacts', await evaluate(tab,
    `(()=>{const e=document.querySelector(${JSON.stringify(ordinary + ' > span')});return e.children.length===0&&!e.textContent.includes('&lt;')&&!e.textContent.includes('&quot;')})()`));
  record(edition + ': hostile-looking chat text remains inert', await evaluate(tab, `!window.qaInjected&&!document.querySelector('#messages img')`));
  await capture(tab, edition + '-conversation');
  await viewport(tab, 393, 852);
  record(edition + ': phone conversation has no horizontal overflow', await evaluate(tab, 'document.documentElement.scrollWidth<=innerWidth+1'));
  await capture(tab, edition + '-conversation-phone');
  await viewport(tab, 1100, 900);

  const beforeHistory = await evaluate(tab, `document.querySelectorAll('#messages .message:not(.user)').length`);
  await click(tab, '#history');
  await until(tab, `document.querySelectorAll('#messages .message:not(.user)').length>${beforeHistory}`, edition + ' history receipt');
  await evaluate(tab, `document.querySelectorAll('#messages .message:not(.user)')[${beforeHistory}].setAttribute('data-qa-operation','history')`);
  const historySelector = '[data-qa-operation="history"]';
  await completed(tab, historySelector, marker + 'REPLY', edition + ' actual conversation history');
  record(edition + ': Recent conversation displays the actual saved user prompt', await evaluate(tab, `document.querySelector(${JSON.stringify(historySelector)}).textContent.includes(${JSON.stringify(prompt)})`));
  record(edition + ': history also decodes the saved assistant reply to literal text', await evaluate(tab,
    `document.querySelector(${JSON.stringify(historySelector + ' > span')}).textContent.includes(${JSON.stringify(marker + 'REPLY: ' + prompt)})`));
  record(edition + ': decoded history never executes HTML', await evaluate(tab,
    `!window.qaInjected&&document.querySelector(${JSON.stringify(historySelector + ' > span')}).children.length===0`));
  record(edition + ': Recent conversation does not expose another account', !await evaluate(tab, `document.querySelector(${JSON.stringify(historySelector)}).textContent.includes(${JSON.stringify(forbiddenHistory)})`));
  record(edition + ': history retrieval does not invoke the model', (await status(fixture)).events.filter(event => event.kind === 'model.fixture').length === 1);

  for (const decision of ['proceed', 'deny']) {
    const selector = await submitChat(fixture, decision, 'QA_PERMISSION ' + decision + ' ' + edition);
    await until(tab, `!!document.querySelector(${JSON.stringify(selector + ' .permission button')})`, edition + ' real broker ' + decision);
    record(edition + ': real broker renders exact ' + decision + ' details', await evaluate(tab, `document.querySelector(${JSON.stringify(selector + ' .permission')}).textContent.includes('No command is executed')`));
    if (decision === 'proceed') {
      await capture(tab, edition + '-permission', selector + ' .permission');
      await viewport(tab, 393, 852);
      record(edition + ': phone permission has no horizontal overflow', await evaluate(tab, 'document.documentElement.scrollWidth<=innerWidth+1'));
      record(edition + ': pending permission expands beyond the compact history scroller', await evaluate(tab, `getComputedStyle(document.querySelector('#messages')).maxHeight==='none'`));
      await capture(tab, edition + '-permission-phone', selector + ' .permission');
      await viewport(tab, 1100, 900);
    }
    const button = selector + ' .permission button:nth-of-type(' + (decision === 'proceed' ? 2 : 1) + ')';
    record(edition + ': permission action has the expected label ' + decision, await evaluate(tab, `document.querySelector(${JSON.stringify(button)}).textContent.toLowerCase()===${JSON.stringify(decision)}`));
    await click(tab, button);
    await completed(tab, selector, marker + 'PERMISSION_' + decision.toUpperCase(), edition + ' broker resolved ' + decision);
    const observed = await status(fixture);
    record(edition + ': ' + decision + ' resolves the actual operation broker', observed.events.some(event => event.kind === 'permission.fixture' && event.decision === decision));
    record(edition + ': resolved ' + decision + ' permission removes action buttons', !await evaluate(tab, `!!document.querySelector(${JSON.stringify(selector + ' .permission button')})`));
  }

  const cancelled = await submitChat(fixture, 'cancel', 'QA_CANCEL ' + edition);
  await until(tab, `document.querySelector(${JSON.stringify(cancelled + ' small')})?.textContent.includes('running')&&!!document.querySelector(${JSON.stringify(cancelled + ' .operation-actions button')})`, edition + ' cancel active run');
  await click(tab, cancelled + ' .operation-actions button');
  await completed(tab, cancelled, '', edition + ' cancellation receipt');
  await poll(() => tab.responseAudit.cancelledResults > 0, edition + ' typed cancellation result');
  const cancelAudit = await status(fixture);
  record(edition + ': cancellation reaches the owned generation epoch', cancelAudit.events.some(event => event.kind === 'cancellation.fixture' && event.cancelled));
  record(edition + ': service receipt carries the actual cancelled result', tab.responseAudit.cancelledResults > 0);
  record(edition + ': cancelled work releases its controls', !await evaluate(tab, `!!document.querySelector(${JSON.stringify(cancelled + ' .operation-actions button')})`));

  const recovery = await submitChat(fixture, 'after-cancel', 'Recovery after cancellation ' + edition);
  await completed(tab, recovery, marker + 'REPLY', edition + ' account recovers after cancellation');
  record(edition + ': cancellation leaves the account ready for another real run', true);
  const finalStatus = await status(fixture);
  record(edition + ': second run receives account-scoped prior history', finalStatus.events.filter(event => event.kind === 'model.fixture').slice(1).every(event => event.history_count > 0));
  record(edition + ': no other account appears in rendered responses', !await evaluate(tab, `document.body.innerText.includes(${JSON.stringify(forbiddenHistory)})`));
  await fs.writeFile(path.join(fixture.directory, 'status-after-chat.json'), JSON.stringify(finalStatus, null, 2));
}

async function revokeAccount(fixture, other) {
  const {edition, main, remote: tab, binding} = fixture;
  await navigate(main, fixture.ready.account_url, '#account-state');
  await click(main, 'form:has(input[value="disable"]) button[type="submit"]');
  await until(main, `!document.querySelector('#account-state')?.classList.contains('active')&&!!document.querySelector('#current-password')`, edition + ' main account disabled');
  await poll(async () => { const value = (await status(fixture)).profile; return value && !value.enabled && !value.binding_id; }, edition + ' durable account revocation');
  await until(tab, `document.querySelector('#connection').textContent==='Disconnected'&&document.querySelector('#send').disabled`, edition + ' revoked RC channel');
  const devices = (await native({op: 'devices'})).devices.filter(device => device.binding_id === binding.binding_id);
  record(edition + ': account disable revokes its enrolled devices', devices.length > 0 && devices.every(device => device.revoked));
  record(edition + ': main-page disable closes the live RC channel', true);
  record(edition + ': revocation preserves immutable local account identity', (await status(fixture)).profile.account_id === binding.account_id);
  if (other) {
    record(edition + ': revocation leaves the other edition connected', await evaluate(other.remote, `document.querySelector('#connection').textContent==='Connected'&&!document.querySelector('#send').disabled`));
    record(edition + ': revocation leaves the other account enabled', (await status(other)).profile.enabled);
  }
  await capture(main, edition + '-account-revoked');
  await route(tab, 'security');
  await capture(tab, edition + '-client-revoked');
}

async function deleteDisabledAccount(fixture, other) {
  const {edition, ready, binding} = fixture;
  const before = await status(fixture);
  assert.ok(before.account_exists && before.profile && !before.profile.enabled && !before.profile.binding_id,
    'Deletion confirmation requires the already-disabled, unbound QA account');
  const endpoint = ready.origin + '/qa/delete-account';
  const refused = await fetch(endpoint, {method: 'POST'});
  record(edition + ': fixture deletion requires its owner token', refused.status === 403 && (await status(fixture)).account_exists);
  const response = await fetch(endpoint, {
    method: 'POST', headers: {'X-RC-QA-Token': ready.shutdown_token},
  });
  assert.equal(response.status, 200, 'The isolated QA account deletion must succeed');
  const result = await response.json();
  record(edition + ': actual ORM deletes only its fixed disabled QA account', result.fixture && result.deleted);
  const after = await status(fixture);
  record(edition + ': account deletion cascades its RC profile and owned history',
    !after.account_exists && after.profile === null && after.history.length === 0);
  record(edition + ': deleted account remains absent from active native bindings',
    !(await native({op: 'accounts'})).accounts.some(item => item.id === binding.binding_id));
  record(edition + ': deleted account cannot open the authenticated main page',
    (await fetch(ready.account_url)).status === 403);
  record(edition + ': deletion leaves the other isolated account history unchanged', after.other_account_history_count === 1);
  if (other) {
    record(edition + ': deletion leaves the other edition enabled and connected',
      (await status(other)).profile.enabled && await evaluate(other.remote, `document.querySelector('#connection').textContent==='Connected'`));
  }
  await fs.writeFile(path.join(fixture.directory, 'status-after-deletion.json'), JSON.stringify(after, null, 2));
  await navigate(fixture.main, ready.origin + '/qa/', '#qa-account-deleted');
  await capture(fixture.main, edition + '-account-deleted');
}

try {
  assert.ok(image, 'Set TL_RC_TEST_IMAGE to the isolated artifact build');
  const relativeImage = path.relative(artifacts, image);
  assert.ok(relativeImage && !relativeImage.startsWith('..') && !path.isAbsolute(relativeImage), 'RC QA image must be under repository artifacts');
  assert.equal(path.basename(image), 'Tlachihuani.RC.Server.exe');
  assert.ok(existsSync(image) && existsSync(python) && existsSync(chrome), 'Built QA image, bundled Python and installed Chrome are required');
  await fs.mkdir(output, {recursive: true});
  const scratch = path.join(output, 'scratch');
  await fs.mkdir(scratch, {recursive: true});
  for (const key of ['TEMP', 'TMP', 'TMPDIR', 'TLAMATINI_TEMP']) process.env[key] = scratch;
  console.log('MAIN BROWSER EVIDENCE ' + output);
  console.log('BOUNDARY: fixed QA login and deterministic model; real service, account UI, worker, consumer, history and broker.');
  host = await start(image, ['--console', path.join(output, 'host'), nonce, '12487']);
  await poll(async () => (await fetch(base + '/api/rc/v1/health')).ok, 'isolated RC service');
  await poll(async () => { const state = await native({op: 'status'}); return state.health === 'ready' && state.listener; }, 'isolated RC listener readiness');
  const english = await launchFixture('english', 12488);
  await enableAccount(english);
  await enrollClient(english);
  const spanish = await launchFixture('spanish', 12489);
  await enableAccount(spanish);
  await enrollClient(spanish);
  record('English and Spanish have distinct immutable account bindings',
    english.binding.binding_id !== spanish.binding.binding_id && english.binding.account_id !== spanish.binding.account_id && english.binding.adapter_id !== spanish.binding.adapter_id);
  record('Both real main adapters are independently bound', (await native({op: 'accounts'})).accounts.length === 2);
  await exerciseChat(english);
  await exerciseChat(spanish);
  await revokeAccount(english, spanish);
  await deleteDisabledAccount(english, spanish);
  await revokeAccount(spanish);
  await deleteDisabledAccount(spanish);
  record('Both explicit revocations remove active native bindings', (await native({op: 'accounts'})).accounts.length === 0);
  for (const fixture of fixtures) {
    await fixture.remote.flushAudit();
    record(fixture.edition + ': browser API response audit observed real requests', fixture.remote.responseAudit.read >= 10);
    record(fixture.edition + ': browser API responses contain no other-account sentinel', !fixture.remote.responseAudit.leaked);
    record(fixture.edition + ': all completed API response bodies were audited', fixture.remote.responseAudit.unreadable.length === 0);
    record(fixture.edition + ': main and RC pages have no uncaught JavaScript errors', fixture.main.errors.length === 0 && fixture.remote.errors.length === 0);
  }
} catch (error) {
  failures++;
  checks.push({label: 'Main integration browser run', passed: false, error: error.stack || String(error)});
  console.error(error);
  for (const fixture of fixtures) {
    const tab = fixture.remote || fixture.main;
    if (tab) try { await capture(tab, fixture.edition + '-failure'); } catch (captureError) { console.error(captureError); }
  }
} finally {
  // Stop only processes created by this run through their owned shutdown paths.
  // Chrome windows and screenshots remain for Angela's inspection.
  for (const fixture of fixtures) {
    try {
      if (!fixture.ready) {
        if (fixture.child.exitCode === null) throw new Error(fixture.edition + ' fixture has no authenticated shutdown metadata; owned PID ' + fixture.child.pid);
        continue;
      }
      const response = await fetch(fixture.ready.origin + '/qa/shutdown', {
        method: 'POST', headers: {'X-RC-QA-Token': fixture.ready.shutdown_token},
      });
      assert.equal(response.status, 200, 'Owned fixture shutdown must succeed');
      await boundedExit(fixture.child, fixture.edition + ' fixture');
      record(fixture.edition + ': owned fixture and actual adapter stop cleanly', true);
    } catch (error) {
      failures++;
      checks.push({label: fixture.edition + ' fixture shutdown', passed: false, error: String(error)});
      console.error(error);
    }
  }
  if (host) {
    try { await native({op: 'development-stop'}); await boundedExit(host, 'RC QA service'); record('Owned QA service stops cleanly', true); }
    catch (error) { failures++; checks.push({label: 'RC QA service shutdown', passed: false, error: String(error)}); console.error(error); }
  }
  for (const tab of tabs) { try { await tab.flushAudit(); } catch {} tab.close(); }
  await fs.mkdir(output, {recursive: true});
  await fs.writeFile(path.join(output, 'results.json'), JSON.stringify({
    fixture: true, runId, output, passed: failures === 0, failures, checks, evidence, foregroundChecks,
    scope: 'Actual RC host, real EN/ES account pages, native account binding, AccountWorker, RuntimeSession, AgentConsumer, account-scoped database history, permission broker, owned cancellation and ORM deletion of the disabled QA account. Real foreground HWND and focused-document verification before interactions and captures.',
    limitations: ['Fixed QA authentication', 'Deterministic model boundary; no real LLM inference', 'Fresh isolated SQLite databases; no production data', 'Host speech, process reaping, account wizard and Torrenter staging suppressed', 'Console QA host; not installed SCM acceptance'],
    browsers: fixtures.map(fixture => ({edition: fixture.edition, pid: fixture.browser?.pid, profile: path.join(fixture.directory, 'chrome-profile'), retained: !!fixture.browser})),
    responseAudits: fixtures.filter(fixture => fixture.remote).map(fixture => ({edition: fixture.edition, ...fixture.remote.responseAudit})),
  }, null, 2));
  console.log('MAIN BROWSER RESULT ' + (failures ? 'FAILED' : 'PASSED') + ': ' + checks.filter(check => check.passed).length + ' checks; ' + output);
  process.exitCode = failures ? 1 : 0;
}
