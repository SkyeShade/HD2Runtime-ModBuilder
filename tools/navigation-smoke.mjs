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
const color = selector => evaluate(`getComputedStyle(document.querySelector(${JSON.stringify(selector)})).color`);
const open = async name => { await go('library'); await evaluate(`[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes(${JSON.stringify(name)})).click()`); await waitFor("document.body.innerText.includes('Project overview') && !document.querySelector('.activity')",'open '+name); };
const expectedNav = ['library','overview','player-weapons','stratagems','stratagems:support','stratagems:offensive','stratagems:defensive','vehicles','backpacks','support','changes','lua','export','research'];
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')",'startup');
    assert.deepEqual(await list('.sidebar [data-nav]', 'b=>b.dataset.nav'), expectedNav);
    const labels = (await evaluate("document.querySelector('.sidebar').innerText")).split('\n');
    // 'Vehicles' is the hd2.vehicle destination since 0.23.0; the legacy mapped categories stay hidden.
    for (const hidden of ['Legacy mapped stratagems','Equipment']) assert(!labels.some(l => l.includes(hidden)), hidden + ' must be hidden');
    assert((await evaluate("document.querySelector('.project-label').innerText")).includes('No project selected'));
    for (const page of ['overview','player-weapons','stratagems','stratagems:defensive','vehicles','backpacks','support','changes','lua','export']) {
        await go(page); assert.equal(await count('[data-empty-workspace]'), 1, page);
        assert((await evaluate("document.querySelector('[data-empty-workspace]').innerText")).includes('Select or create a project to begin editing.'));
        assert.deepEqual(await list('.sidebar [data-nav]', 'b=>b.dataset.nav'), expectedNav, 'navigation persists on ' + page);
    }
    await go('player-weapons'); await screenshot('navigation-no-project');
    await go('research'); assert.equal(await count('[data-empty-workspace]'), 0);
    await open('AntiTankEmplacement022');
    const palette = {};
    for (const [category, expected, families] of [['offensive',20,['all','orbital','eagle']],['defensive',18,['all','sentry','emplacement','mine']],['support',35,['all','support']]]) {
        await go('stratagems:' + category);
        assert.equal(await count(`[data-nav="stratagems:${category}"].selected`), 1);
        assert.equal(await count(`.category-pill.active[data-category="${category}"].cat-${category}`), 1);
        assert.equal(await count('.weapon-list [data-stratagem]'), expected, category);
        assert.deepEqual(await list('[data-family-tab]', 't=>t.dataset.familyTab'), families);
        assert.equal(await count(`.weapon-list [data-stratagem].cat-${category}`), expected);
        palette[category] = await color(`.category-pill.active[data-category="${category}"]`);
    }
    assert.equal(new Set(Object.values(palette)).size, 3, 'support, offensive and defensive use distinct colors');
    await go('stratagems:defensive');
    for (const name of ['FX-12 Shield Generator Relay','A/MG-43 Machine Gun Sentry','E/AT-12 Anti-Tank Emplacement','MD-6 Anti-Personnel Minefield'])
        assert.equal(await count(`.weapon-list [data-stratagem="${name}"]`), 1, name);
    await evaluate(`document.querySelector('[data-stratagem="E/AT-12 Anti-Tank Emplacement"]').click()`); await sleep(300);
    assert.equal(await count('.category-panel.cat-defensive[data-stratagem-category="defensive"]'), 1);
    await screenshot('navigation-defensive');
    await go('stratagems:offensive'); assert.equal(await count('.weapon-list [data-stratagem="Orbital Laser"]'), 1); assert.equal(await count('.weapon-list [data-stratagem="Eagle Airstrike"]'), 1);
    await go('stratagems:support'); await evaluate(`document.querySelector('[data-stratagem="GR-8 Recoilless Rifle"]').click()`); await sleep(300);
    assert.equal(await count('[data-support-linked="GR-8 Recoilless Rifle"]'), 1); assert.equal(await count('[data-semantic-field="stratagem.cooldown"] input[type=number]'), 1);
    await screenshot('navigation-support');
    await go('support'); await waitFor("document.querySelectorAll('.weapon-list [data-support]').length === 3", 'unlinked support weapons');
    await go('stratagems'); assert.equal(await count('.category-pill.active[data-category="all"]'), 1); assert.equal(await count('.weapon-list [data-stratagem]'), 73);
    await go('changes'); assert.equal(await count('[data-change-category="defensive"]'), 1);
    await go('library');
    console.log('PASS: public-only navigation, persistent no-project navigation and empty states, category pills/colors/counts, merged support notice, unlinked Support Weapons list');
} finally { socket.close(); }
