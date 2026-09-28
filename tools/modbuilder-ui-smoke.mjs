// HD2Runtime ModBuilder IA/branding smoke: sidebar, branding, compact Overview, Support → Vehicles / Backpacks / Other, icons.
// HD2GUI_CDP_PORT=<port> node tools/modbuilder-ui-smoke.mjs  (on a data root with SDK 0.25.1, imported icons and a 'liberatordamage' project)
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
const icons = sel => evaluate(`(() => { const rows = [...document.querySelectorAll(${JSON.stringify(sel)})]; return { rows: rows.length, svg: rows.filter(r => r.querySelector('.game-icon img')).length,
    fallback: rows.filter(r => r.querySelector('.game-icon.fallback')).map(r => (r.dataset.stratagem ?? r.dataset.supportEntity ?? r.dataset.standaloneSupport) + ' [' + (r.querySelector('.game-icon').dataset.iconState ?? '') + ']') }; })()`);
const tab = async family => { await click(`[data-family-tab="${family}"]`); await sleep(500); };
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')", 'startup');
    // Branding.
    report.brand = await evaluate(`({ title: document.title, titlebar: document.querySelector('.titlebar').innerText.split('/')[0].trim(), lockup: !!document.querySelector('.sidebar [data-brand-lockup="sm"]'),
        fill: getComputedStyle(document.querySelector('.sidebar .brand-icon rect')).fill, accent: getComputedStyle(document.documentElement).getPropertyValue('--brand-yellow').trim(),
        booster: getComputedStyle(document.documentElement).getPropertyValue('--booster-category-yellow').trim() })`);
    assert.equal(report.brand.title, 'HD2Runtime ModBuilder'); assert.equal(report.brand.titlebar, 'HD2Runtime ModBuilder'); assert(report.brand.lockup);
    assert.equal(report.brand.fill, 'rgb(253, 208, 14)'); assert.equal(report.brand.booster, '#f2c94c');
    await screenshot('modbuilder-branding-sidebar');
    // Sidebar: no top-level Vehicles / Backpacks.
    report.nav = await evaluate(`[...document.querySelectorAll('.sidebar [data-nav]')].map(b => b.dataset.nav)`);
    assert(!report.nav.includes('vehicles') && !report.nav.includes('backpacks'), report.nav.join());
    // Compact Overview.
    await openProject('liberatordamage'); await go('overview'); await sleep(500);
    report.overview = await evaluate(`({ facts: [...document.querySelectorAll('[data-overview-facts] dt')].map(d => d.innerText + '=' + d.nextElementSibling.innerText).join(' | '),
        height: document.querySelector('[data-overview]').getBoundingClientRect().height, switch: document.querySelector('[data-options-enabled]').closest('label').getBoundingClientRect().width,
        status: document.querySelector('[data-options-status]')?.innerText, changePanels: document.querySelectorAll('[data-overview] [data-weapon-group]').length })`);
    await evaluate("document.querySelector('.workspace-content').scrollTop = 0"); await sleep(300); await screenshot('modbuilder-overview');
    if (!(await evaluate("document.querySelector('[data-options-enabled]').checked"))) { await click('[data-options-enabled]'); await sleep(500); }
    report.optionsStatus = await evaluate("document.querySelector('[data-options-status]').innerText");
    await screenshot('modbuilder-overview-options-on');
    await click('[data-options-enabled]'); await sleep(400); // back off: generated Lua unchanged
    // Support → Vehicles.
    await go('stratagems:support'); await sleep(600);
    report.supportTabs = await evaluate(`[...document.querySelectorAll('[data-family-tab]')].map(t => t.innerText.replace(/[\\s]+/g, ' ').trim())`);
    await tab('vehicle'); report.vehicles = await icons('.weapon-list [data-stratagem]');
    await click('[data-stratagem="TD-220 Bastion MK XVI"]'); await sleep(600);
    report.bastion = { header: await count('[data-stratagem-header="TD-220 Bastion MK XVI"]'), callIn: await count('[data-callin]'), vehicle: await count('[data-vehicle="TD-220 Bastion MK XVI"]'),
        durability: await count('[data-vehicle-durability]'), delivers: await evaluate("document.querySelector('[data-delivers-entity]')?.dataset.deliversEntity") };
    assert.equal(report.bastion.header, 1); assert.equal(report.bastion.callIn, 1); assert.equal(report.bastion.vehicle, 1);
    await evaluate("document.querySelector('.workspace-content').scrollTop = 0"); await sleep(300); await screenshot('modbuilder-support-vehicles');
    // Support → Backpacks.
    await tab('backpack'); report.backpacks = await icons('.weapon-list [data-stratagem]');
    await click('[data-stratagem="B-1 Supply Pack"]'); await sleep(600);
    report.supplyPack = { header: await count('[data-stratagem-header="B-1 Supply Pack"]'), callIn: await count('[data-callin]'), backpack: await count('[data-backpack="B-1 Supply Pack"]') };
    assert.equal(report.supplyPack.backpack, 1); assert.equal(report.supplyPack.callIn, 1);
    await evaluate("document.querySelector('.workspace-content').scrollTop = 0"); await sleep(300); await screenshot('modbuilder-support-backpacks');
    // Other / Standalone.
    await tab('other'); report.other = await evaluate(`[...document.querySelectorAll('.weapon-list [data-support-entity], .weapon-list [data-standalone-support], .weapon-list [data-unlinked-support]')].map(b => b.innerText.split(String.fromCharCode(10))[0])`);
    await click('[data-support-entity="GATER Oil Rig"]'); await sleep(500); report.otherVehicle = await count('[data-vehicle="GATER Oil Rig"]');
    // Support weapons tab and the main list with icons.
    await tab('support'); report.supportWeapons = await icons('.weapon-list [data-stratagem]');
    await go('stratagems'); await sleep(700); await click('[data-stratagem="Orbital Precision Strike"]'); await sleep(400); report.all = await icons('.weapon-list [data-stratagem]');
    await evaluate("document.querySelector('.workspace-content').scrollTop = 0"); await sleep(300); await screenshot('modbuilder-stratagems-icons');
    await go('boosters'); await sleep(600); report.boosters = { rows: await count('[data-booster-item]'), svg: await count('[data-booster-item] .game-icon img') };
    // Settings / About branding.
    await evaluate(`[...document.querySelectorAll('.nav-item')].find(b => b.innerText.includes('Settings')).click()`); await sleep(500);
    await evaluate("document.querySelector('[data-about]')?.scrollIntoView({block:'center'})"); await sleep(400); await screenshot('modbuilder-about');
    report.about = await evaluate("document.querySelector('[data-about]')?.innerText.slice(0, 80)");
    console.log(JSON.stringify(report, null, 1));
    assert.equal(report.vehicles.rows, 9); assert.equal(report.vehicles.svg, 7); assert.equal(report.backpacks.rows, 13); assert.equal(report.backpacks.svg, 10);
    assert.equal(report.otherVehicle, 1); assert.equal(report.boosters.svg, 18);
    console.log('modbuilder ui smoke passed');
} finally { socket.close(); }
