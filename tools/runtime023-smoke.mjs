// Developer-only smoke test against a running isolated MAUI app's WebView2 CDP port.
// Node 22+ is used only for UI verification, never by the shipped application.
// Launch Debug with HD2RUNTIMEGUI_DATA_ROOT=<0.23 sample workspace> and
// WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9236, then run: node tools/runtime023-smoke.mjs
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
const go = async page => { await evaluate(`document.querySelector('[data-nav="${page}"]').click()`); await sleep(350); };
const count = selector => evaluate(`document.querySelectorAll(${JSON.stringify(selector)}).length`);
const list = (selector, map) => evaluate(`[...document.querySelectorAll(${JSON.stringify(selector)})].map(${map})`);
const click = async selector => { await evaluate(`(() => { const el=document.querySelector(${JSON.stringify(selector)}); if (!el) throw new Error('Missing '+${JSON.stringify(selector)}); el.click(); })()`); await sleep(350); };
const fill = async (selector, value) => { await evaluate(`(() => { const el=document.querySelector(${JSON.stringify(selector)}); if (!el) throw new Error('Missing '+${JSON.stringify(selector)}); el.value=${JSON.stringify(value)}; el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); })()`); await sleep(350); };
const open = async name => { await go('library'); await evaluate(`[...document.querySelectorAll('.project-open')].find(b=>b.innerText.includes(${JSON.stringify(name)})).click()`); await waitFor("document.body.innerText.includes('Project overview') && !document.querySelector('.activity')",'open '+name); };
const field = (id, scope = '') => `${scope} [data-semantic-field="${id}"]`;
const lua = async () => { await go('lua'); return evaluate('document.querySelector("pre").innerText'); };
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')",'startup');
    await open('AntiTankEmplacement022');
    assert.deepEqual((await list('.sidebar [data-nav]', 'b=>b.dataset.nav')).slice(0, 10), ['library','overview','player-weapons','stratagems','stratagems:support','stratagems:offensive','stratagems:defensive','vehicles','backpacks','support']);
    assert.deepEqual(await list('[data-nav="stratagems"] small, [data-nav="vehicles"] small, [data-nav="backpacks"] small', 'n=>n.innerText'), ['73','11','13']);
    // Stratagems: linked vehicle/backpack call-ins are represented by their Vehicles/Backpacks editors, not listed twice.
    await go('stratagems:support'); assert.equal(await count('[data-entity-callins-moved]'), 1);
    assert.equal(await count('.weapon-list [data-stratagem="TD-220 Bastion MK XVI"]'), 0); assert.equal(await count('.weapon-list [data-stratagem="GR-8 Recoilless Rifle"]'), 1);
    // Shield Generator Relay: physical base, body zone and shield projector are distinct sections.
    await go('stratagems:defensive'); await click('[data-stratagem="FX-12 Shield Generator Relay"]');
    assert((await evaluate("document.querySelector('[data-entity-stats=\"main\"] h2').innerText")).includes('Physical emitter'));
    assert.deepEqual(await list('[data-entity-stats="main"] > .stat-grid [data-semantic-field]', 'n=>n.dataset.semanticField'), ['entity.health','entity.armor','payload.lifetime']);
    assert.deepEqual(await list('[data-entity-zones] [data-zone="zone_0"] [data-semantic-field]', 'n=>n.dataset.semanticField'), ['zone.armor','zone.health','zone.affects_main_health']);
    assert.deepEqual(await list('[data-shield-projector] [data-semantic-field]', 'n=>n.dataset.semanticField'), ['shield.radius','shield.durability']);
    assert.equal(await count('[data-shield-blocked]'), 1); assert.equal(await count('[data-shield-projector] input[type=number]'), 2);
    await fill(field('shield.radius', '[data-shield-projector]') + ' input', '8');
    await waitFor(`document.querySelector('[data-shield-projector] [data-semantic-field="shield.radius"]').classList.contains('modified')`, 'shield autosave');
    await evaluate("document.querySelector('[data-shield-projector]').scrollIntoView({block:'center'})"); await screenshot('runtime023-shield-relay');
    assert((await lua()).includes(":deployed_entity():shield(),"));
    // Vehicles: durability, distinct zones, evidence tiers, call-in in the same editor.
    await go('vehicles'); assert.equal(await count('.weapon-list [data-vehicle-item]'), 11);
    await click('[data-vehicle-item="TD-220 Bastion MK XVI"]');
    assert.equal(await count('[data-callin="TD-220 Bastion MK XVI"] [data-semantic-field="stratagem.cooldown"] input'), 1);
    assert.equal(await count('[data-vehicle-durability] [data-evidence-tier="gameplay_proven"]'), 2);
    assert.equal(await count('[data-vehicle-zones] [data-zone]'), 31);
    await click('[data-vehicle-zones] > summary'); await fill('[data-zone-filter]', 'zone_3');
    assert.deepEqual(await list('[data-vehicle-zones] [data-zone]', 'z=>z.dataset.zone'), ['zone_3','zone_30']);
    await click('[data-zone="zone_3"] > summary');
    await fill(field('zone.affects_main_health', '[data-zone="zone_3"]') + ' input', '0');
    await waitFor(`document.querySelector('[data-zone="zone_3"] [data-semantic-field="zone.affects_main_health"]').classList.contains('modified')`, 'zone autosave');
    await screenshot('runtime023-bastion-zones');
    await go('vehicles'); await click('[data-vehicle-item="M-103 Supply FRV"]');
    assert.equal(await count('[data-mount-kind="non_weapon"] [data-mount-blocked]'), 1); assert.equal(await count('[data-mount-kind="non_weapon"] select'), 0);
    await go('vehicles'); await click('[data-vehicle-item="M-102 Gunner FRV"]');
    const options = await count('[data-mount="slot_0"] select option'); assert(options > 10, 'published replacements: ' + options);
    assert.equal(await count('[data-mount="slot_0"] [data-evidence-tier="live_write_verified"]'), 1);
    const gater = await evaluate(`[...document.querySelectorAll('[data-mount="slot_0"] select option')].find(o=>o.innerText.toLowerCase().includes('gater oil rig turret')).value`);
    await fill('[data-mount="slot_0"] select', gater);
    await waitFor(`!!document.querySelector('[data-mount="slot_0"] [data-residency-warning]')`, 'residency warning');
    assert((await evaluate('document.body.innerText')).includes('Acknowledge the unverified'), 'build blocked until acknowledged');
    await click('[data-mount="slot_0"] [data-reference-ack]');
    await evaluate(`document.querySelector('[data-mount="slot_0"]').scrollIntoView({block:'center'})`); await screenshot('runtime023-mount-swap');
    const swap = await lua(); assert(swap.includes("hd2.vehicle('M-102 Gunner FRV'):mount('slot_0')")); assert(swap.includes('allow_unverified_reference=true')); assert(swap.includes(`value='${gater}'`));
    // Backpacks.
    await go('backpacks'); assert.equal(await count('.weapon-list [data-backpack-item]'), 13);
    await click('[data-backpack-item="LIFT-850 Jump Pack"]');
    assert.equal(await count('[data-entity-field] input[type=number]'), 2); assert.equal(await count('[data-entity-field] [data-evidence-tier="gameplay_proven"]'), 2);
    assert.equal(await count('[data-callin="LIFT-850 Jump Pack"] [data-semantic-field="stratagem.cooldown"] input'), 1);
    await fill('[data-entity-field][data-semantic-field="recharge.time"] input', '8');
    await go('backpacks'); await click('[data-backpack-item="LIFT-860 Hover Pack"]');
    assert.equal(await count('[data-semantic-field="jump.vertical_launch_velocity"] input'), 0); assert.equal(await count('[data-semantic-field="recharge.time"] [data-evidence-tier="schema_proven"]'), 1);
    await go('backpacks'); await click('[data-backpack-item="LIFT-182 Warp Pack"]'); assert.equal(await count('[data-backpack-empty]'), 1);
    await go('backpacks'); await click('[data-backpack-item="B-1 Supply Pack"]'); assert.equal(await count('[data-entity-field] input'), 0); assert.equal(await count('[data-entity-field]'), 3);
    await screenshot('runtime023-backpacks');
    // Changes: one entry per vehicle/backpack.
    await go('changes');
    for (const key of ['vehicle:TD-220 Bastion MK XVI','vehicle:M-102 Gunner FRV','backpack:LIFT-850 Jump Pack']) assert.equal(await count(`[data-entity-change-group="${key}"]`), 1, key);
    assert.equal(await count('[data-stratagem-change-group="FX-12 Shield Generator Relay"]'), 1);
    await click('[data-entity-change-group="vehicle:TD-220 Bastion MK XVI"] summary'); await screenshot('runtime023-changes');
    await go('export'); await evaluate("[...document.querySelectorAll('button')].find(b=>b.innerText.includes('Build / Export Mod')).click()"); await waitFor("document.body.innerText.includes('Export complete')",'export');
    // Restore the sample project.
    await go('changes'); await evaluate("[...document.querySelectorAll('button')].find(b=>b.innerText.includes('Reset all vehicle')).click()"); await sleep(400);
    assert.equal(await count('[data-entity-change-group]'), 0);
    await go('stratagems:defensive'); await click('[data-stratagem="FX-12 Shield Generator Relay"]'); await fill(field('shield.radius', '[data-shield-projector]') + ' input', '15');
    await go('library');
    console.log('PASS: relay base/zone/shield, vehicles (zones, evidence, call-in), mount swap filtering + acknowledgement, backpacks, changes grouping, Lua and export');
} finally { socket.close(); }
