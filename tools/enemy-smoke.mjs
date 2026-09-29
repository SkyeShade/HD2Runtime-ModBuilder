// Developer-only smoke test for enemy / structure authoring against a running isolated app (WebView2 CDP) bound to an unreleased
// HD2Runtime development SDK. Launch the app with HD2RUNTIMEGUI_DATA_ROOT=<empty folder>, --sdk-path <HD2Runtime>\sdk and
// WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9240, then: HD2GUI_CDP_PORT=9240 node tools/enemy-smoke.mjs
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
const go = async target => { await evaluate(`document.querySelector('[data-nav="${target}"]').click()`); await sleep(450); };
const count = selector => evaluate(`document.querySelectorAll(${JSON.stringify(selector)}).length`);
const q = selector => `document.querySelector(${JSON.stringify(selector)})`;
const click = async selector => { await evaluate(`(() => { const el = ${q(selector)}; if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.click(); })()`); await sleep(450); };
const fill = async (selector, value) => { await evaluate(`(() => { const el = ${q(selector)}; if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.value = ${JSON.stringify(value)}; el.dispatchEvent(new Event('input', {bubbles: true})); el.dispatchEvent(new Event('change', {bubbles: true})); })()`); await sleep(550); };
const scroll = async selector => { await evaluate(`(() => { ${q(selector)}.scrollIntoView({block: 'start'}); const c = document.querySelector('.workspace-content'); if (c) c.scrollTop -= 110; })()`); await sleep(300); };
const innerText = selector => evaluate(`${q(selector)}?.innerText ?? ''`);
const text = () => evaluate('document.body.innerText');
const field = (key) => `[data-entity-field=${JSON.stringify(key)}]`;
const CHARGER = 'enemy/v1/terminids/charger', SPEWER = 'enemy/v1/terminids/boomer_burrower', FAB = 'enemy/v1/automatons/spawner_factory_conscript_base';
const key = (sid, path, id, f) => `enemy:${sid}|${path}|${id ?? ''}|${f}`;
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')", 'startup');
    await go('library'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Create New Mod')).click()"); await waitFor(q('#mod-name'), 'create dialog');
    await fill('#mod-name', 'Enemy Smoke'); await fill('#author', 'Tests');
    await evaluate("document.querySelector('.dialog form button[type=submit]').click()");
    await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')", 'project created');
    assert.equal(await count('[data-nav="enemies"]'), 1, 'Enemies destination'); assert.equal(await count('[data-nav="structures"]'), 1, 'Structures destination');

    // Enemies: every published class listed, grouped by faction; faction filter and search (native names and wiki candidates).
    await go('enemies'); await waitFor(q('[data-enemy-browser="enemy"]'), 'enemy browser');
    assert.equal(await count('[data-enemy-item]'), 138);
    assert.deepEqual(await evaluate(`[...document.querySelectorAll('[data-enemy-faction]')].map(h => h.dataset.enemyFaction)`), ['terminids', 'automatons', 'illuminate']);
    await fill('[data-enemy-faction-filter]', 'illuminate'); assert.equal(await count('[data-enemy-item]'), 25);
    await fill('[data-enemy-faction-filter]', ''); await fill('[data-enemy-search]', 'Hunter');
    assert(await evaluate(`[...document.querySelectorAll('[data-enemy-item]')].some(b => b.dataset.enemyItem === 'enemy/v1/terminids/hunter_tier_1')`), 'native class found by wiki candidate');
    await screenshot('enemy-search-native');
    await fill('[data-enemy-search]', 'charger'); await click(`[data-enemy-item="${CHARGER}"]`);
    await waitFor(q(`[data-enemy="${CHARGER}"]`), 'Charger editor');
    assert.equal(await innerText('[data-enemy-name]'), 'Charger'); assert.equal(await innerText('[data-enemy-class]'), 'charger');

    // General: main health.
    await fill(field(key(CHARGER, 'entity', null, 'entity.health')) + ' input', '1200');
    await waitFor(`${q(field(key(CHARGER, 'entity', null, 'entity.health')))}.classList.contains('modified')`, 'main health edit');
    // Body zones: the table lists all 17; selecting the head shows its fields.
    assert.equal(await count('[data-zone-table] tbody tr'), 17);
    await click('[data-zone="zone_0"]'); await waitFor(q('[data-zone-detail="zone_0"]'), 'head zone detail');
    await fill(field(key(CHARGER, 'damage_zone', 'zone_0', 'zone.armor')) + ' input', '1');
    await waitFor(`${q(field(key(CHARGER, 'damage_zone', 'zone_0', 'zone.armor')))}.classList.contains('modified')`, 'head armor edit');
    assert((await innerText('[data-zone="zone_0"]')).includes('1'), 'zone table shows the edited armor');
    // Underside uses the main health pool: read-only.
    await click('[data-zone="zone_3"]'); await waitFor(q('[data-zone-main-pool]'), 'main pool note');
    assert((await innerText(field(key(CHARGER, 'damage_zone', 'zone_3', 'zone.health')))).includes('Read-only'));
    await click('[data-zone="zone_0"]'); await scroll('[data-enemy-section="zones"]'); await screenshot('enemy-charger-zones');

    // Attacks: Rupture Spewer, Bile Bombard (direct hit) and its projectile / explosion rows.
    await fill('[data-enemy-search]', 'Bile Bombard'); await click(`[data-enemy-item="${SPEWER}"]`); await waitFor(q(`[data-enemy="${SPEWER}"]`), 'Spewer editor');
    assert.equal(await count('[data-enemy-attack]'), 4);
    await click('[data-enemy-attack="slot_1"]'); await waitFor(q('[data-attack-detail="slot_1"]'), 'attack detail');
    assert((await innerText('[data-attack-detail="slot_1"]')).includes('Bile Bombard'));
    await fill(field(key(SPEWER, 'attack', 'slot_1', 'damage.standard_damage')) + ' input', '50');
    await waitFor(q('[data-enemy-shared="slot_1"]'), 'shared row warning'); await waitFor(q(`[data-enemy-unverified="${SPEWER}"]`), 'unverified warning');
    await click('[data-enemy-attack="slot_1_projectile"]'); await fill(field(key(SPEWER, 'attack', 'slot_1_projectile', 'projectile.velocity')) + ' input', '45');
    await click('[data-enemy-attack="slot_1_impact_explosion"]'); await fill(field(key(SPEWER, 'attack', 'slot_1_impact_explosion', 'explosion.inner_radius')) + ' input', '2');
    await click('[data-enemy-attack="slot_1"]'); await scroll('[data-enemy-section="attacks"]'); await screenshot('enemy-spewer-attacks');

    // Structures: same editor, hd2.structure.
    await go('structures'); await waitFor(q('[data-enemy-browser="structure"]'), 'structure browser');
    assert.equal(await count('[data-enemy-item]'), 39);
    await fill('[data-enemy-search]', 'spawner_factory_conscript_base'); await click(`[data-enemy-item="${FAB}"]`);
    await fill(field(key(FAB, 'entity', null, 'entity.health').replace('enemy:', 'structure:')) + ' input', '150');
    await waitFor(q(`[data-enemy-unverified="${FAB}"]`), 'structure health gate warning'); await screenshot('structure-fabricator');

    // Changes and Lua.
    assert(!(await text()).includes('Build requires review'), 'build is valid');
    await go('changes'); await waitFor(q('[data-entity-change-group="enemy:' + CHARGER + '"]'), 'Charger change group');
    assert.equal(await count('[data-entity-change-group^="enemy:"]'), 2); assert.equal(await count('[data-entity-change-group^="structure:"]'), 1);
    await go('lua'); const lua = await evaluate('document.querySelector("pre").innerText');
    for (const s of ["hd2.enemy('charger')", "hd2.enemy('charger'):zone('zone_0')", "hd2.enemy('boomer_burrower'):attack('slot_1')", "hd2.enemy('boomer_burrower'):attack('slot_1_projectile')",
        "hd2.enemy('boomer_burrower'):attack('slot_1_impact_explosion')", "hd2.structure('spawner_factory_conscript_base')", 'allow_shared=true', 'allow_unverified_effect=true'])
        assert(lua.includes(s), 'Lua contains ' + s);
    console.log('PASS: enemy and structure authoring (catalog, filter/search, main, zones, attacks, structures, Changes, Lua)');
} finally { socket.close(); }
