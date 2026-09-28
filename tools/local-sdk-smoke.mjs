// Local unpublished SDK smoke (e.g. HD2Runtime 0.25.0 before release). Launch the app on a COPY of a data root with
// --sdk-path <HD2Runtime>\sdk (or HD2RUNTIME_SDK_PATH) and WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=<port>,
// then: HD2GUI_CDP_PORT=<port> node tools/local-sdk-smoke.mjs
// It creates a project, and rebinds the 0.24.0 "liberatordamage" sample to the local SDK.
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
const openProject = async name => { await go('library'); await evaluate(`[...document.querySelectorAll('.project-open')].find(b => b.innerText.includes(${JSON.stringify(name)})).click()`); await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')", 'open ' + name); };
const report = {};
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')", 'startup');
    // 1. Settings clearly shows the local SDK; GitHub verification is skipped.
    await evaluate(`[...document.querySelectorAll('.nav-item')].find(b => b.innerText.includes('Settings')).click()`); await sleep(500);
    assert.equal(await evaluate("document.querySelector('[data-installed-sdk]').innerText"), 'Local SDK 0.25.0');
    assert.equal(await evaluate("document.querySelector('[data-sdk-source]').innerText"), 'Local SDK · development');
    report.localSource = await evaluate("document.querySelector('[data-local-sdk]').dataset.localSdk");
    assert((await text()).includes('GitHub release checks are skipped for this run'));
    assert((await evaluate("document.querySelector('.sdk-mini').innerText")).includes('Local SDK'));
    await screenshot('local025-settings');

    // 2. Fresh project on the local SDK, without an update prompt.
    await go('library'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Create New Mod')).click()"); await waitFor("document.querySelector('#mod-name')", 'create dialog');
    assert(!(await text()).includes('UPDATE AVAILABLE'));
    const name = 'Local025 ' + Date.now(); await fill('#mod-name', name); await fill('#author', 'Tests');
    await evaluate("document.querySelector('.dialog form button[type=submit]').click()");
    await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')", 'project created');

    // 3. Boosters: 20 listed, 19 writable; range-checked tuning; one acknowledgement; Lua.
    await go('boosters'); await sleep(400);
    report.boosters = { listed: await count('[data-booster-item]'), writable: await count('[data-booster-item][data-booster-writable="true"]'),
        iconImages: await count('[data-booster-item] .game-icon img'), iconFallbacks: await count('[data-booster-item] .game-icon.fallback') };
    assert.equal(report.boosters.listed, 20); assert.equal(report.boosters.writable, 19);
    await click('[data-booster-item="Hellpod Space Optimization"]');
    assert.equal(await count('[data-booster-target] input[type=number]'), 0, 'Hellpod Space Optimization stays read-only');
    await click('[data-booster-item="Vitality Enhancement"]');
    const vit = '[data-booster-target="tuning"] [data-semantic-field="booster.damage_taken_scale"]';
    assert.equal(await count(vit), 1); assert.equal(await evaluate(`document.querySelector('${vit} input').max`), '4');
    await fill(`${vit} input`, '5'); assert((await text()).includes('must be between 0 and 4'), 'out-of-range value rejected');
    await fill(`${vit} input`, '0.5'); await waitFor(`document.querySelector('${vit}').classList.contains('modified')`, 'vitality autosave');
    await waitFor("document.querySelector('[data-booster-ack]')", 'booster acknowledgement');
    await click('[data-booster-ack]'); await sleep(300);
    await evaluate(`document.querySelector('${vit}').scrollIntoView({block:'center'})`); await sleep(600); await screenshot('local025-booster-vitality');
    await click('[data-booster-item="Firebomb Hellpods"]');
    report.firebombTargets = await evaluate(`[...document.querySelectorAll('[data-booster-target]')].map(s => s.dataset.boosterTarget)`);
    await click('[data-booster-item="Surplus EAT Allocation"]');
    assert.equal(await count('[data-booster-target="granted_stratagem"] [data-semantic-field="stratagem.max_uses"] input'), 1);
    const script = await lua();
    assert(script.includes("target=hd2.booster('Vitality Enhancement'):tuning(),")); assert(script.includes('allow_unverified_effect=true'));
    assert(script.includes('field=hd2.fields.booster.damage_taken_scale,')); assert(script.includes('value=0.5,'));

    // 4. Support linkage: C4 merged with its call-in; SG-88 / CQC-72 standalone (no_call_in); nothing unresolved or duplicated.
    await go('stratagems:support'); await sleep(400);
    assert.equal(await count('[data-unlinked-support-group]'), 0, 'no unlinked support equipment');
    assert.equal(await count('[data-standalone-support-group]'), 1); assert.equal(await count('[data-standalone-support]'), 2);
    for (const item of ['SG-88 Break-Action Shotgun', 'CQC-72 Entrenchment Tool'])
    { assert.equal(await count(`[data-stratagem="${item}"]`), 0, item + ' is not a stratagem'); assert.equal(await count(`[data-standalone-support="${item}"]`), 1); }
    assert.equal(await count('[data-stratagem="B/MD C4 Pack"]'), 1);
    report.support = { listedStratagems: await count('.weapon-list [data-stratagem]'), standalone: await count('[data-standalone-support]'),
        iconImages: await count('.weapon-list .game-icon img'), iconFallbacks: await count('.weapon-list .game-icon.fallback') };
    await click('[data-stratagem="B/MD C4 Pack"]'); await sleep(500);
    assert.equal(await count('[data-section="delivered"] [data-support-weapon]'), 1, 'C4 equipment merged into its call-in');
    await screenshot('local025-support-c4');
    await click('[data-standalone-support="SG-88 Break-Action Shotgun"]'); await sleep(400);
    assert((await evaluate("document.querySelector('[data-standalone-badge]').innerText")).includes('World Pickup'));
    assert.equal(await count('[data-no-call-in-reason]'), 1); await screenshot('local025-support-sg88');
    report.stratagemIconsNote = await evaluate("document.querySelector('[data-stratagem-icons-unpublished]')?.innerText ?? null");

    // 5. Changes page, export, save/reload.
    await go('changes'); await sleep(300); assert.equal(await count('[data-entity-change-group="booster:Vitality Enhancement"]'), 1);
    await go('export'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Build / Export Mod')).click()"); await waitFor("document.body.innerText.includes('Export complete')", 'export');
    const before = await lua(); await openProject(name); assert.equal(await lua(), before, 'reload keeps the generated Lua');

    // 6. Existing 0.24.0 project: opens unchanged, then rebinds explicitly to the local SDK.
    await openProject('liberatordamage');
    const oldLua = await lua(); await go('overview'); await sleep(300);
    assert.equal(await count('[data-rebind-panel]'), 1); assert.equal(await count('[data-local-rebind-warning]'), 1);
    await screenshot('local025-rebind-offer');
    await click('[data-rebind]'); await waitFor("!document.querySelector('.activity')", 'rebind'); await sleep(600);
    report.rebind = { alerts: await evaluate("[...document.querySelectorAll('[role=alert]')].map(e => e.innerText).join(' | ')"), panelGone: (await count('[data-rebind-panel]')) === 0 };
    const newLua = await lua();
    report.rebind.luaIdenticalApartFromVersion = newLua.replace(/0\.2[45]\.0/g, 'X') === oldLua.replace(/0\.2[45]\.0/g, 'X');
    report.rebind.luaHasLiberator = newLua.includes('AR-23 Liberator');
    console.log(JSON.stringify(report, null, 1));
    console.log('local 0.25.0 SDK smoke passed');
} finally { socket.close(); }
