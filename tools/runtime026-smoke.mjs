// Developer-only smoke test for HD2Runtime SDK 0.26.0 against a running isolated app (WebView2 CDP): magazine variants, reticles,
// fire modes, mounted vehicle weapons, mission uses, backpack ammo and drop-pod contents, then Lua, Changes and export.
// Launch the app with HD2RUNTIMEGUI_DATA_ROOT=<empty folder> and WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9238,
// then run: HD2GUI_CDP_PORT=9238 node tools/runtime026-smoke.mjs
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
const q = selector => `document.querySelector(${JSON.stringify(selector)})`;
const click = async selector => { await evaluate(`(() => { const el = ${q(selector)}; if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.click(); })()`); await sleep(450); };
const fill = async (selector, value) => { await evaluate(`(() => { const el = ${q(selector)}; if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.value = ${JSON.stringify(value)}; el.dispatchEvent(new Event('input', {bubbles: true})); el.dispatchEvent(new Event('change', {bubbles: true})); })()`); await sleep(500); };
// Scroll the target into view below the sticky object header.
const scroll = async selector => { await evaluate(`(() => { ${q(selector)}.scrollIntoView({block: 'start'}); const c = document.querySelector('.workspace-content'); if (c) c.scrollTop -= 230; })()`); await sleep(300); };
const text = () => evaluate('document.body.innerText');
const reviewBlocked = async () => (await text()).includes('Build requires review');
// Opt-ins are implicit: a risk is a warning panel with no checkbox, and the build is never blocked on it.
const warned = async scope => { assert(await count(`${scope} [data-ack-panel]`) >= 1, 'warning shown in ' + scope); assert.equal(await count(`${scope} [data-ack-panel] input`), 0); assert(!(await reviewBlocked())); };
const lua = async () => { await go('lua'); return evaluate('document.querySelector("pre").innerText'); };
const weapon = async name => { await go('player-weapons'); await click(`[data-weapon="${name}"]`); };
const stratagem = async (name, family = 'support') => { await go('stratagems:support'); await click(`[data-family-tab="${family}"]`); await click(`[data-stratagem="${name}"]`); await waitFor(`${q(`[data-stratagem-header="${name}"]`)}`, name); };
const DRUM = 'weapon-attachment/v1/magazine/rifle-5-5x50mm-drum/fa499a29b375c6cf', LEVELLER = 'pickup/v1/eat-411-leveller/bbd78065df92b6eb';
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')", 'startup');
    assert((await text()).includes('0.26.0'), 'SDK 0.26.0 is bound');
    await go('library'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Create New Mod')).click()"); await waitFor("document.querySelector('#mod-name')", 'create dialog');
    await fill('#mod-name', 'Runtime 026 Smoke'); await fill('#author', 'Tests');
    await evaluate("document.querySelector('.dialog form button[type=submit]').click()");
    await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')", 'project created');

    // Magazines: every resolved option as a variant; the non-default Drum edited on its own.
    await weapon('AR-23 Liberator');
    await waitFor(q('[data-magazine-variants]'), 'magazine variants');
    assert.equal(await count('[data-magazine-variant]'), 4); assert.equal(await count('[data-selection-blocked]'), 1);
    await click(`[data-magazine-variant="${DRUM}"]`);
    const card = `[data-attachment-id="${DRUM}"]`;
    assert.equal(await count(`${card} [data-entity-field] input[type=number]`), 6, 'capacity, starting, supply, spare, reload, ergonomics');
    await fill(`${card} [data-semantic-field="attachment.magazine_capacity"] input`, '90');
    await fill(`${card} [data-semantic-field="attachment.reload_duration"] input`, '2.5');
    await warned(card);
    await scroll('[data-magazine-attachments]'); await screenshot('runtime026-magazine-variants');

    // Player reticle: an On/Off switch; read-only reticles show only a blocker.
    assert.equal(await count('[data-field="weapon.third_person_reticle"] input[type=checkbox][role=switch]'), 1);
    await weapon('CQC-42 Machete'); assert.equal(await count('[data-field="weapon.third_person_reticle"] input'), 0);

    // JAR-5 fire modes: four native slots, add Automatic; the unverified effect is a warning only.
    await weapon('JAR-5 Dominator'); await waitFor(q('[data-fire-modes="JAR-5 Dominator"]'), 'JAR-5 fire modes');
    assert.equal(await count('[data-fire-modes="JAR-5 Dominator"] [data-fire-mode-slot]'), 4);
    assert.equal(await count('[data-fire-modes="JAR-5 Dominator"] [data-fire-mode-slot][data-mode="single"]'), 1);
    await click('[data-fire-modes="JAR-5 Dominator"] [data-fire-mode-add="automatic"]');
    await waitFor(q('[data-fire-modes="JAR-5 Dominator"] [data-fire-mode-slot="3"][data-mode="automatic"]'), 'automatic added');
    await warned('[data-fire-modes="JAR-5 Dominator"]');
    await scroll('[data-fire-modes="JAR-5 Dominator"]'); await screenshot('runtime026-jar5-fire-modes');
    await weapon('LAS-5 Scythe'); assert.equal(await count('[data-fire-mode-blocked]'), 1);

    // APW-1 reticle inside its Support stratagem page: gameplay-proven, no acknowledgement.
    await stratagem('APW-1 Anti-Materiel Rifle');
    const reticle = '[data-support-field][data-semantic-field="weapon.third_person_reticle"]';
    await waitFor(q(reticle), 'APW-1 reticle'); await click(`${reticle} input[type=checkbox]`);
    await waitFor(q(`${reticle}.modified`), 'reticle saved'); assert.equal(await count(`${reticle} [data-gameplay-proven]`), 1); assert(!(await reviewBlocked()));
    await scroll(reticle); await screenshot('runtime026-apw1-reticle');

    // Mounted vehicle weapons: Bastion cannon and HMG are separate sections with scope lines.
    await stratagem('TD-220 Bastion MK XVI', 'vehicle'); await waitFor(q('[data-vehicle-weapon]'), 'vehicle weapons');
    assert.equal(await count('[data-vehicle-weapon]'), 2); assert(await count('[data-scope-line]') >= 4);
    const hmg = '[data-vehicle-weapon="TD-220 Bastion MK XVI / attach_tank_gun_mg"]';
    const hmgCapacity = `${hmg} [data-vehicle-weapon-group="local"] [data-semantic-field="weapon.capacity"] input`;
    const base = await evaluate(`${q(hmgCapacity)}.value`); await fill(hmgCapacity, String(Number(base) + 50));
    await waitFor(q(`${hmg}.modified`), 'vehicle weapon saved');
    assert.equal(await count(`${hmg} [data-ack-panel] input`), 0); assert(!(await reviewBlocked())); await scroll(hmg); await screenshot('runtime026-vehicle-weapon');
    await stratagem('EXO-49 Emancipator Exosuit', 'vehicle'); await waitFor(q('[data-vehicle-weapon="EXO-49 Emancipator Exosuit / left_gun"]'), 'exosuit arms');
    assert.equal(await count('[data-vehicle-weapon="EXO-49 Emancipator Exosuit / right_gun"]'), 1);

    // Mission uses: compact [Limited ▼] [3] -> [Unlimited ▼] (no count input); Exosuit 3 -> Unlimited is gameplay-proven.
    const uses = '[data-mission-uses]';
    assert.equal(await evaluate(`${q(uses)}.dataset.usesMode`), 'limited'); assert.equal(await evaluate(`${q(uses + ' [data-uses-count]')}.value`), '3');
    assert.equal(await count(uses + ' input[type=radio]'), 0); assert.equal(await evaluate(`${q(uses)}.closest('.field-row').querySelectorAll('[data-uses-caveat]').length`), 1);
    await scroll('[data-callin]'); await screenshot('runtime026-mission-uses-limited');
    await fill(uses + ' [data-uses-select]', 'unlimited'); await waitFor(`${q(uses)}.dataset.usesMode === 'unlimited'`, 'unlimited');
    assert.equal(await count(uses + ' [data-uses-count]'), 0); assert.equal(await count('[data-gameplay-proven]'), 1); assert(!(await reviewBlocked()));
    await screenshot('runtime026-mission-uses-unlimited');
    // Unlimited -> Limited proposes the baseline count; the value returns to base, so the change is removed.
    await fill(uses + ' [data-uses-select]', 'limited'); await waitFor(`${q(uses)}.dataset.usesMode === 'limited'`, 'limited again');
    assert.equal(await evaluate(`${q(uses + ' [data-uses-count]')}.value`), '3'); assert.equal(await evaluate(`${q(uses)}.closest('.field-row').classList.contains('modified')`), false);
    await fill(uses + ' [data-uses-select]', 'unlimited'); await waitFor(`${q(uses)}.dataset.usesMode === 'unlimited'`, 'unlimited again');
    // FRV: baseline Unlimited; Limited shows the count and the unverified-effect warning.
    await stratagem('M-102 Gunner FRV', 'vehicle'); assert.equal(await evaluate(`${q(uses)}.dataset.usesMode`), 'unlimited');
    await fill(uses + ' [data-uses-select]', 'limited'); await waitFor(q(uses + ' [data-uses-count]'), 'FRV limited count');
    await fill(uses + ' [data-uses-count]', '2'); await waitFor(`${q(uses + ' [data-uses-count]')}.value === '2'`, 'FRV count 2');
    await warned(`[data-stratagem-field="${await evaluate(`${q(uses)}.closest('[data-stratagem-field]').dataset.stratagemField`)}"]`);
    await screenshot('runtime026-mission-uses-frv');
    assert((await lua()).includes("value=2"));

    // Maxigun backpack ammunition on its Support page.
    await stratagem('M-1000 Maxigun'); await waitFor(q('[data-backpack-ammo="M-1000 Maxigun Backpack"]'), 'Maxigun backpack ammo');
    await fill('[data-backpack-ammo] [data-semantic-field="deposit.capacity"] input', '1500');
    await warned('[data-backpack-ammo]');
    await scroll('[data-backpack-ammo]'); await screenshot('runtime026-maxigun-backpack-ammo');

    // Surplus EAT: granted stratagem -> drop pod -> spawn count -> slots, with the shared-rack warning.
    await go('boosters'); await click('[data-booster-item="Surplus EAT Allocation"]');
    await waitFor(q('[data-booster-pod] [data-pod-rack="EAT-17 Expendable Anti-Tank pod"]'), 'Surplus EAT drop pod');
    assert.equal(await count('[data-pod-shared-warning]'), 1); assert.equal(await count('[data-pod-slot]'), 4);
    assert.equal(await count('[data-pod-slot="5"]'), 0, 'slots 5-8 are never shown');
    await fill('[data-pod-spawn-count] input[type=number]', '3');
    await fill('[data-pod-slot="2"] [data-pod-slot-select]', LEVELLER);
    await waitFor(q('[data-pod-slot="2"] [data-ack-panel]'), 'replacement warning');
    await warned('[data-pod-slot="2"]'); assert(await count('[data-booster-pod] [data-ack-panel]') >= 3, 'replacement, spawn-count and shared-rack warnings');
    await scroll('[data-booster-pod]'); await screenshot('runtime026-surplus-eat-payload');

    const script = await lua();
    for (const part of [`hd2.weapon_attachment('${DRUM}')`, "{'single','burst','automatic'}", "hd2.support_weapon('APW-1 Anti-Materiel Rifle')", 'hd2.fields.weapon.third_person_reticle',
        "hd2.vehicle('TD-220 Bastion MK XVI'):weapon('attach_tank_gun_mg')", "value='unlimited'", "hd2.support_weapon('M-1000 Maxigun'):backpack()",
        "hd2.pod_rack('EAT-17 Expendable Anti-Tank pod'):slot(2)", `value=hd2.pickup('${LEVELLER}')`, 'hd2.fields.payload.spawn_count', 'allow_unverified_reference=true', 'allow_shared=true'])
        assert(script.includes(part), part);

    await go('changes');
    assert.equal(await count('[data-entity-change-group="vehicle:TD-220 Bastion MK XVI"]'), 1);
    assert.equal(await count('[data-entity-change-group="pod_rack:EAT-17 Expendable Anti-Tank pod"]'), 1);
    await go('export'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Build / Export Mod')).click()"); await waitFor("document.body.innerText.includes('Export complete')", 'export');
    await go('changes'); await click('[data-entity-change-group="pod_rack:EAT-17 Expendable Anti-Tank pod"] summary'); await click('[data-pod-reset]');
    assert.equal(await count('[data-entity-change-group="pod_rack:EAT-17 Expendable Anti-Tank pod"]'), 0);
    assert(!(await lua()).includes('hd2.pod_rack'));
    await go('library');
    console.log('PASS: SDK 0.26.0 magazine variants, reticles, JAR-5 fire modes, Bastion/Exosuit mounted weapons, mission uses, Maxigun backpack ammo, Surplus EAT drop pod, Lua, Changes, export, reset');
} finally { socket.close(); }
