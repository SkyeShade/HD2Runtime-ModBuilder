// Developer-only smoke test for the information-architecture pass (support merging, compact rows, game icons) against a running
// isolated MAUI app (WebView2 CDP). Import icons first (dotnet run --project tools/HD2RuntimeGUI.Sample -- <root> --import-icons),
// launch Debug with HD2RUNTIMEGUI_DATA_ROOT=<root> and WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9237,
// then run: HD2GUI_CDP_PORT=9237 node tools/ia-smoke.mjs
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
const shot = async (selector, name) => { await evaluate(`document.querySelector(${JSON.stringify(selector)})?.scrollIntoView({block: 'start'})`); await sleep(250); await screenshot(name); };
const stratagem = async (category, name) => { await go('stratagems:' + category); await click(`[data-stratagem="${name}"]`); await waitFor(`document.querySelector('[data-stratagem-header="${name}"]')`, 'stratagem ' + name); };
const UNLINKED = ['B/MD C4 Pack', 'CQC-72 Entrenchment Tool', 'SG-88 Break-Action Shotgun'];
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')", 'startup');
    // Sidebar: no leftover Support Weapons category; Boosters stays yellow.
    assert.equal(await count('[data-nav="support"]'), 0);
    assert.deepEqual(await evaluate(`[...document.querySelectorAll('.nav-group:not(.research) [data-nav]')].map(b => b.dataset.nav)`),
        ['overview', 'player-weapons', 'stratagems', 'stratagems:support', 'stratagems:offensive', 'stratagems:defensive', 'vehicles', 'backpacks', 'boosters', 'changes', 'lua', 'export']);
    assert.equal(await evaluate(`getComputedStyle(document.querySelector('[data-nav="boosters"] .cat-dot')).backgroundColor`), 'rgb(242, 201, 76)');
    await evaluate(`[...document.querySelectorAll('.nav-item')].find(b => b.innerText.includes('Settings')).click()`); await sleep(500);
    assert((await evaluate("document.querySelector('[data-icon-status]').innerText")).includes('18 of 20 boosters'));
    await shot('[data-game-icons]', 'ia-settings-icons');

    await go('library'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Create New Mod')).click()"); await waitFor("document.querySelector('#mod-name')", 'create dialog');
    await fill('#mod-name', 'IA Smoke'); await fill('#author', 'Tests');
    await evaluate("document.querySelector('.dialog form button[type=submit]').click()");
    await waitFor("document.body.innerText.includes('Project overview') && !document.querySelector('.activity')", 'project created');

    // Support: merged call-in + equipment, one conceptual entry; unresolved items only in the small unlinked group.
    await go('stratagems:support');
    assert.equal(await count('[data-unlinked-support-group]'), 1);
    assert.deepEqual(await evaluate(`[...document.querySelectorAll('[data-unlinked-support]')].map(b => b.dataset.unlinkedSupport)`), UNLINKED);
    assert.equal(await count('.weapon-list [data-stratagem] .game-icon'), await count('.weapon-list [data-stratagem]'));
    assert.equal(await count('[data-unlinked-support="MG-43 Machine Gun"]'), 0);

    await stratagem('support', 'MG-43 Machine Gun');
    for (const section of ['call-in', 'delivered', 'advanced']) assert.equal(await count(`[data-section="${section}"]`), 1, section);
    assert.equal(await evaluate(`document.querySelector('[data-section="advanced"]').open`), false);
    assert.equal(await evaluate(`document.querySelector('[data-support-inspection]').open`), false);
    assert.equal(await count('[data-section="delivered"] [data-support-weapon="MG-43 Machine Gun"]'), 1);
    const groups = await evaluate(`[...document.querySelectorAll('[data-section="delivered"] .group-title')].map(h => h.textContent.trim())`);
    for (const g of ['Ammo', 'Handling', 'Firing', 'Projectile', 'Damage']) assert(groups.includes(g), g + ' in ' + groups);
    assert.equal(await count('[data-section="delivered"] .stat-card'), 0); assert(await count('[data-section="delivered"] .field-row') > 20);
    // Safety stays visible as compact badges; provenance is one click away.
    const reload = '[data-support-field][data-semantic-field="reload.duration"]';
    assert.equal(await count(`${reload} [data-flag="allow_unverified_effect"]`), 1); assert.equal(await count(`${reload} details.field-info`), 1);
    const baseline = await evaluate(`document.querySelector('${reload} input[type=number]').value`);
    await fill(`${reload} input[type=number]`, String(Number(baseline) + 1));
    await waitFor(`document.querySelector('${reload} [data-support-effect-ack]')`, 'reload acknowledgement'); await click(`${reload} [data-support-effect-ack]`);
    await shot('[data-stratagem-header]', 'ia-support-mg43');
    await evaluate(`document.querySelector('${reload}').scrollIntoView({block: 'center'})`); await sleep(250); await screenshot('ia-support-mg43-ammo');

    // Unlinked equipment: explicitly marked, controls and blockers exactly as published.
    await go('stratagems:support'); await click('[data-unlinked-support="CQC-72 Entrenchment Tool"]');
    await waitFor(`document.querySelector('[data-unlinked-support-detail="CQC-72 Entrenchment Tool"]')`, 'CQC-72');
    assert.equal(await count('[data-unlinked-badge]'), 1); assert.equal(await count('[data-support-identity-blocked]'), 1);
    await screenshot('ia-unlinked-cqc72');

    // Offensive and defensive stratagems use the same header / call-in / delivered / advanced structure.
    await stratagem('offensive', 'Orbital Precision Strike'); assert.equal(await count('[data-section="call-in"]'), 1); await screenshot('ia-offensive-orbital');
    await go('stratagems:defensive');
    const sentry = await evaluate(`[...document.querySelectorAll('[data-stratagem][data-family="sentry"]')].map(b => b.dataset.stratagem)[0]`);
    await stratagem('defensive', sentry); assert.equal(await count('[data-section="delivered"] [data-entity-stats]'), 1); await screenshot('ia-defensive-sentry');

    // Boosters: real game icons where the published identity names one, glyphs otherwise.
    await go('boosters');
    assert.equal(await count('[data-booster-item] .game-icon img'), 18); assert.equal(await count('[data-booster-item] .game-icon.fallback'), 2);
    await click('[data-booster-item="Armed Resupply Pods"]'); await waitFor(`document.querySelector('[data-booster="Armed Resupply Pods"] .object-header [data-game-icon="BoosterArmedpods"] img')`, 'booster header icon');
    await screenshot('ia-boosters');
    await click('[data-booster-item="Integrated Extinguishers"]'); await waitFor(`document.querySelector('[data-booster="Integrated Extinguishers"] [data-game-icon="fallback"]')`, 'booster fallback');

    // Changes / Lua / export unaffected.
    await go('lua'); const lua = await evaluate('document.querySelector("pre").innerText');
    assert(lua.includes("target=hd2.support_weapon('MG-43 Machine Gun'),")); assert(lua.includes('allow_unverified_effect=true,'));
    await go('changes'); await screenshot('ia-changes');
    await go('export'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Build / Export Mod')).click()"); await waitFor("document.body.innerText.includes('Export complete')", 'export');
    await go('library');
    console.log('PASS: no Support Weapons category, merged support call-in + equipment, 3 unlinked items, grouped compact rows, sections, 18 booster icons + 2 fallbacks, Lua/export unchanged');
} finally { socket.close(); }
