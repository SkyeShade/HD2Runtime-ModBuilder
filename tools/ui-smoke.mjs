// Developer-only smoke test against a running Debug MAUI app's WebView2 CDP port.
// Node 22+ is used only for UI verification, never by the shipped application.
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
let pages;
for (let attempt = 0; attempt < 60; attempt++) {
    try { pages = await (await fetch(`http://127.0.0.1:${process.env.HD2GUI_CDP_PORT ?? 9223}/json`)).json(); if (pages.length) break; }
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
const click = async text => { await evaluate(`(() => { const el = [...document.querySelectorAll('button')].find(b => b.innerText.trim() === ${JSON.stringify(text)}); if (!el) throw new Error('Missing button: '+${JSON.stringify(text)}); el.click(); })()`); await sleep(200); };
const fill = async (selector, value) => { await evaluate(`(() => { const el=document.querySelector(${JSON.stringify(selector)}); el.value=${JSON.stringify(value)}; el.dispatchEvent(new Event('change',{bubbles:true})); })()`); await sleep(100); };
const screenshot = async name => { await fs.mkdir('docs/screenshots', {recursive:true}); const image=await cdp('Page.captureScreenshot',{format:'png'}); await fs.writeFile('docs/screenshots/'+name+'.png', Buffer.from(image.data,'base64')); };
try {
    await waitFor("!!document.querySelector('.desktop-shell')", 'Blazor initial render');
    await waitFor("!document.querySelector('.activity')", 'startup SDK check');
    console.log(await evaluate('document.body.innerText'));
    if (process.argv.includes('--inspect')) process.exitCode = 0;
    else if (process.argv.includes('--update-scenario')) {
        await click('+ Create New Mod');
        await waitFor("document.querySelector('#dialog-title')?.innerText.includes('SDK update available')", 'outdated SDK warning');
        await screenshot('sdk-update');
        await click('Ignore This Time');
        await waitFor("!!document.querySelector('#mod-name')", 'ignore continues creation');
        assert((await evaluate("document.querySelector('[role=dialog]').innerText")).includes('SDK 0.5.0'));
        await click('Cancel'); await click('+ Create New Mod');
        await waitFor("document.querySelector('#dialog-title')?.innerText.includes('SDK update available')", 'ignore does not suppress the next warning');
        await click('Install Update');
        await waitFor("!!document.querySelector('#mod-name')", 'install continues creation');
        assert((await evaluate("document.querySelector('[role=dialog]').innerText")).includes('SDK 0.5.1'));
        await click('Cancel');
        console.log('PASS: update warning → ignore once → warning again → verified install → create with new SDK');
    } else if (process.argv.includes('--open-export')) {
        await click('Open Export Folder ↗');
        assert(!await evaluate("!!document.querySelector('.notice.error')"));
        console.log('PASS: Open Export Folder invoked successfully');
    } else if (process.argv.includes('--relaunch')) {
        await waitFor("document.querySelector('.project-open')?.innerText.includes('JAR-5 AP4')", 'persisted project card');
        await screenshot('library');
        await click('Open →');
        await evaluate("[...document.querySelectorAll('.nav-item')].find(b=>b.innerText.includes('Changes')).click()");
        await waitFor("document.body.innerText.includes('Armor Penetration')", 'persisted change');
        console.log('PASS: relaunch restored project and modification');
    } else {
        if (await evaluate("!!document.querySelector('[role=dialog]')")) await click('Cancel');
        await screenshot('library-empty');
        await click('+ Create New Mod');
        await waitFor("document.querySelector('#mod-name') || document.body.innerText.includes('Ignore This Time')", 'create SDK check');
        if (await evaluate("!![...document.querySelectorAll('button')].find(b=>b.innerText==='Ignore This Time')")) await click('Ignore This Time');
        await fill('#mod-name','JAR-5 AP4'); await fill('#author','SkyeShade'); await fill('#resource-id','mods/skyeshade/jar5_ap4');
        await fill('#description','JAR-5 armor penetration 3 → 4, configured through SDK metadata.');
        await screenshot('create-project');
        await click('Create project →');
        await waitFor("document.body.innerText.includes('Project overview')", 'project creation');
        await evaluate("[...document.querySelectorAll('.nav-item')].find(b=>b.innerText.includes('Weapons')).click()"); await sleep(150);
        await evaluate("[...document.querySelectorAll('.resource-item')].find(b=>b.innerText.includes('JAR-5 Dominator')).click()"); await sleep(150);
        assert.equal(await evaluate("document.querySelector('#field').value"),'armor_penetration');
        await fill('#modified-value','4'); await screenshot('weapon-editor');
        await click('+ Add Modification');
        await waitFor("document.querySelector('.change-row') !== null", 'change summary');
        assert((await evaluate("document.querySelector('pre').innerText")).includes('value=4'));
        await screenshot('changes');
        await click('↗ Build / Export Mod');
        await waitFor("document.body.innerText.includes('Export complete')", 'ZIP export');
        await screenshot('export');
        console.log('PASS: create → weapon → AP 4 → changes → Lua preview → ZIP export');
    }
} finally { socket.close(); }
