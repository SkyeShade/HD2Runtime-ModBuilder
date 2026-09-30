// Developer-only smoke test for UI languages against a running isolated app (WebView2 CDP). Launch the app with
// HD2RUNTIMEGUI_DATA_ROOT=<empty folder>, --sdk-path <HD2Runtime SDK> and WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9242,
// then: HD2GUI_CDP_PORT=9242 node tools/language-smoke.mjs
// It starts in English, creates a project with an unsaved custom Lua edit, switches Settings → Language to 简体中文, checks that the main
// screens re-render in Chinese with the project, the unsaved draft and the SDK intact, then switches back to English.
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
const port = process.env.HD2GUI_CDP_PORT ?? 9242;
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
const go = async target => { await evaluate(`document.querySelector('[data-nav="${target}"]').click()`); await sleep(500); };
const q = selector => `document.querySelector(${JSON.stringify(selector)})`;
const click = async selector => { await evaluate(`(() => { const el = ${q(selector)}; if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.click(); })()`); await sleep(450); };
const fill = async (selector, value) => { await evaluate(`(() => { const el = ${q(selector)}; if (!el) throw new Error('Missing ' + ${JSON.stringify(selector)}); el.value = ${JSON.stringify(value)}; el.dispatchEvent(new Event('input', {bubbles: true})); el.dispatchEvent(new Event('change', {bubbles: true})); })()`); await sleep(550); };
const settings = async () => { await evaluate(`document.querySelector('.sidebar-bottom .nav-item').click()`); await waitFor(q('[data-language-select]'), 'language setting'); };
const cjk = s => (s.match(/[一-鿿]/g) ?? []).length;
const latinWords = s => (s.match(/[A-Za-z]{3,}/g) ?? []).length;
const headings = () => evaluate(`[...document.querySelectorAll('h1, h2, .nav-item span, label, button')].filter(e => e.offsetParent).map(e => e.innerText.trim()).filter(Boolean).join('\\n')`);
const NAV = ['overview', 'player-weapons', 'stratagems:support', 'enemies', 'changes', 'scripting', 'lua', 'export'];
try {
    await waitFor("!!document.querySelector('.desktop-shell') && !document.querySelector('.activity')", 'startup');
    assert.equal(await evaluate('document.documentElement.lang'), 'en', 'starts in English');
    await go('library'); await evaluate("[...document.querySelectorAll('button')].find(b => b.innerText.includes('Create New Mod')).click()"); await waitFor(q('#mod-name'), 'create dialog');
    await fill('#mod-name', 'Language Smoke'); await fill('#author', 'Tests');
    await evaluate("document.querySelector('.dialog form button[type=submit]').click()");
    await waitFor("!!document.querySelector('[data-overview]') && !document.querySelector('.activity')", 'project created');
    const projectTitle = await evaluate(`document.querySelector('.sidebar')?.innerText.includes('Language Smoke')`);
    // An unsaved custom Lua edit, kept across the language change.
    await go('scripting'); await click('[data-custom-lua-add]'); await waitFor(q('[data-lua-input]'), 'editor');
    await evaluate(`(() => { const t = ${q('[data-lua-input]')}; t.focus(); t.setSelectionRange(t.value.length, t.value.length); document.execCommand('insertText', false, '\\n-- unsaved draft line'); })()`); await sleep(600);
    await waitFor(`${q('[data-custom-lua-state]')}`, 'state badge');
    const englishState = await evaluate(`${q('[data-custom-lua-state]')}.innerText`);
    const sdk = await evaluate(`document.querySelector('.sdk-mini')?.innerText`);

    // Switch to Simplified Chinese.
    await settings(); await screenshot('language-settings-en');
    await fill('[data-language-select]', 'zh-Hans');
    await waitFor(`document.documentElement.lang === 'zh-Hans'`, 'html lang follows the language');
    await waitFor(`${q('[data-language-current]')}.dataset.languageCurrent === 'zh-Hans'`, 'setting applied');
    assert(cjk(await headings()) > 20, 'Settings re-rendered in Chinese');
    await screenshot('language-settings-zh');
    const report = [];
    for (const nav of NAV) {
        await go(nav); await sleep(300);
        const text = await headings(); report.push(`${nav}: ${cjk(text)} CJK chars, ${latinWords(text)} Latin words`);
        assert(cjk(text) > 0, `${nav} shows Chinese`);
        await screenshot('language-zh-' + nav.replace(':', '-'));
    }
    // The project, the unsaved draft and the SDK are untouched.
    await go('scripting'); await waitFor(q('[data-lua-input]'), 'editor after switch');
    assert((await evaluate(`${q('[data-lua-input]')}.value`)).includes('-- unsaved draft line'), 'unsaved custom Lua kept');
    assert.notEqual(await evaluate(`${q('[data-custom-lua-state]')}.innerText`), englishState, 'state badge re-rendered in Chinese');
    assert.equal(await evaluate(`document.querySelector('.sidebar')?.innerText.includes('Language Smoke')`), projectTitle, 'project still open');
    assert.equal(await evaluate(`document.querySelector('.sdk-mini')?.innerText.split('\\n')[0]`), sdk.split('\n')[0], 'SDK unchanged');

    // And back to English.
    await settings(); await fill('[data-language-select]', 'en');
    await waitFor(`document.documentElement.lang === 'en'`, 'back to English');
    for (const nav of NAV) { await go(nav); await sleep(250); assert.equal(cjk(await headings()), 0, `${nav} back in English`); }
    await go('scripting'); assert((await evaluate(`${q('[data-lua-input]')}.value`)).includes('-- unsaved draft line'), 'draft still kept');
    assert.equal(await evaluate(`${q('[data-custom-lua-state]')}.innerText`), englishState, 'English state badge');
    console.log(report.join('\n'));
    console.log('PASS: English → 简体中文 → English with project, unsaved custom Lua and SDK intact; html lang follows');
} finally { socket.close(); }
