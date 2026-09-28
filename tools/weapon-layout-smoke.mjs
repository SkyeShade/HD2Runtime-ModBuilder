// Player Weapons authoring layout smoke: authoring-first sections, compact rows, collapsed secondary sections and grouped
// acknowledgements (shared projectile damage and a shared + unverified magazine attachment), including build blocking.
// Usage: launch the desktop app with WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=<port>, then
//        HD2GUI_CDP_PORT=<port> node tools/weapon-layout-smoke.mjs
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
const weapon = async name => { await go('player-weapons'); await evaluate(`document.querySelector('[data-weapon=${JSON.stringify(name)}]').click()`); await sleep(700); };
const summary = key => evaluate(`Number(document.querySelector('[data-authoring-summary] [data-' + ${JSON.stringify(key)}.replace(/[A-Z]/g, c => '-' + c.toLowerCase()) + ']').dataset[${JSON.stringify(key)}])`);
const blocked = async () => /Build requires review/.test(await evaluate('document.body.innerText'));
const CARBINE = 'AR-23A Liberator Carbine';
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')", 'startup');
    await go('library'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Create New Mod')).click()"); await waitFor("document.querySelector('#mod-name')", 'create dialog');
    await fill('#mod-name', 'Layout Smoke ' + Date.now()); await fill('#author', 'Tests');
    await evaluate("document.querySelector('.dialog form button[type=submit]').click()");
    await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')", 'project created');

    await weapon(CARBINE);
    const order = await evaluate(`[...document.querySelectorAll('.weapon-detail > .semantic-section')].map(s => s.dataset.section)`);
    assert.deepEqual(order.slice(0, 5), ['Weapon', 'Ammo / Magazine', 'Handling', 'Projectile', 'Damage'], 'authoring-first order: ' + order);
    assert.deepEqual(order.slice(-3), ['Attachments', 'Advanced / Read-only', 'Evidence'], 'secondary sections last: ' + order);
    assert.equal(await summary('summaryModified'), 0); assert.equal(await summary('summaryAcks'), 0);
    // Compact rows instead of cards; secondary sections collapsed; provenance only behind ⓘ or in Evidence.
    assert.equal(await count('.weapon-detail .stat-card'), 0);
    for (const s of ['Attachments', 'Advanced / Read-only', 'Evidence']) assert.equal(await evaluate(`document.querySelector('[data-section="${s}"]').open`), false, s);
    assert(await count('[data-section="Advanced / Read-only"] .field-row') > 0);
    assert.equal(await count('[data-section="Advanced / Read-only"] .field-row input'), 0, 'Advanced is read-only');
    assert.equal(await evaluate(`[...document.querySelectorAll('.weapon-detail details.provenance')].filter(d => !d.closest('[data-section="Evidence"]')).length`), 0);
    assert.equal(await evaluate(`document.querySelector('[data-composition-attack]').open`), false, 'composition collapsed');
    assert((await evaluate(`document.querySelector('[data-attachment-summary]').innerText`)).includes('Magazine'));
    // Magazine: attachment-owned block with one ownership sentence, other options collapsed, placeholders in Advanced.
    assert.equal(await count('[data-section="Ammo / Magazine"] [data-magazine-attachments]'), 1);
    assert.equal(await count('[data-ammo-owned-by-attachment]'), 1);
    assert.equal(await evaluate(`document.querySelector('[data-other-magazines]').open`), false);
    assert.equal(await count('[data-section="Ammo / Magazine"] [data-field]'), 0, 'weapon-level magazine fields moved to Advanced');
    // Editable only hides Advanced entirely.
    await click('[data-editable-only]'); assert.equal(await count('[data-section="Advanced / Read-only"]'), 0); await click('[data-editable-only]');
    assert.equal(await count('[data-section="Advanced / Read-only"]'), 1);

    // Shared projectile damage: one acknowledgement panel in the Damage section; build blocked until it is checked.
    const damage = '[data-section="Damage"] [data-object-field]';
    const shared = await count(`${damage}:first-child [data-flag="allow_shared"]`) > 0;
    const base = Number(await evaluate(`document.querySelector('${damage} [data-baseline]').dataset.baseline`));
    await fill(`${damage} input`, String(base + 10));
    await waitFor(`document.querySelector('${damage}').classList.contains('modified')`, 'damage autosave');
    assert.equal(await summary('summaryModified'), 1);
    assert.equal(await count(`${damage}.modified .modified-badge`), 1, 'modified field visible');
    if (shared) {
        const panel = '[data-section="Damage"] [data-ack-panel="shared"]';
        assert.equal(await count(panel), 1);
        assert.equal(await evaluate(`document.querySelector('${panel} [data-ack-state]').innerText`), 'Acknowledgement required');
        assert(await evaluate(`document.querySelector('${panel} .ack-items').innerText.includes('Liberator')`), 'lists affected weapons');
        assert.equal(await summary('summaryAcks'), 1); assert(await blocked(), 'build blocked until acknowledged');
        await evaluate(`document.querySelector('${panel}').scrollIntoView({block:'center'})`); await sleep(800); await screenshot('after-carbine-ack-required');
        await click(`${panel} .ack-check input`);
        await waitFor(`document.querySelector('${panel}').classList.contains('acknowledged')`, 'acknowledged');
        assert.equal(await summary('summaryAcks'), 0); assert(!(await blocked()), 'build unblocked');
    }
    // Magazine attachment: combined shared + unverified acknowledgement in one panel.
    const card = '[data-section="Ammo / Magazine"] [data-attachment-id]';
    const capacity = `${card} [data-semantic-field="attachment.magazine_capacity"]`;
    const cap = Number(await evaluate(`document.querySelector('${capacity} [data-baseline]').dataset.baseline`));
    await fill(`${capacity} input`, String(cap + 5));
    await waitFor(`document.querySelector('${card}').classList.contains('modified')`, 'attachment autosave');
    assert.equal(await count(`${card} [data-ack-panel="unverified"] [data-attachment-ack]`), 1);
    assert.equal(await summary('summaryAcks'), 1); assert(await blocked());
    await evaluate(`document.querySelector('${card}').scrollIntoView({block:'center'})`); await sleep(800); await screenshot('after-carbine-magazine-ack');
    await click(`${card} [data-attachment-ack]`);
    await waitFor(`document.querySelector('${card} [data-ack-panel]').classList.contains('acknowledged')`, 'attachment acknowledged');
    assert.equal(await summary('summaryAcks'), 0); assert(!(await blocked()));
    // Review changes goes to the Changes page.
    await click('[data-review-changes]');
    assert.equal(await evaluate(`document.querySelector('[data-nav="changes"]').getAttribute('aria-current')`), 'page');
    // The Changes page shows the same acknowledgement panels (satisfiable there too).
    if (shared) {
        const group = `[data-weapon-group="${CARBINE}"]`;
        assert.equal(await count(`${group} [data-ack-panel="shared"].acknowledged`), 1);
        await click(`${group} [data-ack-panel="shared"] .ack-check input`); await sleep(300);
        assert.equal(await count(`${group} [data-acks-required="1"]`), 1); assert(await blocked(), 'unchecking on Changes blocks the build');
        await click(`${group} [data-ack-panel="shared"] .ack-check input`); await sleep(300);
        assert.equal(await count(`${group} [data-acks-required]`), 0); assert(!(await blocked()));
    }
    console.log('weapon layout smoke passed');
} finally { socket.close(); }
