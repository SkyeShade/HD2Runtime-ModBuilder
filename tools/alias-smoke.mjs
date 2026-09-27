// Developer-only smoke test against a running Debug MAUI app's WebView2 CDP port.
// Node 22+ is used only for UI verification, never by the shipped application.
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
let pages;
for (let attempt = 0; attempt < 60; attempt++) {
    try { pages = await (await fetch(`http://127.0.0.1:${process.env.HD2GUI_CDP_PORT ?? 9223}/json`)).json(); if (pages.some(p => p.url === 'https://0.0.0.1/')) break; }
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
const click = async text => { await evaluate(`(() => { const el = [...document.querySelectorAll('button')].find(b => b.innerText.trim() === ${JSON.stringify(text)}); if (!el) throw new Error('Missing button: '+${JSON.stringify(text)}); el.click(); })()`); await sleep(200); };
const fill = async (selector, value) => { await evaluate(`(() => { const el=document.querySelector(${JSON.stringify(selector)}); el.value=${JSON.stringify(value)}; el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); })()`); await sleep(100); };
const screenshot = async name => { await fs.mkdir('docs/screenshots', {recursive:true}); const image=await cdp('Page.captureScreenshot',{format:'png'}); await fs.writeFile('docs/screenshots/'+name+'.png', Buffer.from(image.data,'base64')); };
const nav = async label => { await evaluate(`[...document.querySelectorAll('.nav-item')].find(b=>b.innerText.includes(${JSON.stringify(label)})).click()`); await sleep(150); };
const choose = async weapon => { await fill('#weapon-search',weapon); await evaluate(`document.querySelector('[data-weapon='+CSS.escape(${JSON.stringify(weapon)})+']').click()`); await sleep(150); };
const fieldClick = async (field,label) => { await evaluate(`[...document.querySelector('[data-field="${field}"]').querySelectorAll('button')].find(b=>b.innerText===${JSON.stringify(label)}).click()`); await sleep(200); };
const group = name => `[data-weapon-group="${name}"]`;
const expand = async name => { await evaluate(`document.querySelector(${JSON.stringify(group(name)+' > summary')}).click()`); await sleep(100); };
const state = async field => evaluate(`({modified:document.querySelector('[data-field="${field}"]').classList.contains('modified'),text:document.querySelector('[data-field="${field}"] .value-comparison').innerText})`);
// Isolated sample library: Verdict capacity saved under weapon.capacity;
// Punisher has both feed aliases (feed 1 conflicts at 12 vs 10, feed 2 equals 10).
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')",'startup');
    await evaluate("[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes('VerdictMagazine')).click()"); await sleep(200);
    await nav('Player Weapons'); await choose('P-113 Verdict');
    assert.equal(await evaluate("document.querySelectorAll('[data-field=\"weapon.capacity\"]').length"),0);
    assert.equal(await evaluate("document.querySelectorAll('[data-field=\"magazine.capacity\"] input[type=number]').length"),1);
    assert.equal(await evaluate("document.querySelector('[data-field=\"magazine.capacity\"] input[type=number]').value"),process.argv.includes('--relaunch')?'17':'15');
    assert(await evaluate("!!document.querySelector('[data-field=\"weapon.base_capacity\"]')"));
    if(!process.argv.includes('--relaunch')) {
        await fill('[data-field="magazine.capacity"] input[type=number]','17');
        await waitFor("!document.querySelector('.stat-card [role=status]')",'alias autosave');
    }
    await nav('Changes'); await expand('P-113 Verdict');
    assert.equal(await evaluate("document.querySelectorAll('[data-change=\"weapon.capacity\"]').length"),0);
    assert.equal(await evaluate("document.querySelectorAll('[data-change=\"magazine.capacity\"]').length"),1);
    await nav('Project library');
    await evaluate("[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes('PunisherDualFeed')).click()"); await sleep(200);
    await nav('Player Weapons'); await choose('SG-8 Punisher');
    for(const n of ['1','2']) assert.equal(await evaluate(`document.querySelectorAll('[data-field="weapon.feed_capacity_${n}"]').length`),0);
    assert.equal(await evaluate("document.querySelectorAll('[data-field=\"rounds.feed_capacity_2\"] input[type=number]').length"),1);
    if(!process.argv.includes('--relaunch')) {
        assert(await evaluate("!!document.querySelector('[data-field=\"rounds.feed_capacity_1\"] [data-alias-conflict]')"));
        assert.equal(await evaluate("document.querySelectorAll('[data-field=\"rounds.feed_capacity_1\"] input[type=number]').length"),0);
        await nav('Lua Preview'); assert((await evaluate('document.querySelector("pre").innerText')).includes('Build blocked'));
        await nav('Changes'); await expand('SG-8 Punisher');
        assert.equal(await evaluate("document.querySelectorAll('[data-change]').length"),5);
        assert(await evaluate("!!document.querySelector('[data-alias-conflict]')"));
        await screenshot('alias-migration-conflict');
        await click('Keep weapon.feed_capacity_1 value');
        await waitFor("!document.querySelector('[data-alias-conflict]')",'conflict resolved');
    }
    await nav('Lua Preview'); const lua=await evaluate('document.querySelector("pre").innerText');
    assert(lua.includes('hd2.fields.rounds.feed_capacity_1,expect=8,value=12'));
    assert(!lua.includes('hd2.fields.weapon.feed_capacity'));
    assert.equal(lua.split('field=hd2.fields.rounds.feed_capacity_2').length-1,1);
    await click('↗ Build / Export Mod'); await waitFor("document.body.innerText.includes('Export complete')",'resolved export');
    await nav('Project library');
    console.log('PASS: preferred canonical controls, preserved alias values, no offset deduplication, conflict blocking/choice, coalesced changes/Lua, export'+(process.argv.includes('--relaunch')?', relaunch persistence':''));
} finally { socket.close(); }
