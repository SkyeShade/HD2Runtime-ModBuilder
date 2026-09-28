// Developer-only smoke test against a running isolated MAUI app's WebView2 CDP port.
// Node 22+ is used only for UI verification, never by the shipped application.
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
let pages;
for (let attempt = 0; attempt < 60; attempt++) {
    try { pages = await (await fetch(`http://127.0.0.1:${process.env.HD2GUI_CDP_PORT ?? 9236}/json`)).json(); if (pages.some(p => p.url === 'https://0.0.0.1/')) break; }
    catch { /* WebView2 is still starting. */ }
    await new Promise(resolve => setTimeout(resolve, 500));
}
assert(pages?.length, 'WebView2 CDP port must be enabled on the running Debug app');
const page = pages.find(p => p.url === 'https://0.0.0.1/');
assert(page, 'MAUI Blazor WebView page must be running');
const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let id = 0;
const pending = new Map();
socket.onmessage = ({data}) => { const message = JSON.parse(data); if (message.id) { const p = pending.get(message.id); pending.delete(message.id); message.error ? p.reject(message.error) : p.resolve(message.result); } };
const cdp = (method, params = {}) => new Promise((resolve, reject) => { const n = ++id; pending.set(n, {resolve, reject}); socket.send(JSON.stringify({id: n, method, params})); });
const evaluate = async expression => { const result = await cdp('Runtime.evaluate', {expression, returnByValue: true, awaitPromise: true}); if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails)); return result.result.value; };
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const waitFor = async (expression, message) => { for (let i=0;i<120;i++) { if (await evaluate(`Boolean(${expression})`)) return; await sleep(250); } throw new Error('Timed out: ' + message + '\n' + await evaluate('document.body.innerText')); };
const screenshot = async name => { await fs.mkdir('docs/screenshots', {recursive:true}); const image=await cdp('Page.captureScreenshot',{format:'png'}); await fs.writeFile('docs/screenshots/'+name+'.png', Buffer.from(image.data,'base64')); };
const go = async page => { await evaluate(`document.querySelector('[data-nav="${page}"]').click()`); await sleep(300); };
const count = selector => evaluate(`document.querySelectorAll(${JSON.stringify(selector)}).length`);
const list = (selector, map) => evaluate(`[...document.querySelectorAll(${JSON.stringify(selector)})].map(${map})`);
const fill = async (selector, value) => { await evaluate(`(() => { const el=document.querySelector(${JSON.stringify(selector)}); if (!el) throw new Error('Missing '+${JSON.stringify(selector)}); el.value=${JSON.stringify(value)}; el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); })()`); await sleep(300); };
const open = async name => { await go('library'); await evaluate(`[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes(${JSON.stringify(name)})).click()`); await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')",'open '+name); };
const pick = async name => { await go('stratagems:support'); await evaluate(`document.querySelector('[data-stratagem='+CSS.escape(${JSON.stringify(name)})+']').click()`); await sleep(400); };
const GR8 = 'GR-8 Recoilless Rifle';
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')",'startup');
    await open('ExplosiveSentry022');
    await pick(GR8);
    assert.equal(await count(`[data-support-linked="${GR8}"]`), 1);
    assert.equal(await count('[data-delivery-graph]'), 1);
    assert.equal(await count(`[data-support-weapon="${GR8}"]`), 1);
    assert.equal(await count('[data-stratagem-field][data-semantic-field="stratagem.cooldown"] input[type=number]'), 1);
    const weaponInputs = await count('[data-support-field] input'); assert(weaponInputs > 10, 'weapon controls merged: ' + weaponInputs);
    await fill('[data-support-field][data-semantic-field="weapon.sway"] input[type=number]', '0.5');
    await fill('[data-stratagem-field][data-semantic-field="stratagem.cooldown"] input[type=number]', '300');
    await waitFor(`document.querySelector('[data-support-field][data-semantic-field="weapon.sway"]').classList.contains('modified')`, 'weapon autosave');
    await screenshot('support-linked-gr8');
    await go('lua'); const lua = await evaluate('document.querySelector("pre").innerText');
    assert(lua.includes(`hd2.support_weapon('${GR8}')`)); assert(lua.includes(`hd2.stratagem('${GR8}')`));
    await go('changes');
    assert.equal(await count(`[data-stratagem-change-group="${GR8}"]`), 1);
    await evaluate(`document.querySelector('[data-stratagem-change-group="${GR8}"] summary').click()`); await sleep(300);
    assert.equal(await count(`[data-stratagem-change-group="${GR8}"] [data-linked-weapon="${GR8}"] [data-support-field].modified`), 1);
    assert(!(await evaluate('document.body.innerText')).includes('Support weapon changes'), 'linked weapon edits are not listed separately');
    await screenshot('support-linked-changes');
    await pick(GR8);
    await evaluate("[...document.querySelectorAll('button')].find(b=>b.innerText.trim()==='Reset support weapon').click()"); await sleep(400);
    await evaluate("[...document.querySelectorAll('button')].find(b=>b.innerText.trim()==='Reset stratagem').click()"); await sleep(400);
    assert.equal(await count('[data-stratagem-field].modified, [data-support-field].modified'), 0);
    await pick('MG-43 Machine Gun');
    assert.equal(await count('[data-support-linked="MG-43 Machine Gun"]'), 1); assert.equal(await count('[data-support-identity-blocked]'), 1);
    assert.equal(await count('[data-support-field]'), 0); assert.equal(await count('[data-semantic-field="stratagem.cooldown"] input[type=number]'), 1);
    await pick('MS-11 Solo Silo');
    assert.deepEqual(await list('[data-delivery-node]', 'n=>n.dataset.deliveryNode'), ['call_in','delivery','attack_entity','branch:swp-solo-silo-e','branch:swp-solo-silo-eimpact']);
    assert(await count('[data-support-field][data-support-role="detonation"]') > 0); assert(await count('[data-support-field][data-support-role="impact"]') > 0);
    await evaluate("document.querySelector('[data-delivery-graph]').scrollIntoView()"); await screenshot('support-linked-solo-silo');
    for (const name of ['B/MD C4 Pack','SG-88 Break-Action Shotgun','CQC-72 Entrenchment Tool']) {
        await pick(name); assert.equal(await count('[data-support-link-unresolved]'), 1, name); assert.equal(await count('[data-support-weapon]'), 0, name);
    }
    await go('stratagems:support');
    assert.deepEqual((await list('.weapon-list [data-unlinked-support]', 'b=>b.dataset.unlinkedSupport')).sort(), ['B/MD C4 Pack','CQC-72 Entrenchment Tool','SG-88 Break-Action Shotgun']);
    await go('library');
    console.log('PASS: 32 linked support entries merged (GR-8 edit/Lua/Changes, MG-43 guard, Solo Silo graph), 3 unresolved kept separate');
} finally { socket.close(); }
