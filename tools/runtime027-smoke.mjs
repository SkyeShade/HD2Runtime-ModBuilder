// Developer-only smoke test for HD2Runtime SDK 0.27.0 automatic asset loading against a running isolated app (WebView2 CDP):
// asset states on pod payloads, vehicle mounts and projectile sources, kept separate from gameplay compatibility; the SDK pin.
// Launch the app with HD2RUNTIMEGUI_DATA_ROOT=<empty folder> and WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9238,
// then run: HD2GUI_CDP_PORT=9238 node tools/runtime027-smoke.mjs
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
const go = async target => { if (target === 'settings') await evaluate(`[...document.querySelectorAll('.sidebar-bottom button')].find(b => b.innerText.includes('Settings')).click()`); else await evaluate(`document.querySelector('[data-nav="${target}"]').click()`); await sleep(450); };
const count = selector => evaluate(`document.querySelectorAll(${JSON.stringify(selector)}).length`);
const q = selector => `document.querySelector(${JSON.stringify(selector)})`;
const click = async selector => { await evaluate(`(() => { const el = ${q(selector)}; if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.click(); })()`); await sleep(450); };
const fill = async (selector, value) => { await evaluate(`(() => { const el = ${q(selector)}; if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.value = ${JSON.stringify(value)}; el.dispatchEvent(new Event('input', {bubbles: true})); el.dispatchEvent(new Event('change', {bubbles: true})); })()`); await sleep(550); };
const scroll = async selector => { await evaluate(`(() => { ${q(selector)}.scrollIntoView({block: 'start'}); const c = document.querySelector('.workspace-content'); if (c) c.scrollTop -= 230; })()`); await sleep(300); };
const text = () => evaluate('document.body.innerText');
const lua = async () => { await go('lua'); return evaluate('document.querySelector("pre").innerText'); };
const stratagem = async (name, family = 'support') => { await go('stratagems:support'); await click(`[data-family-tab="${family}"]`); await click(`[data-stratagem="${name}"]`); await waitFor(q(`[data-stratagem-header="${name}"]`), name); };
const EAT700 = 'pickup/v1/eat-700-expendable-napalm/bff4b15f31d35c94', AMMO_POD = 'pickup/v1/ammo-box-pod/096e2d589f69f1ae';
const SUPPLY_FRV_GUN = 'mounted-weapon/v1/m-103-supply-frv-gun-weapon/34d73fd6e0ab1d96';
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')", 'startup');
    assert((await evaluate(`${q('.sdk-mini')}.innerText`)).includes('0.27.0'), 'SDK 0.27.0 is bound');
    await go('settings'); assert.equal(await evaluate(`${q('[data-sdk-supported]')}.innerText`), '0.27.0');
    await go('library'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Create New Mod')).click()"); await waitFor(q('#mod-name'), 'create dialog');
    await fill('#mod-name', 'Runtime 027 Smoke'); await fill('#author', 'Tests');
    await evaluate("document.querySelector('.dialog form button[type=submit]').click()");
    await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')", 'project created');

    // Stalwart pod slot 2 -> EAT-700: assets live-verified, a live-verified pair, and the (separate) compatibility warning. No package risk.
    await stratagem('M-105 Stalwart');
    const slot2 = '[data-pod-rack="M-105 Stalwart pod"] [data-pod-slot="2"]';
    await waitFor(q(slot2), 'Stalwart pod');
    assert((await evaluate(`[...${q(slot2 + ' [data-pod-slot-select]')}.options].find(o => o.value === ${JSON.stringify(EAT700)}).text`)).includes('assets live-verified'));
    await fill(slot2 + ' [data-pod-slot-select]', EAT700); await waitFor(q(slot2 + ' [data-asset-badge]'), 'asset badge');
    assert.equal(await evaluate(`${q(slot2 + ' [data-asset-badge]')}.innerText`), 'Assets live-verified');
    assert.equal(await count(slot2 + ' [data-pickup-badge="live-pair"]'), 1); assert.equal(await count(slot2 + ' [data-pickup-badge="package-risk"]'), 0);
    assert.equal(await count(slot2 + ' [data-pod-compatibility-warning]'), 1); assert.equal(await count(slot2 + ' [data-asset-warning]'), 0);
    assert.equal(await evaluate(`${q(slot2 + ' [data-asset-details]')}.open`), false, 'asset details collapsed by default');
    await scroll(slot2); await screenshot('runtime027-pod-assets-live-verified');
    // Slot 1 -> Ammo Box (pod): Runtime cannot load it -> explicit "Assets unknown" warning, compatibility warning kept.
    const slot1 = '[data-pod-rack="M-105 Stalwart pod"] [data-pod-slot="1"]';
    await fill(slot1 + ' [data-pod-slot-select]', AMMO_POD); await waitFor(q(slot1 + ' [data-asset-warning]'), 'unknown warning');
    assert.equal(await evaluate(`${q(slot1 + ' [data-asset-badge]')}.innerText`), 'Assets unknown');
    assert((await evaluate(`${q(slot1 + ' [data-asset-warning]')}.innerText`)).includes('missing asset'));
    await click(slot1 + ' [data-asset-details] summary'); assert((await evaluate(`${q(slot1 + ' [data-asset-details]')}.innerText`)).includes('Blocker'));
    await scroll(slot1); await screenshot('runtime027-pod-assets-unknown');

    // Reprimand -> Talon projectile: offered now, assets live-verified.
    await go('player-weapons'); await click('[data-weapon="SMG-32 Reprimand"]');
    const proj = '[data-projectile="primary"]';
    await waitFor(q(proj + ' select'), 'projectile selector');
    const talonOption = await evaluate(`[...${q(proj + ' select')}.options].find(o => o.text.startsWith('LAS-58 Talon'))?.text`);
    assert(talonOption && talonOption.includes('assets live-verified'), 'Talon source offered with live-verified assets: ' + talonOption);
    await evaluate(`(() => { const s = ${q(proj + ' select')}; s.value = [...s.options].find(o => o.text.startsWith('LAS-58 Talon')).value; s.dispatchEvent(new Event('change', {bubbles: true})); })()`); await sleep(600);
    await waitFor(q(proj + ' [data-asset-badge]'), 'projectile asset badge');
    assert.equal(await evaluate(`${q(proj + ' [data-asset-badge]')}.innerText`), 'Assets live-verified');
    assert(!(await text()).includes('independent resource residency is unresolved'));
    await scroll(proj); await screenshot('runtime027-projectile-talon');

    // FRV mount -> M-103 Supply FRV gun: assets loaded automatically (offline-proven loader) + gameplay compatibility unverified.
    await stratagem('M-102 Gunner FRV', 'vehicle'); await waitFor(q('[data-vehicle-weapon] .mount-swap'), 'mount swap');
    await evaluate(`${q('[data-vehicle-weapon] .mount-swap')}.open = true`); await sleep(300);
    await fill('[data-vehicle-weapon] [data-mount-select]', SUPPLY_FRV_GUN);
    await waitFor(q('[data-vehicle-weapon] [data-mount-compatibility-warning]'), 'mount compatibility warning');
    assert.equal(await evaluate(`${q('[data-vehicle-weapon] [data-mount] [data-asset-badge]')}.innerText`), 'Assets loaded automatically');
    await click('[data-vehicle-weapon] [data-mount] [data-asset-details] summary');
    const details = await evaluate(`${q('[data-vehicle-weapon] [data-mount] [data-asset-details]')}.innerText`);
    assert(details.includes('ammo_rack_mounted_turret') && details.includes('proven offline') && details.includes('waiting_for_assets') && details.includes('ASSET_UNAVAILABLE'));
    assert(!/fail/i.test(details), 'the inconclusive mount live test is not presented as a failure');
    await scroll('[data-vehicle-weapon] [data-mount]'); await screenshot('runtime027-mount-assets');

    const script = await lua();
    for (const part of [`hd2.pod_rack('M-105 Stalwart pod'):slot(2)`, `value=hd2.pickup('${EAT700}')`, 'LAS-58 Talon', 'allow_unverified_reference=true'])
        assert(script.includes(part), part);
    assert(!script.includes('require_assets'), 'Runtime loads dependencies itself: no preload calls are generated');
    await go('changes'); await go('export'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Build / Export Mod')).click()"); await waitFor("document.body.innerText.includes('Export complete')", 'export');
    await go('library');
    console.log('PASS: SDK 0.27.0 bound (supported up to 0.27.0), pod assets live-verified / unknown with separate compatibility warning, Talon projectile live-verified, FRV mount auto-loaded (offline-proven), typed Lua without preload calls, export');
} finally { socket.close(); }
