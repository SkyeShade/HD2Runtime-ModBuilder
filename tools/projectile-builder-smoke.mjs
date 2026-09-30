// Developer-only smoke test for the 0.28.0 projectile builder (unified projectile hosts, row slots, mode presentation) against a running
// isolated app (WebView2 CDP) bound to the HD2Runtime 0.28.0 SDK. Launch the app with HD2RUNTIMEGUI_DATA_ROOT=<empty folder>,
// --sdk-path <extracted sdk-0.28.0> and WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9263, then:
// HD2GUI_CDP_PORT=9263 node tools/projectile-builder-smoke.mjs
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
const port = process.env.HD2GUI_CDP_PORT ?? 9263;
let pages;
for (let attempt = 0; attempt < 120; attempt++) {
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
const go = async target => { await evaluate(`document.querySelector('[data-nav="${target}"]').click()`); await sleep(450); };
const count = selector => evaluate(`document.querySelectorAll(${JSON.stringify(selector)}).length`);
const q = selector => `document.querySelector(${JSON.stringify(selector)})`;
const click = async selector => { await evaluate(`(() => { const el = ${q(selector)}; if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.click(); })()`); await sleep(500); };
const fill = async (selector, value) => { await evaluate(`(() => { const el = ${q(selector)}; if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.value = ${JSON.stringify(value)}; el.dispatchEvent(new Event('input', {bubbles: true})); el.dispatchEvent(new Event('change', {bubbles: true})); })()`); await sleep(650); };
const scroll = async selector => { await evaluate(`(() => { ${q(selector)}.scrollIntoView({block: 'start'}); const c = document.querySelector('.workspace-content'); if (c) c.scrollTop -= 110; })()`); await sleep(300); };
const innerText = selector => evaluate(`${q(selector)}?.innerText ?? ''`);
const out = id => 'output/v1/projectile/' + id;
const lua = async () => { await go('lua'); await sleep(400); const t = await evaluate('document.body.innerText'); return t; };
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')", 'startup');
    await go('library'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Create New Mod')).click()"); await waitFor(q('#mod-name'), 'create dialog');
    await fill('#mod-name', 'Projectile Smoke'); await fill('#author', 'Tests');
    await evaluate("document.querySelector('.dialog form button[type=submit]').click()");
    await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')", 'project created');
    assert.equal(await count('[data-nav="projectiles"]'), 1, 'Projectile builder destination');

    // Hosts: every published projectile source, writable or read-only.
    await go('projectiles'); await waitFor(q('[data-projectile-builder]'), 'projectile builder');
    const hosts = await count('[data-builder-host]'); assert(hosts >= 90, 'every projectile source listed: ' + hosts);
    await fill('[data-builder-search]', 'EAT-17'); await click('[data-builder-host="EAT-17 Expendable Anti-Tank"]');
    await waitFor(q('[data-projectile-host="EAT-17 Expendable Anti-Tank"] [data-donor-list]'), 'EAT-17 donors');
    await click(`[data-projectile-host="EAT-17 Expendable Anti-Tank"] [data-donor="${out('plas-1-scorcher')}"]`);
    await waitFor(`${q('[data-projectile-host="EAT-17 Expendable Anti-Tank"]')}.classList.contains('modified')`, 'EAT-17 swapped');
    assert.equal(await count('[data-projectile-host="EAT-17 Expendable Anti-Tank"] [data-opt-in]'), 0, 'live-proven support pair: no opt-in warnings');
    await screenshot('projectile-host-support');
    await click(`[data-projectile-host="EAT-17 Expendable Anti-Tank"] [data-donor="${out('eat-411-leveller')}"]`);
    await waitFor(q('[data-projectile-host="EAT-17 Expendable Anti-Tank"] [data-opt-in="allow_unverified_effect"]'), 'unproven support pair warns');

    // A read-only host keeps Runtime's reason.
    await fill('[data-builder-search]', 'AC-8'); await click('[data-builder-host="AC-8 Autocannon"]');
    await waitFor(q('[data-host-readonly]'), 'AC-8 read-only'); assert.match(await innerText('[data-host-readonly]'), /WeaponRounds/);
    await screenshot('projectile-host-readonly');

    // Mounted host, then the donor row it fires (a row write never follows the swap).
    const patriot = 'EXO-45 Patriot Exosuit / right_gun';
    await fill('[data-builder-search]', 'Patriot'); await click(`[data-builder-host="${patriot}"]`);
    await waitFor(q(`[data-projectile-host="${patriot}"] [data-donor-list]`), 'Patriot donors');
    await click(`[data-projectile-host="${patriot}"] [data-donor="${out('las-58-talon')}"]`);
    await waitFor(q(`[data-projectile-host="${patriot}"] [data-open-donor-row]`), 'donor row link');
    await click(`[data-projectile-host="${patriot}"] [data-open-donor-row]`);
    const talon = `[data-row-editor="${out('las-58-talon')}"]`;
    await waitFor(q(talon), 'Talon row editor'); assert.equal(await count(talon + ' [data-donor-row-context]'), 1);
    await fill(talon + ' [data-slot-select="impactExplosion"]', out('gl-21-grenade-launcher') + '#impactExplosion');
    await waitFor(`${q(talon + ' [data-slot="impactExplosion"]')}.classList.contains('modified')`, 'Talon impact slot written');
    assert.equal(await count(talon + ' [data-slot-proven]'), 1, 'donor-row tuple is live-proven');
    assert.equal(await count(talon + ' [data-row-flight] .field-row'), 3, 'velocity, drag and penetration slowdown');
    await scroll(talon); await screenshot('projectile-donor-row');

    // Rows: the Speargun spare twin with STUN and the auto icon; the MA5C flight values.
    await click('[data-builder-tab="rows"]');
    await fill('[data-builder-search]', 'spare twin'); await click(`[data-builder-row="${out('s-11-speargun-spare-twin')}"]`);
    const twin = `[data-mode-presentation="${out('s-11-speargun-spare-twin')}"]`;
    await waitFor(q(twin), 'mode presentation');
    await fill(twin + ' [data-mode-label]', 'stun'); await fill(twin + ' [data-mode-icon]', 'auto');
    await waitFor(`${q(twin)}.classList.contains('modified')`, 'label and icon');
    assert.equal(await count(twin + ' [data-opt-in]'), 0, 'live-proven STUN and its auto icon');
    await scroll(twin); await screenshot('projectile-mode-presentation');
    await fill('[data-builder-search]', 'MA5C'); await click(`[data-builder-row="${out('ma5c-assault-rifle')}"]`);
    await waitFor(q(`[data-row-editor="${out('ma5c-assault-rifle')}"] [data-object-field="projectile.penetration_slowdown"]`), 'MA5C penetration slowdown');

    // Outputs no attack can fire.
    await click('[data-builder-tab="other"]'); await fill('[data-builder-search]', 'LAS-98');
    await click('[data-builder-unavailable="output/v1/beam/las-98-laser-cannon"]');
    await waitFor(q('[data-builder-unavailable-detail]'), 'LAS-98 beam'); assert.match(await innerText('[data-builder-unavailable-detail]'), /INCOMPATIBLE_OUTPUT_FAMILY/);
    await screenshot('projectile-unavailable-beam');

    // Player weapons keep their selector (now with the shared donor list and row links).
    await go('player-weapons'); await fill('#weapon-search', 'Reprimand'); await click('[data-weapon="SMG-32 Reprimand"]');
    await evaluate(`document.querySelector('[data-composition-attack="primary"]').open = true`); await sleep(400);
    await waitFor(q('[data-attack-output="primary"] [data-donor-list]'), 'player donor list');
    assert.equal(await count('[data-attack-output="primary"] [data-open-own-row]'), 1, 'own row link');

    const text = await lua();
    for (const expected of ["hd2.support_weapon('EAT-17 Expendable Anti-Tank'):attack('primary')", "hd2.vehicle('EXO-45 Patriot Exosuit'):weapon('right_gun'):attack('primary'):projectile_source().target",
        `hd2.attack_output('${out('las-58-talon')}')`, "value='stun'", "value='auto'"])
        assert(text.includes(expected), 'Lua contains ' + expected);
    await go('changes'); await waitFor(q('[data-projectile-builder-changes]'), 'changes list');
    assert(await count('[data-row-change]') >= 3); await screenshot('projectile-changes');
    console.log('projectile builder smoke: OK');
} finally { socket.close(); }
