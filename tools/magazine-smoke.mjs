// Developer-only smoke test for Runtime 0.23.1 magazine attachments against a running isolated MAUI app (WebView2 CDP).
// Launch Debug with HD2RUNTIMEGUI_DATA_ROOT=<0.23.1 sample workspace> and
// WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9236, then run: node tools/magazine-smoke.mjs
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
let pages;
for (let attempt = 0; attempt < 60; attempt++) {
    try { pages = await (await fetch(`http://127.0.0.1:${process.env.HD2GUI_CDP_PORT ?? 9236}/json`)).json(); if (pages.some(p => p.url === 'https://0.0.0.1/')) break; }
    catch { /* WebView2 is still starting. */ }
    await new Promise(resolve => setTimeout(resolve, 500));
}
const page = pages.find(p => p.url === 'https://0.0.0.1/');
assert(page, 'MAUI Blazor WebView page must be running');
const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let id = 0; const pending = new Map();
socket.onmessage = ({data}) => { const message = JSON.parse(data); if (message.id) { const p = pending.get(message.id); pending.delete(message.id); message.error ? p.reject(message.error) : p.resolve(message.result); } };
const cdp = (method, params = {}) => new Promise((resolve, reject) => { const n = ++id; pending.set(n, {resolve, reject}); socket.send(JSON.stringify({id: n, method, params})); });
const evaluate = async expression => { const result = await cdp('Runtime.evaluate', {expression, returnByValue: true, awaitPromise: true}); if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails)); return result.result.value; };
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const waitFor = async (expression, message) => { for (let i=0;i<120;i++) { if (await evaluate(`Boolean(${expression})`)) return; await sleep(250); } throw new Error('Timed out: ' + message + '\n' + await evaluate('document.body.innerText')); };
const screenshot = async name => { await fs.mkdir('docs/screenshots', {recursive:true}); const image=await cdp('Page.captureScreenshot',{format:'png'}); await fs.writeFile('docs/screenshots/'+name+'.png', Buffer.from(image.data,'base64')); };
const go = async target => { await evaluate(`document.querySelector('[data-nav="${target}"]').click()`); await sleep(350); };
const count = selector => evaluate(`document.querySelectorAll(${JSON.stringify(selector)}).length`);
const click = async selector => { await evaluate(`(() => { const el=document.querySelector(${JSON.stringify(selector)}); if (!el) throw new Error('Missing '+${JSON.stringify(selector)}); el.click(); })()`); await sleep(350); };
const fill = async (selector, value) => { await evaluate(`(() => { const el=document.querySelector(${JSON.stringify(selector)}); if (!el) throw new Error('Missing '+${JSON.stringify(selector)}); el.value=${JSON.stringify(value)}; el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); })()`); await sleep(350); };
const open = async name => { await go('library'); await evaluate(`[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes(${JSON.stringify(name)})).click()`); await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')",'open '+name); };
const weapon = async name => { await go('player-weapons'); await evaluate(`document.querySelector('[data-weapon=${JSON.stringify(name)}]').click()`); await sleep(400); };
const DRUM = 'weapon-attachment/v1/magazine/rifle-5-5x50mm-drum/fa499a29b375c6cf';
const card = `[data-attachment-id="${DRUM}"]`;
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')",'startup');
    await open('AntiTankEmplacement022');
    await weapon('AR-23C Liberator Concussive');
    assert.equal(await count('[data-magazine-attachments="AR-23C Liberator Concussive"]'), 1);
    assert.equal(await count(`${card}[data-relationship="native_resource_default"] [data-default-magazine]`), 1);
    assert.equal(await evaluate(`document.querySelector('${card} [data-semantic-field="attachment.magazine_capacity"] [data-baseline]').dataset.baseline`), '60');
    assert.equal(await count(`${card} [data-entity-field] input[type=number]`), 4);
    assert.equal(await evaluate(`document.querySelector('${card} [data-semantic-id]').innerText`), DRUM);
    assert.equal(await count(`${card} [data-flag="allow_shared"]`), 1); assert.equal(await count(`${card} [data-flag="allow_unverified_effect"]`), 1);
    for (const name of ['Short Magazine', 'Extended Magazine']) { assert.equal(await count(`[data-unresolved-option="${name}"]`), 1); assert.equal(await count(`[data-unresolved-option="${name}"] input`), 0); }
    assert.equal(await count('[data-selection-blocked]'), 1); assert.equal(await count('[data-placeholder-capacity]'), 1);
    assert.equal(await count('[data-ammo-owned-by-attachment]'), 1);
    assert.equal(await count('[data-field="weapon.capacity"] input:not([disabled])'), 0, 'weapon-level capacity is not editable');
    await fill(`${card} [data-semantic-field="attachment.magazine_capacity"] input`, '90');
    await waitFor(`document.querySelector('${card}').classList.contains('modified')`, 'attachment autosave');
    assert(/Build requires review: Acknowledge/.test(await evaluate('document.body.innerText')), 'build blocked until acknowledged');
    await click(`${card} [data-attachment-ack]`);
    await evaluate(`document.querySelector('${card}').scrollIntoView({block:'center'})`); await screenshot('runtime0231-concussive-drum');
    await go('lua'); const lua = await evaluate('document.querySelector("pre").innerText');
    assert(lua.includes(`hd2.weapon_attachment('${DRUM}')`)); assert(lua.includes('allow_shared=true')); assert(lua.includes('allow_unverified_effect=true'));
    assert(lua.includes('field=hd2.fields.attachment.magazine_capacity')); assert(lua.includes('value=90'));
    await go('changes'); assert.equal(await count(`[data-entity-change-group="weapon_attachment:${DRUM}"]`), 1);
    assert((await evaluate(`document.querySelector('[data-entity-change-group="weapon_attachment:${DRUM}"] summary').innerText`)).includes('Liberator Concussive'));
    await click(`[data-entity-change-group="weapon_attachment:${DRUM}"] summary`); await screenshot('runtime0231-changes');
    await go('export'); await evaluate("[...document.querySelectorAll('button')].find(b=>b.innerText.includes('Build / Export Mod')).click()"); await waitFor("document.body.innerText.includes('Export complete')",'export');
    await go('changes'); await click(`[data-entity-change-group="weapon_attachment:${DRUM}"] summary`);
    await evaluate(`[...document.querySelectorAll('[data-entity-change-group="weapon_attachment:${DRUM}"] button')].find(b=>b.innerText.trim()==='Reset attachment').click()`); await sleep(400);
    assert.equal(await count(`[data-entity-change-group="weapon_attachment:${DRUM}"]`), 0);
    await weapon('AR-2 Coyote'); assert.equal(await count('[data-magazine-attachments]'), 0, 'weapon-owned magazines have no attachment section');
    await weapon('LAS-5 Scythe'); assert.equal(await count('[data-no-resolved]'), 1);
    await go('library');
    console.log('PASS: Concussive drum (60 rounds, default, semantic ID, flags), ambiguous options unresolved, selection blocked, acknowledgement gate, Lua, changes, export, reset');
} finally { socket.close(); }
