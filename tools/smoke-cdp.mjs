// Shared WebView2 CDP helpers for the desktop smokes (tools/*-smoke.mjs). The app must run with
// WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=<port>; scripts/run-rc-smokes.ps1 launches it that way.
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';

export async function connect(port = process.env.HD2GUI_CDP_PORT ?? 9250) {
    let pages;
    for (let attempt = 0; attempt < 120; attempt++) {
        try { pages = await (await fetch(`http://127.0.0.1:${port}/json`)).json(); if (pages.some(p => p.url === 'https://0.0.0.1/')) break; }
        catch { /* WebView2 is still starting. */ }
        await new Promise(resolve => setTimeout(resolve, 500));
    }
    const page = pages?.find(p => p.url === 'https://0.0.0.1/');
    assert(page, 'MAUI Blazor WebView page must be running');
    const socket = new WebSocket(page.webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
    let id = 0; const pending = new Map();
    socket.onmessage = ({data}) => { const m = JSON.parse(data); if (m.id) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.reject(m.error) : p.resolve(m.result); } };
    const cdp = (method, params = {}) => new Promise((resolve, reject) => { const n = ++id; pending.set(n, {resolve, reject}); socket.send(JSON.stringify({id: n, method, params})); });
    const evaluate = async expression => { const r = await cdp('Runtime.evaluate', {expression, returnByValue: true, awaitPromise: true}); if (r.exceptionDetails) throw new Error(JSON.stringify(r.exceptionDetails)); return r.result.value; };
    const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
    const q = selector => `document.querySelector(${JSON.stringify(selector)})`;
    const text = () => evaluate('document.body.innerText');
    const waitFor = async (expression, message, tries = 200) => { for (let i = 0; i < tries; i++) { if (await evaluate(`Boolean(${expression})`)) return; await sleep(250); } throw new Error('Timed out: ' + message + '\n' + (await text()).slice(0, 3000)); };
    const idle = () => waitFor("!document.querySelector('.activity')", 'activity');
    const click = async selector => { await evaluate(`(() => { const el = ${q(selector)}; if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.scrollIntoView({block:'center'}); el.click(); })()`); await sleep(450); };
    const clickText = async (selector, label) => { await evaluate(`(() => { const el = [...document.querySelectorAll(${JSON.stringify(selector)})].find(e => e.innerText.includes(${JSON.stringify(label)})); if (!el) throw new Error('Missing ' + ${JSON.stringify(selector + ' ' + label)}); el.scrollIntoView({block:'center'}); el.click(); })()`); await sleep(450); };
    const fill = async (selector, value) => { await evaluate(`(() => { const el = ${q(selector)}; if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.value = ${JSON.stringify(value)}; el.dispatchEvent(new Event('input', {bubbles: true})); el.dispatchEvent(new Event('change', {bubbles: true})); })()`); await sleep(600); };
    const count = selector => evaluate(`document.querySelectorAll(${JSON.stringify(selector)}).length`);
    const go = async target => { await evaluate(`document.querySelector('[data-nav="${target}"]').click()`); await sleep(500); await idle(); };
    const lua = async () => { await go('lua'); await sleep(300); return evaluate('document.querySelector("pre")?.innerText ?? ""'); };
    const shots = process.env.HD2GUI_SCREENSHOTS ?? 'docs/screenshots';
    const screenshot = async name => { await fs.mkdir(shots, {recursive: true}); const image = await cdp('Page.captureScreenshot', {format: 'png'}); await fs.writeFile(`${shots}/${name}.png`, Buffer.from(image.data, 'base64')); };
    const startup = () => waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')", 'startup');
    const createProject = async (name, author = 'Smoke') => {
        await go('library'); await clickText('button', 'Create New Mod'); await waitFor(q('#mod-name'), 'create dialog');
        await fill('#mod-name', name); await fill('#author', author);
        await evaluate("document.querySelector('.dialog form button[type=submit]').click()");
        await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')", 'project created');
    };
    const openProject = async name => { await go('library'); await clickText('.project-open', name); await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')", 'open ' + name); };
    // Player Weapons: pick a weapon from the list and autosave one numeric field.
    const chooseWeapon = async weapon => {
        await evaluate(`(() => { const e = document.querySelector('#weapon-search'); e.value = ${JSON.stringify(weapon)}; e.dispatchEvent(new Event('input', {bubbles: true})); })()`); await sleep(200);
        await evaluate(`document.querySelector('[data-weapon=' + CSS.escape(${JSON.stringify(weapon)}) + ']').click()`); await sleep(300);
    };
    const setField = async (field, value, scope = '') => {
        const row = `${scope} [data-field="${field}"]`.trim();
        await fill(`${row} input[type=number]`, value);
        await waitFor(`document.querySelector(${JSON.stringify(row)}).classList.contains('modified')`, 'saved ' + field);
        await waitFor("!document.querySelector('.stat-card [role=status]')", 'autosave complete');
    };
    const setLanguage = async language => {
        await evaluate("document.querySelector('.sidebar-bottom .nav-item').click()"); await waitFor(q('[data-language-select]'), 'language setting');
        await fill('[data-language-select]', language); await waitFor(`document.documentElement.lang === ${JSON.stringify(language)}`, 'language ' + language);
    };
    const close = () => socket.close();
    return { cdp, evaluate, sleep, q, text, waitFor, idle, click, clickText, fill, count, go, lua, screenshot, startup, createProject, openProject, chooseWeapon, setField, setLanguage, close };
}

// Runs a smoke body and prints one JSON line with the result; exits non-zero on failure.
export async function run(name, body) {
    const report = { smoke: name };
    let ui;
    try {
        ui = await connect();
        await body(ui, report);
        report.status = 'PASS';
    } catch (e) {
        report.status = 'FAIL'; report.error = String(e?.stack ?? e);
        try { if (ui) { await ui.screenshot(name + '-failure'); report.page = (await ui.text()).slice(0, 2000); } } catch { /* page gone */ }
    } finally { ui?.close(); }
    console.log(JSON.stringify(report, null, 2));
    if (report.status !== 'PASS') process.exit(1);
}
