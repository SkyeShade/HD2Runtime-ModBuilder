// Game icons + SDK 0.25.1 desktop smoke. Launch on an isolated data root with --remote-debugging-port, then
// HD2GUI_CDP_PORT=<port> node tools/icons-smoke.mjs fresh|stale|off
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
const mode = process.argv[2] ?? 'fresh';
const settings = async () => { await evaluate(`[...document.querySelectorAll('.nav-item')].find(b => b.innerText.includes('Settings')).click()`); await sleep(500); };
const report = { mode };
try {
    await waitFor("!!document.querySelector('.desktop-shell')", 'startup');
    report.brand = await evaluate(`({ accent: getComputedStyle(document.documentElement).getPropertyValue('--accent').trim(), booster: getComputedStyle(document.documentElement).getPropertyValue('--cat-booster').trim(), mark: getComputedStyle(document.querySelector('.sidebar .brand-icon rect')).fill })`);
    assert.equal(report.brand.accent, '#fdd00e'); assert.equal(report.brand.mark, 'rgb(253, 208, 14)'); assert.equal(report.brand.booster, '#f2c94c');
    await waitFor("!document.querySelector('.activity')", 'initial load');
    await settings();
    report.sdkAtStart = await evaluate("document.querySelector('[data-installed-sdk]').innerText");
    if (!report.sdkAtStart.includes('0.25.1')) {
        // Older installed SDK: the published 0.25.1 release is offered and installed from GitHub (update path).
        await waitFor("[...document.querySelectorAll('button')].some(b => b.innerText.includes('Install Update'))", 'update offered');
        await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Install Update')).click()");
        await waitFor("document.querySelector('[data-installed-sdk]')?.innerText.includes('0.25.1') && !document.querySelector('.activity')", 'installed 0.25.1');
    }
    report.sdk = await evaluate("document.querySelector('[data-installed-sdk]').innerText");
    assert(report.sdk.includes('0.25.1'), report.sdk); assert((await evaluate("document.querySelector('.sdk-mini').innerText")).includes('0.25.1'));
    if (mode === 'off') {
        await sleep(8000); await settings();
        report.status = await evaluate("document.querySelector('[data-icon-status]').innerText");
        assert(report.status.includes('Automatic import is off'), report.status); assert.equal(await count('[data-icons-auto]:checked'), 0);
    } else {
        // Icons arrive in the background without a reload.
        for (let i = 0; i < 160 && !(await evaluate("(document.querySelector('[data-stratagem-icon-count]')?.innerText ?? '').length > 0")); i++) { await sleep(500); if (i % 10 === 9) await settings(); }
        report.status = await evaluate("document.querySelector('[data-icon-status]').innerText");
        report.stratagemCount = await evaluate("document.querySelector('[data-stratagem-icon-count]').innerText");
        report.boosterCount = await evaluate("document.querySelector('[data-booster-icon-count]')?.innerText");
        report.mismatch = await count('[data-icon-library-mismatch]'); report.error = await evaluate("document.querySelector('[data-icon-import-error]')?.innerText ?? null");
        assert(report.stratagemCount.startsWith('84 of 95'), report.stratagemCount);
    }
    // Authoring pages need a project.
    await go('library'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Create New Mod')).click()"); await waitFor("document.querySelector('#mod-name')", 'create dialog');
    await fill('#mod-name', 'Icons ' + mode + ' ' + Date.now()); await fill('#author', 'Tests');
    await evaluate("document.querySelector('.dialog form button[type=submit]').click()");
    await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')", 'project created');
    await go('stratagems'); await sleep(800);
    report.stratagems = await evaluate(`(() => { const rows = [...document.querySelectorAll('.weapon-list [data-stratagem]')];
        return { listed: rows.length, svg: rows.filter(r => r.querySelector('.game-icon img')).length,
          fallbacks: rows.filter(r => r.querySelector('.game-icon.fallback')).map(r => r.dataset.stratagem + ' [' + r.querySelector('.game-icon').dataset.iconState + '] ' + r.querySelector('.game-icon').title) }; })()`);
    await screenshot('icons-' + mode + '-stratagems');
    await go('boosters'); await sleep(600);
    report.boosters = { listed: await count('[data-booster-item]'), svg: await count('[data-booster-item] .game-icon img') };
    if (mode !== 'off') { assert.equal(report.stratagems.listed, 93); assert.equal(report.stratagems.svg, 84); assert.equal(report.boosters.svg, 18); }
    else { assert.equal(report.stratagems.svg, 0); assert.equal(report.boosters.svg, 0); }
    console.log(JSON.stringify(report, null, 1)); console.log('icons smoke passed: ' + mode);
} finally { socket.close(); }
