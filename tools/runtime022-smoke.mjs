// Developer-only smoke test against a running isolated MAUI app's WebView2 CDP port.
// Node 22+ is used only for UI verification, never by the shipped application.
// Launch Debug with HD2RUNTIMEGUI_DATA_ROOT=<runtime022 sample workspace> and
// WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9236, then run: node tools/runtime022-smoke.mjs
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
const fill = async (selector, value) => { await evaluate(`(() => { const el=document.querySelector(${JSON.stringify(selector)}); if (!el) throw new Error('Missing '+${JSON.stringify(selector)}); el.value=${JSON.stringify(value)}; el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); })()`); await sleep(200); };
const screenshot = async name => { await fs.mkdir('docs/screenshots', {recursive:true}); const image=await cdp('Page.captureScreenshot',{format:'png'}); await fs.writeFile('docs/screenshots/'+name+'.png', Buffer.from(image.data,'base64')); };
const nav = async label => { await waitFor(`[...document.querySelectorAll('.nav-item')].some(b=>b.innerText.includes(${JSON.stringify(label)}))`,label+' navigation'); await evaluate(`[...document.querySelectorAll('.nav-item')].find(b=>b.innerText.includes(${JSON.stringify(label)})).click()`); await sleep(200); };
const open = async name => { await nav('Project library'); await evaluate(`[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes(${JSON.stringify(name)})).click()`); await waitFor("document.body.innerText.includes('Project overview') && !document.querySelector('.activity')",'open '+name); };
const tab = async family => { await evaluate(`document.querySelector('[data-family-tab="${family}"]').click()`); await sleep(200); };
const count = selector => evaluate(`document.querySelectorAll(${JSON.stringify(selector)}).length`);
const list = (selector, map) => evaluate(`[...document.querySelectorAll(${JSON.stringify(selector)})].map(${map})`);
const select = async (name, family = 'all') => { await nav('Stratagems'); await tab(family); await fill('#stratagem-search',name); await evaluate(`document.querySelector('[data-stratagem='+CSS.escape(${JSON.stringify(name)})+']').click()`); await sleep(300); };
const card = id => '[data-stratagem-field][data-semantic-field="'+id+'"]';
const text = () => evaluate('document.body.innerText');
const build = async () => { await nav('Build / Export'); await evaluate("[...document.querySelectorAll('button')].find(b=>b.innerText.includes('Build / Export Mod')).click()"); await waitFor("document.body.innerText.includes('Export complete')",'export'); console.log(await evaluate("document.querySelector('.export-success code').innerText")); };
const AT = 'E/AT-12 Anti-Tank Emplacement';
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')",'startup');
    await open('AntiTankEmplacement022'); await nav('Stratagems');
    assert.deepEqual(await list('[data-family-tab]', 't=>t.dataset.familyTab'), ['all','orbital','eagle','support','sentry','emplacement','mine']);
    for (const [family, expected] of [['all',73],['sentry',10],['emplacement',4],['mine',4]]) { await tab(family); await fill('#stratagem-search',''); assert.equal(await count('.weapon-list [data-stratagem]'), expected, family); }
    await select(AT, 'emplacement');
    assert.equal(await count('[data-entity-stats="main"]'), 1);
    assert.equal(await count('[data-mounted-weapon="primary"]'), 1);
    assert.deepEqual(await list('[data-mounted-weapon] [data-stratagem-branch]', 'b=>b.dataset.stratagemBranch'), ['primary','primary_damage','primary_impact','primary_impact_damage']);
    assert.deepEqual(await list('[data-entity-stats] > .stat-grid h3', 'h=>h.innerText'), ['Health','Armor']);
    assert.equal(await evaluate(`document.querySelector('${card('entity.health')} .modified-value').dataset.baseline`), '300');
    assert.equal(await evaluate(`document.querySelector('${card('entity.health')} .modified-value').dataset.desired`), '600');
    assert.equal(await count('[data-stratagem-field].modified'), 5);
    assert.equal(await count('[data-shared-scope] input:checked'), 2);
    assert((await text()).includes('Targeting (range, traverse, tracking, firing arc)'));
    assert.equal(await count('[data-stratagem-field] input[type=number]'), await count('[data-stratagem-field]') - 1, 'only max uses is read-only');
    await fill(card('entity.armor')+' input[type=number]','4');
    await waitFor(`document.querySelector('${card('entity.armor')} .modified-value')?.dataset.desired === '4'`, 'armor autosave');
    await nav('Lua Preview'); const lua = await evaluate('document.querySelector("pre").innerText');
    assert(lua.includes(":deployed_entity():weapon('primary'):attack('primary_impact')")); assert(lua.includes('{field=hd2.fields.entity.armor,expect=2,value=4}'));
    await select(AT, 'emplacement'); await fill(card('entity.armor')+' input[type=number]','3');
    await waitFor(`document.querySelector('${card('entity.armor')} .modified-value')?.dataset.desired === '3'`, 'armor restore');
    await screenshot('runtime022-anti-tank-emplacement');
    await evaluate("document.querySelector('[data-mounted-weapon]').scrollIntoView()"); await screenshot('runtime022-anti-tank-weapon');
    await nav('Changes'); const changes = await text(); assert(changes.includes('Deployed Entity')); assert(changes.includes('Mounted Weapon · Primary'));
    await build();
    await select('MD-6 Anti-Personnel Minefield', 'mine');
    assert.equal(await count('[data-mine-scope]'), 1); assert.equal(await count('[data-mounted-weapon]'), 0);
    assert.equal(await count('[data-stratagem-field] input[type=number]'), 3);
    assert.equal(await count('[data-blocked-field="mine explosion/status"]'), 1);
    await screenshot('runtime022-minefield');
    await select('E/GL-21 Grenadier Battlement', 'emplacement');
    assert.equal(await count('[data-mounted-weapon]'), 0); assert.equal(await count('[data-blocked-field="mounted weapon"]'), 1);
    await open('UnusualSentry022'); await select('A/LAS-98 Laser Sentry', 'sentry');
    assert.deepEqual(await list('[data-mounted-weapon] .group-title', 'h=>h.textContent.trim()'), ['Heat','Heatsinks','Beam','Damage','Status · slot 1']);
    assert.equal(await count('[data-semantic-field="weapon.fire_rate"]'), 0);
    assert.equal(await count('[data-stratagem-field].modified'), 2);
    assert((await list('[data-shared-scope] [data-shared-consumer]', 'l=>l.innerText')).some(c => c.includes('A/LAS-98 Laser Sentry')));
    await screenshot('runtime022-laser-sentry'); await build();
    await select('A/ARC-3 Tesla Tower', 'sentry'); assert.equal(await count('[data-branch-kind="ArcSettings"]'), 1);
    await open('ConventionalSentry022'); await select('A/MG-43 Machine Gun Sentry', 'sentry');
    assert.equal(await count('[data-stratagem-field].modified'), 5); await build();
    await open('ExplosiveSentry022'); await select('A/MLS-4X Rocket Sentry', 'sentry');
    assert.equal(await count('[data-stratagem-field].modified'), 2); await build();
    await nav('Project library');
    console.log('PASS: four 0.22 projects, family tabs, entity/weapon/attack graph, health/armor autosave, shared scopes, mines and blocked presentation, Lua and exports');
} finally { socket.close(); }
