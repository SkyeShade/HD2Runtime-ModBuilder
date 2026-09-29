// Developer-only smoke test for the 0.28.0 integration pass (custom Lua / event scripting, attack outputs, SH-20 shield zone, Resupply,
// developer SDK settings) against a running isolated app (WebView2 CDP) bound to an unreleased HD2Runtime development SDK. Launch the app
// with HD2RUNTIMEGUI_DATA_ROOT=<empty folder>, --sdk-path <HD2Runtime>\sdk and WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9241,
// then: HD2GUI_CDP_PORT=9241 node tools/scripting-smoke.mjs
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
const port = process.env.HD2GUI_CDP_PORT ?? 9241;
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
const go = async target => { await evaluate(`document.querySelector('[data-nav="${target}"]').click()`); await sleep(450); };
const count = selector => evaluate(`document.querySelectorAll(${JSON.stringify(selector)}).length`);
const q = selector => `document.querySelector(${JSON.stringify(selector)})`;
const click = async selector => { await evaluate(`(() => { const el = ${q(selector)}; if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.click(); })()`); await sleep(450); };
const fill = async (selector, value) => { await evaluate(`(() => { const el = ${q(selector)}; if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.value = ${JSON.stringify(value)}; el.dispatchEvent(new Event('input', {bubbles: true})); el.dispatchEvent(new Event('change', {bubbles: true})); })()`); await sleep(550); };
const scroll = async selector => { await evaluate(`(() => { ${q(selector)}.scrollIntoView({block: 'start'}); const c = document.querySelector('.workspace-content'); if (c) c.scrollTop -= 110; })()`); await sleep(300); };
const innerText = selector => evaluate(`${q(selector)}?.innerText ?? ''`);
const text = () => evaluate('document.body.innerText');
const editor = '[data-lua-input]';
const editorText = () => evaluate(`${q(editor)}.value`);
// Types at the end of the editor the way a keyboard does (an input event after the change), so completion and diagnostics react.
const type = async value => { await evaluate(`(() => { const t = ${q(editor)}; t.focus(); t.setSelectionRange(t.value.length, t.value.length); document.execCommand('insertText', false, ${JSON.stringify(value)}); })()`); await sleep(500); };
const NAPALM = 'output/v1/projectile/eat-700-expendable-napalm';
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')", 'startup');
    await go('library'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Create New Mod')).click()"); await waitFor(q('#mod-name'), 'create dialog');
    await fill('#mod-name', 'Scripting Smoke'); await fill('#author', 'Tests');
    await evaluate("document.querySelector('.dialog form button[type=submit]').click()");
    await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')", 'project created');

    // Custom Lua: add, the editor mounts with highlighting and a gutter.
    await go('scripting'); await waitFor(q('[data-custom-lua-add]'), 'add custom Lua'); await click('[data-custom-lua-add]');
    await waitFor(`${q(editor)} && ${q('.lua-gutter [data-line]')}`, 'editor mounted');
    assert((await editorText()).includes('local mod = hd2.mod()'), 'starter text');
    assert(await count('.lua-highlight .lua-hd2') > 0, 'hd2 highlighted');
    assert.equal(await innerText('[data-custom-lua-state]'), 'Saved');
    const path = await evaluate(`${q('.custom-lua-file small')}.title`);
    assert.equal((await fs.readFile(path, 'utf8')).replace(/\r\n/g, '\n'), (await editorText()).replace(/\r\n/g, '\n'), 'working copy written');

    // Snippet insertion, then Save (Ctrl+S path is the same handler).
    await click('[data-snippet-insert="player-death-explosion"]');
    assert((await editorText()).includes("hd2.explosions.spawn('Hellbomb', {position = event.position})"), 'snippet inserted');
    await waitFor(`${q('[data-custom-lua-state]')}.innerText === 'Unsaved changes'`, 'dirty state');
    await waitFor(q('[data-custom-lua-clean]'), 'no diagnostics');
    await screenshot('custom-lua-editor');
    await click('[data-custom-lua-save]'); await waitFor(`${q('[data-custom-lua-state]')}.innerText === 'Saved'`, 'saved');
    assert((await fs.readFile(path, 'utf8')).includes("'Hellbomb'"), 'saved to src/addon.lua');

    // Autocomplete from the SDK stub and catalog.
    await type('\nhd2.ev'); await waitFor(`!${q('.lua-popup')}.hidden`, 'completion popup');
    assert(await evaluate(`[...document.querySelectorAll('.lua-completion-name')].some(n => n.innerText === 'events')`), 'hd2.events offered');
    await evaluate(`${q(editor)}.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape', bubbles: true}))`);
    await type("ents.on('player_d"); await waitFor(`!${q('.lua-popup')}.hidden`, 'event name completion');
    assert(await evaluate(`[...document.querySelectorAll('.lua-completion-name')].some(n => n.innerText === 'player_died')`), 'event names offered');
    await screenshot('custom-lua-autocomplete');
    await evaluate(`${q(editor)}.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape', bubbles: true}))`);

    // Diagnostics: an unknown event warns, a syntax error errors with a gutter marker.
    await type("yed', function() end)\n"); await waitFor(q('[data-lua-diagnostic="warning"]'), 'unknown event warning');
    await type('if true then\n'); await waitFor(`${q('[data-lua-diagnostic="error"]')} && ${q('.lua-gutter .lua-line-error')}`, 'syntax error');
    await screenshot('custom-lua-diagnostics');
    // Reload from disk discards the unsaved edits.
    await click('[data-custom-lua-reload]'); await waitFor(`${q('[data-custom-lua-state]')}.innerText === 'Saved'`, 'reloaded');
    assert(!(await editorText()).includes('player_dyed'), 'unsaved text replaced by the saved file');

    // An outside edit: the page notices it and asks before anything is overwritten.
    const outside = (await fs.readFile(path, 'utf8')) + "\n-- edited outside ModBuilder\n";
    await fs.writeFile(path, outside);
    await waitFor(q('[data-custom-lua-conflict]'), 'external change detected');
    await screenshot('custom-lua-external-change');
    await click('[data-custom-lua-take-disk]'); await waitFor(`!${q('[data-custom-lua-conflict]')}`, 'conflict resolved');
    assert((await editorText()).includes('-- edited outside ModBuilder'), 'editor has the outside text');

    // Reference and actions, generated from the SDK.
    await click('[data-custom-lua-tab="reference"]'); await waitFor(q('[data-reference-event="player_died"]'), 'event reference');
    assert.equal(await count('[data-event-blocked]'), 1, 'blocked event marked');
    await evaluate(`${q('[data-reference-event="player_died"]')}.open = true`); await sleep(200);
    assert(await count('[data-reference-event="player_died"] [data-payload-field="position"]') === 1, 'payload fields listed');
    await screenshot('custom-lua-reference');
    await click('[data-custom-lua-tab="actions"]'); await waitFor(q('[data-action-explosion="NUX-223 Hellbomb"]'), 'action picker');
    assert.equal(await count('[data-action-explosion="NUX-223 Hellbomb"] [data-live-proven]'), 1, 'Hellbomb live-proven');
    assert.equal(await count('[data-action-explosion="B-100 Portable Hellbomb"] [data-live-proven]'), 0, 'portable Hellbomb not live-proven');
    await click('[data-action-status="fire"] [data-action-insert]');
    assert((await editorText()).includes("hd2.status.apply(event.entity, 'fire', {buildup = 100})"), 'status inserted');
    await screenshot('custom-lua-actions');
    await click('[data-custom-lua-save]'); await waitFor(`${q('[data-custom-lua-state]')}.innerText === 'Saved'`, 'saved again');

    // AR-23 Liberator: donors through its ammunition (INDIRECT), proven pairs marked; choosing one writes the ammunition source.
    await go('player-weapons'); await click('[data-weapon="AR-23 Liberator"]'); await waitFor(q('[data-authoring-summary="AR-23 Liberator"]'), 'Liberator editor');
    await evaluate(`${q('[data-composition-attack="primary"]')}.open = true`); await sleep(300);
    await waitFor(q('[data-attack-output="primary"] [data-projectile-source="INDIRECT"]'), 'active source shown');
    assert.equal(await count('[data-ammunition-source]'), 1, 'ammunition named');
    assert.equal(await count(`[data-donor="${NAPALM}"] [data-donor-proven]`), 1, 'EAT-700 proven on the Liberator');
    await click(`[data-donor="${NAPALM}"]`); await waitFor(`${q(`[data-donor="${NAPALM}"]`)}.classList.contains('active')`, 'donor chosen');
    assert.equal(await count('[data-opt-in="allow_shared"]'), 1, 'shared opt-in explained');
    await scroll('[data-attack-output="primary"]'); await screenshot('attack-output-liberator');

    // SH-20: the shield zone armor is editable and named; the default-zone armor is read-only and points at it.
    await go('stratagems:support'); await click('[data-family-tab="backpack"]'); await click('[data-stratagem="SH-20 Ballistic Shield Backpack"]');
    await waitFor(q('[data-backpack="SH-20 Ballistic Shield Backpack"]'), 'SH-20 editor');
    assert.equal(await count('[data-field-dormant]'), 1, 'dormant armor marked'); assert.equal(await count('[data-field-active-instead]'), 1, 'active field named');
    assert((await innerText('[data-semantic-field="zone.armor"]')).includes('Shield zone · Armor'), 'zone field names its zone');
    await fill('[data-semantic-field="zone.armor"] input', '6');
    await waitFor(`${q('[data-semantic-field="zone.armor"]')}.classList.contains('modified')`, 'zone armor edited');
    await scroll('[data-backpack-group="Shield Plate"]'); await screenshot('backpack-sh20-shield-zone');

    // Resupply: Mission category, drop pod slots with the live-verified Grenade Box.
    await go('stratagems:mission'); await click('[data-stratagem="Resupply"]'); await waitFor(q('[data-stratagem-header="Resupply"]'), 'Resupply');
    await waitFor(q('[data-section="drop-pod"] [data-pod-slot="1"]'), 'drop pod editor');
    const grenade = await evaluate(`[...${q('[data-pod-slot="1"] [data-pod-slot-select]')}.options].find(o => o.text.startsWith('Grenade Box')).value`);
    await fill('[data-pod-slot="1"] [data-pod-slot-select]', grenade);
    await waitFor(q('[data-pod-slot="1"] [data-pickup-badge="live-pair"]'), 'live-verified pair badge');
    await scroll('[data-section="drop-pod"]'); await screenshot('resupply-drop-pod');

    // Changes and Lua.
    assert(!(await text()).includes('Build requires review'), 'build is valid');
    await go('changes'); await waitFor(q('[data-attack-output-change="primary"]'), 'attack output on Changes');
    assert.equal(await count('[data-custom-lua-change]'), 1, 'custom Lua on Changes');
    await go('lua'); const lua = await evaluate('document.querySelector("pre").innerText');
    for (const s of ["hd2.weapon('AR-23 Liberator'):ammunition()", 'hd2.fields.ammunition.projectile', `hd2.attack_output('${NAPALM}')`,
        "hd2.backpack('SH-20 Ballistic Shield Backpack'):damage_zone('zone_0')", "hd2.pod_rack(", '-- Custom Lua: src/addon.lua', 'addon()'])
        assert(lua.includes(s), 'Lua contains ' + s);

    // Settings: the local SDK and its Runtime commit.
    await evaluate(`[...document.querySelectorAll('.nav-item')].find(b => b.innerText.includes('Settings')).click()`); await sleep(500);
    await waitFor(q('[data-local-sdk-settings]'), 'developer SDK panel');
    assert.equal(await innerText('[data-local-sdk-mode]'), 'Local SDK this run');
    await scroll('[data-local-sdk-settings]'); await screenshot('settings-local-sdk');
    console.log('PASS: custom Lua (editor, snippets, completion, diagnostics, reload, outside edit, reference, actions), attack outputs, SH-20 zone, Resupply, Changes, Lua, settings');
} finally { socket.close(); }
