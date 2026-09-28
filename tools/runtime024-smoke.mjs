// Developer-only smoke test for Runtime 0.24.0 boosters and expanded support weapons against a running isolated MAUI app (WebView2 CDP).
// Launch Debug with HD2RUNTIMEGUI_DATA_ROOT=<empty folder> and WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9237,
// then run: HD2GUI_CDP_PORT=9237 node tools/runtime024-smoke.mjs
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
const lua = async () => { await go('lua'); const value = await evaluate('document.querySelector("pre").innerText'); return value; };
const booster = async name => { await go('boosters'); await click(`[data-booster-item="${name}"]`); await waitFor(`document.querySelector('[data-booster="${name}"]')`, 'booster ' + name); };
const PODS = 'Armed Resupply Pods', INFUSION = 'Experimental Infusion';
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')", 'startup');
    // Yellow Boosters category next to Support (blue), Offensive (red) and Defensive (green).
    assert.equal(await count('[data-nav="boosters"].cat-booster'), 1);
    const colours = await evaluate(`Object.fromEntries(['stratagems:support','stratagems:offensive','stratagems:defensive','boosters'].map(k => [k, getComputedStyle(document.querySelector('[data-nav="' + k + '"] .cat-dot')).backgroundColor]))`);
    assert.equal(colours.boosters, 'rgb(242, 201, 76)'); assert.equal(new Set(Object.values(colours)).size, 4);

    await go('library'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Create New Mod')).click()"); await waitFor("document.querySelector('#mod-name')", 'create dialog');
    await fill('#mod-name', 'Booster Smoke'); await fill('#author', 'Tests');
    await evaluate("document.querySelector('.dialog form button[type=submit]').click()");
    await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')", 'project created');

    // Every published booster is listed; unresolved ones render Runtime's blocker and no controls.
    await go('boosters'); assert.equal(await count('[data-booster-item]'), 20); assert.equal(await count('[data-booster-item][data-booster-writable="true"]'), 2);
    await booster('Integrated Extinguishers');
    assert.equal(await count('[data-booster-readonly]'), 1); assert.equal(await count('[data-blocked-field="susceptibility override"]'), 1);
    assert.equal(await count('[data-booster-relationship="susceptibility_gate"]'), 1); assert.equal(await count('[data-booster-target]'), 0);
    assert.equal(await count('.weapon-detail input[type=number]'), 0);
    await booster('Vitality Enhancement'); assert((await evaluate("document.querySelector('[data-booster-identity]').innerText")).includes('Candidate values'));

    // Armed Resupply Pods: deployed turret fire rate and magazine capacity, allow_unverified_effect only.
    await booster(PODS);
    assert.equal(await count('[data-booster-target="deployed_entity"] [data-entity-field] input[type=number]'), 2);
    assert.equal(await count('[data-booster-target="deployed_entity"] [data-flag="allow_unverified_effect"]'), 1); assert.equal(await count('[data-booster-target="deployed_entity"] [data-flag="allow_shared"]'), 0);
    assert.equal(await evaluate(`document.querySelector('[data-semantic-field="weapon.fire_rate"] [data-baseline]').dataset.baseline`), '640');
    assert.equal(await evaluate(`document.querySelector('[data-semantic-field="magazine.capacity"] [data-baseline]').dataset.baseline`), '140');
    await fill('[data-semantic-field="weapon.fire_rate"] input[type=number]', '900');
    await waitFor("document.querySelector('[data-booster-ack]')", 'booster acknowledgement');
    assert((await text()).includes('Build requires review: Acknowledge the unverified booster effect'));
    await click('[data-booster-ack]'); await sleep(300); assert(!(await text()).includes('Build requires review'));
    await screenshot('runtime0240-armed-resupply-pods');

    // Experimental Infusion: status effect with both acknowledgements.
    await booster(INFUSION);
    assert.equal(await count('[data-booster-target="status_effect"] [data-entity-field] input[type=number]'), 3);
    assert.equal(await count('[data-booster-target="status_effect"] [data-flag="allow_shared"]'), 1); assert.equal(await count('[data-booster-target="status_effect"] [data-flag="allow_unverified_effect"]'), 1);
    await fill('[data-semantic-field="status.incoming_damage_scale"] input[type=number]', '0.8');
    await waitFor("document.querySelector('[data-booster-ack]')", 'infusion acknowledgement');
    assert((await text()).includes('Build requires review: Acknowledge this shared object'));
    await click('[data-booster-ack]'); await sleep(300); assert(!(await text()).includes('Build requires review'));
    await screenshot('runtime0240-experimental-infusion');

    let script = await lua();
    assert(script.includes("target=hd2.booster('Armed Resupply Pods'):deployed_entity(),")); assert(script.includes('field=hd2.fields.weapon.fire_rate,'));
    assert(script.includes("target=hd2.booster('Experimental Infusion'):status_effect(),")); assert(script.includes('allow_shared=true,'));
    assert.equal(script.split('allow_unverified_effect=true,').length - 1, 2);

    // Support weapons: MG-43 is delivery-resolved and writable in its merged stratagem editor; reload.duration needs its own acknowledgement.
    await go('stratagems:support'); await click('[data-stratagem="MG-43 Machine Gun"]');
    await waitFor("document.querySelector('[data-support-delivery-resolved]')", 'MG-43 delivery resolved');
    assert.equal(await count('[data-support-writable="true"]'), 1); assert.equal(await count('[data-support-identity-blocked]'), 0);
    assert.equal(await evaluate("document.querySelector('[data-support-identity]').dataset.supportIdentity"), 'DELIVERY_RESOLVED');
    const reload = '[data-support-field][data-semantic-field="reload.duration"]';
    assert.equal(await count(`${reload} [data-flag="allow_unverified_effect"]`), 1);
    const baseline = await evaluate(`document.querySelector('${reload} input[type=number]').value`);
    await fill(`${reload} input[type=number]`, String(Number(baseline) + 1));
    const effectAck = `document.querySelector('${reload}').closest('[data-support-authoring-branch]').querySelector('[data-support-effect-ack]')`; // one grouped acknowledgement per branch
    await waitFor(effectAck, 'reload acknowledgement');
    assert((await text()).includes('Acknowledge the unverified gameplay effect'));
    await evaluate(`${effectAck}.click()`); await sleep(400);
    await evaluate(`document.querySelector('${reload}').scrollIntoView({block: 'center'})`); await screenshot('runtime0240-mg43-reload');
    script = await lua();
    assert(script.includes("target=hd2.support_weapon('MG-43 Machine Gun'),")); assert(script.includes('field=hd2.fields.reload.duration,'));
    assert.equal(script.split('allow_unverified_effect=true,').length - 1, 3);

    // EAT-17 stays blocked exactly as Runtime publishes it.
    await go('stratagems:support'); await click('[data-stratagem="EAT-17 Expendable Anti-Tank"]');
    await waitFor("document.querySelector('[data-support-identity-blocked]')", 'EAT-17 blocked');
    assert.equal(await count('[data-support-writable="false"]'), 1); assert.equal(await count('[data-support-field]'), 0);

    // Changes: booster entries in yellow; reset per booster; build/export.
    await go('changes');
    assert.equal(await count(`[data-entity-change-group="booster:${PODS}"] .category-pill.cat-booster`), 1);
    assert.equal(await count(`[data-entity-change-group="booster:${INFUSION}"]`), 1);
    await click(`[data-entity-change-group="booster:${INFUSION}"] summary`); await screenshot('runtime0240-changes');
    await go('export'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Build / Export Mod')).click()"); await waitFor("document.body.innerText.includes('Export complete')", 'export');
    await go('changes'); await click(`[data-entity-change-group="booster:${PODS}"] summary`);
    await evaluate(`[...document.querySelectorAll('[data-entity-change-group="booster:${PODS}"] button')].find(b => b.innerText.trim() === 'Reset booster').click()`); await sleep(500);
    assert.equal(await count(`[data-entity-change-group="booster:${PODS}"]`), 0);
    script = await lua(); assert(!script.includes(PODS)); assert(script.includes(INFUSION));
    await go('library');
    console.log('PASS: yellow Boosters category, 20 boosters (blocked ones read-only), Armed Resupply Pods and Experimental Infusion fields and acknowledgements, MG-43 delivery-resolved reload with allow_unverified_effect, EAT-17 blocked, Lua, Changes, export, reset');
} finally { socket.close(); }
