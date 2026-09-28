// In-game options desktop smoke (HD2Runtime 0.25.0+). Launch the app on a COPY of a data root with a 0.25.0 SDK (installed, or
// --sdk-path <HD2Runtime-0.25.0-sdk.zip>) and --remote-debugging-port, then: HD2GUI_CDP_PORT=<port> node tools/mod-options-smoke.mjs
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
const port = process.env.HD2GUI_CDP_PORT ?? 9236;
let pages;
for (let attempt = 0; attempt < 80; attempt++) {
    try { pages = await (await fetch(`http://127.0.0.1:${port}/json`)).json(); if (pages.some(p => p.url === 'https://0.0.0.1/')) break; }
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
const waitFor = async (expression, message) => { for (let i = 0; i < 160; i++) { if (await evaluate(`Boolean(${expression})`)) return; await sleep(250); } throw new Error('Timed out: ' + message + '\n' + (await evaluate('document.body.innerText')).slice(0, 3000)); };
const screenshot = async name => { await fs.mkdir('docs/screenshots', {recursive: true}); const image = await cdp('Page.captureScreenshot', {format: 'png'}); await fs.writeFile('docs/screenshots/' + name + '.png', Buffer.from(image.data, 'base64')); };
const go = async target => { await evaluate(`document.querySelector('[data-nav="${target}"]').click()`); await sleep(400); };
const count = selector => evaluate(`document.querySelectorAll(${JSON.stringify(selector)}).length`);
const click = async selector => { await evaluate(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.click(); })()`); await sleep(400); };
const fill = async (selector, value) => { await evaluate(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.value = ${JSON.stringify(value)}; el.dispatchEvent(new Event('input', {bubbles: true})); el.dispatchEvent(new Event('change', {bubbles: true})); })()`); await sleep(450); };
const text = () => evaluate('document.body.innerText');
const lua = async () => { await go('lua'); await sleep(300); return evaluate('document.querySelector("pre")?.innerText ?? ""'); };
const openProject = async name => { await go('library'); await evaluate(`[...document.querySelectorAll('.project-open')].find(b => b.innerText.includes(${JSON.stringify(name)})).click()`); await waitFor("document.body.innerText.includes('Project overview') && !document.querySelector('.activity')", 'open ' + name); };
const report = {};
const weapon = async name => { await go('player-weapons'); await evaluate(`document.querySelector('[data-weapon=${JSON.stringify(name)}]').click()`); await sleep(700); };
const damageRow = '[data-section="Damage"] [data-object-field="damage.standard_damage"]';
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')", 'startup');
    await go('library'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Create New Mod')).click()"); await waitFor("document.querySelector('#mod-name')", 'create dialog');
    await fill('#mod-name', 'Options Smoke ' + Date.now()); await fill('#author', 'Tests');
    await evaluate("document.querySelector('.dialog form button[type=submit]').click()");
    await waitFor("document.body.innerText.includes('Project overview') && !document.querySelector('.activity')", 'project created');
    const baseline = await lua();

    // Liberator damage 90 -> 110 (shared object, acknowledged once).
    await weapon('AR-23 Liberator');
    await fill(`${damageRow} input`, '110'); await waitFor(`document.querySelector('${damageRow}').classList.contains('modified')`, 'damage autosave');
    await click('[data-section="Damage"] [data-ack-panel="shared"] .ack-check input'); await sleep(300);
    assert.equal(await count(`${damageRow} [data-expose-option]`), 0, 'no option controls before the project switch');
    const plain = await lua(); assert(!plain.includes('hd2.options'));

    // Project switch on: nothing changes until a field is exposed.
    await go('overview'); await sleep(300);
    await click('[data-options-enabled]'); await waitFor("document.querySelector('[data-options-optional]')", 'options enabled');
    assert.equal(await lua(), plain, 'enabling the switch alone changes nothing');

    // Expose the edited damage as a slider.
    await weapon('AR-23 Liberator');
    await click(`${damageRow} [data-expose-option]`); await waitFor("document.querySelector('[data-option-editor]')", 'option editor');
    assert.equal(await evaluate("document.querySelector('[data-option-min]').value"), '90');
    assert.equal(await evaluate("document.querySelector('[data-option-default]').value"), '110');
    await fill('[data-option-label]', 'Liberator Damage'); await fill('[data-option-id]', 'liberator_damage');
    await fill('[data-option-max]', '500'); await fill('[data-option-step]', '10');
    await evaluate(`document.querySelector('[data-option-editor]').scrollIntoView()`); await screenshot('options-editor');
    await click('[data-option-save]'); await waitFor("!document.querySelector('[data-option-editor]')", 'option saved');
    assert.equal(await count(`${damageRow} [data-option-badge]`), 1, 'row shows the In-game option badge');
    const script = await lua();
    assert(script.includes("local options=hd2.options({id="));
    assert(script.includes("local enabled=options:toggle({id='enabled',label='Enabled',default=true})"));
    assert(script.includes("local option_liberator_damage=options:slider({id='liberator_damage',label='Liberator Damage',min=90,max=500,step=10,default=110})"));
    assert(script.includes('hd2.ensure({\n    enabled=enabled,\n    patch={')); assert(script.includes('value=option_liberator_damage,')); assert(script.includes('allow_shared=true,'));
    await screenshot('options-lua');

    // Reopen from the badge; invalid values are explained before saving.
    await weapon('AR-23 Liberator'); await click(`${damageRow} [data-option-badge]`); await waitFor("document.querySelector('[data-option-editor]')", 'reopen');
    await fill('[data-option-default]', '115'); assert((await text()).includes('default must sit on a step'));
    await click('[data-option-editor-backdrop]'); await sleep(300);

    // Changes page marks the configurable edit; Overview lists master + row; export works.
    await go('changes'); await sleep(300); assert(await count('[data-composition-change] [data-option-badge]') >= 1);
    await go('overview'); await sleep(400);
    assert.equal(await count('[data-option-rows] [data-option-row]'), 2); assert.equal(await count('[data-option-row="liberator_damage"]'), 1);
    await evaluate(`document.querySelector('[data-mod-options]').scrollIntoView()`); await sleep(400); await screenshot('options-overview');
    await go('export'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Build / Export Mod')).click()"); await waitFor("document.body.innerText.includes('Export complete')", 'export');

    // Switch off: Lua is exactly the option-free Lua again.
    await go('overview'); await sleep(300); await click('[data-options-enabled]'); await sleep(400);
    assert.equal(await lua(), plain, 'switching options off restores the original Lua');
    console.log('mod options smoke passed');
} finally { socket.close(); }
